using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.Http;

/// <summary>Matches publisher-declared resources by track and timeline identity, never by a stripped URL.</summary>
internal sealed class AdaptivePlaybackGraph
{
    private sealed record Entry(string Identity,Uri Url,string Facts,bool Manifest=false,string? Slot=null,DateTimeOffset? ProgramDateTime=null,string? ClockIndependentFacts=null);
    private sealed record Node(string Key,string Root,MediaKind Kind,Uri Canonical,Uri Current,string Guard,bool Moving,Dictionary<string,Entry> Entries,
        Dictionary<string,(decimal Start,decimal? Duration)> Periods,DateTimeOffset? Published,bool Terminal);
    private readonly object _sync=new();
    private readonly Dictionary<string,Node> _nodes=new(StringComparer.Ordinal);
    private readonly Dictionary<Uri,Uri> _aliases=new();
    private readonly Dictionary<Uri,Uri?> _current=new();
    private readonly Dictionary<Uri,string> _roots=new();
    private readonly Dictionary<Uri,string> _manifests=new();
    private readonly Dictionary<Uri,string> _recovery=new();
    private const int MaximumAliases=200000;
    internal void Clear(){lock(_sync){_nodes.Clear();_aliases.Clear();_current.Clear();_roots.Clear();_manifests.Clear();_recovery.Clear();}}
    internal bool Contains(Uri url){lock(_sync)return _aliases.ContainsKey(url);}
    internal Uri Canonicalize(Uri url){lock(_sync)return _aliases.GetValueOrDefault(url,url);}
    internal string RecoveryIdentity(Uri url)
    {
        lock(_sync)
        {
            var canonical=_aliases.GetValueOrDefault(url,url);if(!_current.TryGetValue(canonical,out var current)||current is null)throw new InvalidDataException("The recovery resource is no longer in its declared graph.");
            if(!_recovery.TryGetValue(canonical,out var identity))throw new InvalidDataException("The recovery resource has no declared identity.");return identity;
        }
    }
    internal (Uri Url,string Root) Resolve(Uri url)
    {
        lock(_sync){if(!_aliases.TryGetValue(url,out var canonical)||!_current.TryGetValue(canonical,out var current))throw new InvalidDataException("The adaptive resource is outside its declared graph.");
            if(current is null)throw new InvalidDataException("The requested adaptive resource left the renewed timeline.");return(current,_roots[canonical]);}
    }
    internal bool Redirect(Uri source,Uri destination)
    {
        lock(_sync){if(!_aliases.TryGetValue(source,out var canonical)||_aliases.TryGetValue(destination,out var other)&&other!=canonical||_aliases.Count>=MaximumAliases)return false;
            _aliases[destination]=canonical;if(_current[canonical]==source)_current[canonical]=destination;return true;}
    }
    internal void Observe(string root,MediaKind rootKind,Uri canonical,Uri effective,string text)
    {
        lock(_sync)
        {
            var key=root;var kind=rootKind;
            if(_aliases.TryGetValue(canonical,out var original)&&_manifests.TryGetValue(original,out var child)){key=child;kind=MediaKind.Hls;canonical=original;}
            if(kind is not (MediaKind.Hls or MediaKind.Dash))throw new InvalidDataException("The declared resource is not an adaptive manifest.");
            if(key.Length>16384||_nodes.Count>=128&&!_nodes.ContainsKey(key))throw new InvalidDataException("The adaptive manifest graph exceeds its limit.");
            var old=_nodes.GetValueOrDefault(key);
            var next=Parse(key,root,kind,canonical,effective,text,old?.Published is not null);
            if(old is not null)next=Validate(old,next);
            Apply(_nodes.GetValueOrDefault(key),next);_nodes[key]=next;
        }
    }
    internal async Task<Action> PrepareRefreshAsync(IReadOnlyDictionary<string,Uri> roots,Func<Uri,CancellationToken,Task<MediaDocument>>? read,CancellationToken ct,IReadOnlySet<string>? endedRoots=null)
    {
        Node[] previous;lock(_sync)previous=_nodes.Values.OrderBy(n=>n.Key.Length).ToArray();
        if(previous.Length==0){if(endedRoots?.Count>0)throw new InvalidDataException("Ending playback metadata needs an observed complete adaptive graph.");return ()=>{};}
        if(read is null)throw new NotSupportedException("This provider cannot refresh adaptive resource graphs.");
        var fresh=new Dictionary<string,Node>(StringComparer.Ordinal);var locations=new Dictionary<string,Uri>(roots,StringComparer.Ordinal);long bytes=0;
        foreach(var old in previous)
        {
            ct.ThrowIfCancellationRequested();if(!locations.TryGetValue(old.Key,out var location))throw new InvalidDataException("A selected adaptive manifest disappeared during renewal.");
            var document=await read(location,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();bytes+=Encoding.UTF8.GetByteCount(document.Text);if(bytes>32*1024*1024)throw new InvalidDataException("Adaptive renewal documents exceed the shared limit.");
            var next=Validate(old,Parse(old.Key,old.Root,old.Kind,old.Canonical,document.EffectiveUrl??document.Url,document.Text,old.Published is not null));fresh.Add(old.Key,next);
            foreach(var entry in next.Entries.Values)PlaybackResourceExpiry.RequireFresh(entry.Url);
            foreach(var entry in next.Entries.Values.Where(e=>e.Manifest))locations[old.Key+"/"+entry.Identity]=entry.Url;
        }
        if(endedRoots is not null)foreach(var root in endedRoots)
        {
            var nodes=fresh.Values.Where(node=>node.Root==root).ToArray();
            if(!nodes.Any(node=>node.Terminal)||nodes.Any(node=>node.Moving))throw new InvalidDataException("The issuer ended playback before its selected adaptive graphs completed.");
        }
        return ()=>{lock(_sync)
        {
            // Validate the entire generation before changing any live mapping.
            ct.ThrowIfCancellationRequested();
            if(previous.Any(node=>!ReferenceEquals(_nodes.GetValueOrDefault(node.Key),node)))throw new InvalidDataException("The adaptive graph changed while renewal was prepared.");
            var staged=new Dictionary<Uri,Uri?>();var aliases=new Dictionary<Uri,Uri>(_aliases);
            foreach(var next in fresh.Values)Prepare(_nodes[next.Key],next,staged,aliases);
            if(aliases.Count>MaximumAliases)throw new InvalidDataException("The adaptive alias inventory exceeds its limit.");
            foreach(var pair in aliases)_aliases[pair.Key]=pair.Value;foreach(var pair in staged)_current[pair.Key]=pair.Value;
            foreach(var next in fresh.Values){Register(next);_nodes[next.Key]=next;}
        }};
    }
    private static Node Validate(Node old,Node next)
    {
        if(old.Guard!=next.Guard||!old.Moving&&next.Moving)throw new InvalidDataException("Adaptive renewal changed the selected manifest or track identity.");
        if(next.Published<old.Published)throw new InvalidDataException("Adaptive renewal moved the DASH publish clock backwards.");
        var periods=new Dictionary<string,(decimal Start,decimal? Duration)>(old.Periods,StringComparer.Ordinal);
        foreach(var (id,timing) in next.Periods)
        {
            if(old.Periods.TryGetValue(id,out var known))
            {if(known.Start!=timing.Start||known.Duration is { } duration&&timing.Duration!=duration)throw new InvalidDataException("Adaptive renewal changed a known DASH period boundary.");}
            else if(old.Periods.Count>0&&timing.Start<=old.Periods.Values.Max(period=>period.Start))throw new InvalidDataException("Adaptive renewal reused or moved a DASH period backwards.");
            periods[id]=timing;
        }
        if(periods.Count>1000)throw new InvalidDataException("Adaptive renewal exceeds the DASH period history limit.");
        var slots=next.Entries.Values.Where(entry=>entry.Slot is not null).ToDictionary(entry=>entry.Slot!,StringComparer.Ordinal);
        foreach(var entry in old.Entries.Values)
        {
            if(entry.Slot is { } slot&&slots.TryGetValue(slot,out var numbered)&&numbered.Identity!=entry.Identity)throw new InvalidDataException("Adaptive renewal moved a numbered DASH source on its timeline.");
            if(!next.Entries.TryGetValue(entry.Identity,out var current)){if(!old.Moving||entry.Manifest)throw new InvalidDataException("A static or selected adaptive resource disappeared.");continue;}
            // Match the recorder's existing live HLS clock tolerance. Preserve all
            // non-clock facts and the first resource/recovery binding unchanged.
            var reanchored=old.Moving&&entry.ClockIndependentFacts is not null&&entry.ClockIndependentFacts==current.ClockIndependentFacts&&
                entry.ProgramDateTime is { } oldClock&&current.ProgramDateTime is { } newClock&&Math.Abs((oldClock-newClock).TotalSeconds)<=0.05;
            if(entry.Manifest!=current.Manifest||entry.Facts!=current.Facts&&!reanchored)throw new InvalidDataException("Adaptive renewal changed segment timing, range, encryption or format facts.");
        }
        return next with {Periods=periods};
    }
    private void Prepare(Node? old,Node next,Dictionary<Uri,Uri?> staged,Dictionary<Uri,Uri> aliases)
    {
        foreach(var entry in next.Entries.Values)
        {
            var previous=old?.Entries.GetValueOrDefault(entry.Identity);var original=previous?.Url??entry.Url;
            var canonical=previous is null?aliases.GetValueOrDefault(entry.Url,entry.Url):_aliases.GetValueOrDefault(original,original);
            if(staged.TryGetValue(canonical,out var mapped)&&mapped!=entry.Url)throw new InvalidDataException("A shared adaptive resource renewed to conflicting addresses.");
            if(aliases.TryGetValue(entry.Url,out var existing)&&existing!=canonical)throw new InvalidDataException("An adaptive address changed resource identity.");
            aliases[entry.Url]=canonical;aliases[canonical]=canonical;staged[canonical]=entry.Url;
        }
        if(old is not null)foreach(var entry in old.Entries.Values.Where(e=>!next.Entries.ContainsKey(e.Identity)))
        {var canonical=_aliases.GetValueOrDefault(entry.Url,entry.Url);if(!staged.ContainsKey(canonical))staged[canonical]=null;}
    }
    private void Apply(Node? old,Node next)
    {
        var staged=new Dictionary<Uri,Uri?>();var aliases=new Dictionary<Uri,Uri>(_aliases);Prepare(old,next,staged,aliases);
        if(aliases.Count>MaximumAliases)throw new InvalidDataException("The adaptive alias inventory exceeds its limit.");
        foreach(var pair in aliases)_aliases[pair.Key]=pair.Value;foreach(var pair in staged)_current[pair.Key]=pair.Value;Register(next);
    }
    private void Register(Node node)
    {
        foreach(var entry in node.Entries.Values){var canonical=_aliases[entry.Url];_roots[canonical]=node.Root;if(entry.Manifest)_manifests[canonical]=node.Key+"/"+entry.Identity;}
        // Freeze the first validated binding of a physical resource. New period references
        // to that same resource do not redefine an already saved media/init receipt.
        var newResources=node.Entries.Values.Select(entry=>_aliases[entry.Url]).Where(url=>!_recovery.ContainsKey(url)).ToHashSet();
        foreach(var group in _nodes.Values.Where(other=>other.Key!=node.Key).Append(node).SelectMany(other=>other.Entries.Values
            .Where(entry=>newResources.Contains(_aliases[entry.Url])).Select(entry=>(Canonical:_aliases[entry.Url],Facts:Facts(other.Root,other.Key,other.Kind,other.Guard,entry.Identity,entry.Facts)))).GroupBy(item=>item.Canonical))
            _recovery[group.Key]=AdaptiveFragmentCheckpoint.Hash(Facts(group.Select(item=>item.Facts).Order(StringComparer.Ordinal).ToArray()));
    }
    private static string Facts(params object?[] fields)=>JsonSerializer.Serialize(fields);
    private static Node Parse(string key,string root,MediaKind kind,Uri canonical,Uri effective,string text,bool liveProjection=false)
    {
        if(Encoding.UTF8.GetByteCount(text)>8*1024*1024)throw new InvalidDataException("The adaptive manifest exceeds its byte limit.");
        var entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        void Add(string identity,Uri url,string facts,bool manifest=false,string? slot=null,DateTimeOffset? clock=null,string? clockIndependentFacts=null)
        {
            if(url.Scheme!="https"||!url.IsDefaultPort||url.UserInfo.Length>0||url.Fragment.Length>0)throw new InvalidDataException("The adaptive graph contains an insecure resource.");
            if(entries.Count>=100000)throw new InvalidDataException("The adaptive resource graph exceeds its limit.");
            var value=new Entry(identity,url,facts,manifest,slot,clock,clockIndependentFacts);if(entries.TryGetValue(identity,out var prior)){if(prior!=value)throw new InvalidDataException("The manifest has ambiguous resource identities.");}else entries.Add(identity,value);
        }
        string guard;bool moving;bool terminal;var history=new Dictionary<string,(decimal Start,decimal? Duration)>(StringComparer.Ordinal);DateTimeOffset? published=null;
        if(kind==MediaKind.Hls)
        {
            var playlist=HlsPlaylistParser.Parse(text,effective);if(playlist.UnsupportedFeatures.Count>0)throw new NotSupportedException("Renewal does not support this HLS manifest feature.");
            guard=Facts("hls",playlist.IsMaster);moving=!playlist.IsMaster&&!playlist.IsComplete;terminal=!playlist.IsMaster&&playlist.IsComplete;
            foreach(var variant in playlist.Variants){var facts=Facts("variant",variant.Height,variant.Bandwidth,variant.VideoCodec,variant.FramesPerSecond,variant.AudioGroup,variant.SubtitleGroup,variant.ClosedCaptionGroup);Add("variant:"+facts,variant.Url,facts,true);}
            foreach(var audio in playlist.AudioRenditions.Where(a=>a.Url is not null)){var facts=Facts("audio",audio.Group,audio.Name,audio.Language,audio.Default);Add("audio:"+facts,audio.Url!,facts,true);}
            foreach(var caption in playlist.SubtitleRenditions.Where(a=>a.Url is not null)){var facts=Facts("caption",caption.Group,caption.Name,caption.Language,caption.Default,caption.Forced,caption.InBand);Add("caption:"+facts,caption.Url!,facts,true);}
            foreach(var segment in playlist.MediaSegments)
            {
                var identity=segment.DiscontinuitySequence+":"+segment.Sequence;
                var cipher=Facts(segment.Encryption is not null,segment.Encryption?.IV is { } iv?Convert.ToHexString(iv):null);
                Add("segment:"+identity,segment.Url,Facts(segment.DurationSeconds,segment.Range,segment.ProgramDateTime,cipher,segment.Initialization is not null),clock:segment.ProgramDateTime,clockIndependentFacts:Facts(segment.DurationSeconds,segment.Range,cipher,segment.Initialization is not null));
                if(segment.Encryption is { } encryption)Add("key:"+identity,encryption.KeyUrl,cipher);
                if(segment.Initialization is { } init){var initCipher=Facts(init.Encryption is not null,init.Encryption?.IV is { } iv2?Convert.ToHexString(iv2):null);Add("init:"+identity,init.Url,Facts(init.Range,initCipher));if(init.Encryption is { } encrypted)Add("init-key:"+identity,encrypted.KeyUrl,initCipher);}
            }
        }
        else
        {
            DashManifest manifest;try{manifest=DashManifestParser.Parse(text,effective,liveProjection);}catch(NotSupportedException){manifest=DashManifestParser.Parse(text,effective,liveProjection:true);}moving=manifest.IsDynamic;guard=Facts("dash",manifest.AvailabilityStartTime);
            var periods=manifest.Periods.Count>0?manifest.Periods:[new DashPeriod(manifest.PeriodIdentity,manifest.PeriodStartSeconds,null,manifest.Representations){Captions=manifest.CaptionTracks}];
            published=manifest.PublishTime;terminal=!manifest.IsDynamic;
            if(terminal)
            {
                var last=periods[^1];
                terminal=last.DurationSeconds is { } duration&&last.Representations.Count>0&&last.Representations.All(track=>track.Segments.Count>0&&
                    track.Segments.Max(segment=>segment.PresentationStartSeconds+(decimal?)segment.DurationSeconds)>=last.StartSeconds+duration-0.000001m);
            }
            foreach(var period in periods)
            {
                history.Add(period.Id,(period.StartSeconds,period.DurationSeconds));
                foreach(var representation in period.Representations.Concat(period.Captions.Select(c=>c.Representation)))
                {
                    if(representation.UnavailableReason is not null)continue;
                    var identity=Facts(period.Id,period.StartSeconds,representation.Id);var facts=Facts(representation.MimeType,representation.Codecs,representation.Height,representation.Bandwidth,representation.FramesPerSecond,representation.SegmentBase,representation.InlineInitialization is { } bytes?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)):null,representation.Width,representation.Language,representation.Roles,representation.Channels,representation.SamplingRate,representation.Viewpoints,representation.Accessibility);
                    if(representation.Initialization is { } initialization)Add("init:"+identity,initialization,Facts(facts,representation.InitializationRange));
                    if(representation.Segments.Count==0)Add("file:"+identity,representation.Url,facts);
                    for(var i=0;i<representation.Segments.Count;i++)
                    {
                        var segment=representation.Segments[i];
                        // Closing a live period changes its visible interval, not the complete source resource.
                        var sourceStart=segment.SourcePresentationStartSeconds??segment.PresentationStartSeconds-segment.TrimStartSeconds;
                        var sourceDuration=segment.SourceDurationSeconds??segment.DurationSeconds;
                        // Time-addressed sliding timelines may reset their implicit ordinal numbers.
                        var segmentId=Facts(segment.SourceSlot??(object?)segment.Number,sourceStart,segment.Number is null&&sourceStart is null?i:null);
                        Add("segment:"+identity+":"+segmentId,segment.Url,Facts(facts,segment.Range,sourceDuration),slot:segment.SourceSlot is { } slot?Facts(identity,slot):segment.Number is { } number?Facts(identity,number):null);
                    }
                }
            }
        }
        return new(key,root,kind,canonical,effective,guard,moving,entries,history,published,terminal);
    }
}

