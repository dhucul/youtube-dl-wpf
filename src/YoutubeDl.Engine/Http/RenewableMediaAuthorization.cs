using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Http;

/// <summary>Renews identity-bound file/caption resources and declared adaptive graphs; issuer credentials stay at the issuer.</summary>
internal sealed class RenewableMediaAuthorization: IPlaybackAuthorization,IDisposable
{
    private sealed record Resource(string Key,Uri Canonical,Uri Current,DateTimeOffset? Expires,MediaCandidate? Format,SubtitleTrack? Caption,bool Retired=false);
    private readonly int _maximumRenewals;
    private readonly string? _videoId;private readonly Func<CancellationToken,Task<PageMetadata>> _refresh;
    private readonly string _extractor;private bool _live;
    private readonly HashSet<string> _active=new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown=new();private bool _disposed,_canceling;private int _operations;
    private readonly Func<Uri,bool>? _redirect;
    private readonly Func<Uri,CancellationToken,Task<MediaDocument>>? _readManifest;
    private readonly AdaptivePlaybackGraph _graph=new();
    private readonly Dictionary<Uri,string> _aliases=new();private readonly Dictionary<string,Resource> _resources=new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate=new(1,1);private long _generation=1;private int _renewals;
    internal RenewableMediaAuthorization(PageMetadata initial,Func<CancellationToken,Task<PageMetadata>> refresh,Func<Uri,bool>? redirect=null,Func<Uri,CancellationToken,Task<MediaDocument>>? readManifest=null,int maximumRenewals=256)
    {
        _maximumRenewals=Math.Clamp(maximumRenewals,1,256);_videoId=initial.Id;_extractor=initial.Extractor;_live=initial.IsLive;_refresh=refresh;_redirect=redirect;_readManifest=readManifest;
        foreach(var entry in Entries(initial)){if(!_resources.TryAdd(entry.Key,entry))throw new InvalidDataException("Playback resources have ambiguous identities.");if(!_aliases.TryAdd(entry.Current,entry.Key))throw new InvalidDataException("Playback resources share an ambiguous address.");}
    }
    public bool AppliesTo(Uri url){lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);return _aliases.ContainsKey(url)||_graph.Contains(url);}}
    internal bool IsActive(string key){lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);return _active.Contains(key);}}
    internal bool IsLive{get{lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);return _live;}}}
    public Uri Canonicalize(Uri url){lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);return _aliases.TryGetValue(url,out var key)?_resources[key].Canonical:_graph.Canonicalize(url);}}
    public string? RecoveryIdentity(Uri url)
    {
        lock(_aliases)
        {
            var resource=Get(url);var identity=_aliases.TryGetValue(url,out var key)?System.Text.Json.JsonSerializer.Serialize(new object?[]{key,resource.Format?.Kind,resource.Format?.Height,resource.Format?.Width,resource.Format?.VideoCodec,resource.Format?.AudioCodec,resource.Format?.FramesPerSecond}):_graph.RecoveryIdentity(url);
            return Download.AdaptiveFragmentCheckpoint.Hash(System.Text.Json.JsonSerializer.Serialize(new[]{_videoId,resource.Key,identity}));
        }
    }
    public async Task ObserveManifestAsync(MediaDocument document,CancellationToken ct)
    {
        using var operation=Begin(ct);ct=operation.Token;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try{lock(_aliases){CheckOperation(ct);var resource=Use(document.Url);var root=_resources[resource.Key];_graph.Observe(root.Key,root.Format!.Kind,document.Url,document.EffectiveUrl??document.Url,document.Text);}}
        finally{_gate.Release();}
    }
    public bool TryRedirect(Uri source,Uri destination)
    {
        if(_redirect?.Invoke(destination)!=true)return false;
        lock(_aliases)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!_aliases.TryGetValue(source,out var key))return _graph.Redirect(source,destination);
            if(_aliases.TryGetValue(destination,out var other)&&other!=key||_aliases.Count>=4096)return false;
            _aliases[destination]=key;if(_resources[key].Current==source)_resources[key]=_resources[key] with {Current=destination,Expires=Expiry(destination)??_resources[key].Expires};return true;
        }
    }
    public async Task<PlaybackLease> AuthorizeAsync(Uri url,CancellationToken ct)
    {
        using var operation=Begin(ct);ct=operation.Token;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Resource resource;lock(_aliases){CheckOperation(ct);resource=Use(url);}
            if(resource.Expires is { } expires&&expires<=DateTimeOffset.UtcNow.AddSeconds(5))await RefreshAsync(ct).ConfigureAwait(false);
            lock(_aliases){CheckOperation(ct);return new(Get(url).Current,_generation);}
        }
        catch(ObjectDisposedException){lock(_aliases){CheckOperation(ct);}throw;}
        finally{_gate.Release();}
    }
    public async Task<PlaybackLease> RenewAsync(Uri url,long rejectedGeneration,CancellationToken ct)
    {
        using var operation=Begin(ct);ct=operation.Token;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool refresh;lock(_aliases){CheckOperation(ct);var resource=Use(url);refresh=_generation==rejectedGeneration||resource.Expires is { } expires&&expires<=DateTimeOffset.UtcNow.AddSeconds(5);}
            if(refresh)await RefreshAsync(ct).ConfigureAwait(false);
            lock(_aliases){CheckOperation(ct);return new(Get(url).Current,_generation);}
        }
        catch(ObjectDisposedException){lock(_aliases){CheckOperation(ct);}throw;}
        finally{_gate.Release();}
    }
    private Resource Use(Uri url){lock(_aliases){var resource=Get(url);_active.Add(resource.Key);return resource;}}
    private Resource Get(Uri url)
    {
        lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);if(_aliases.TryGetValue(url,out var key)){var value=_resources[key];if(value.Retired)throw new InvalidDataException("The requested unused playback resource was retired by its issuer.");return value;}var adaptive=_graph.Resolve(url);var root=_resources[adaptive.Root];if(root.Retired)throw new InvalidDataException("The adaptive playback resource was retired.");return new(root.Key,_graph.Canonicalize(url),adaptive.Url,Expiry(adaptive.Url)??root.Expires,null,null);}
    }
    private async Task RefreshAsync(CancellationToken ct)
    {
        lock(_aliases){CheckOperation(ct);if(++_renewals>_maximumRenewals)throw new NotSupportedException("This job exceeded its bounded playback refresh count.");}
        var page=await _refresh(ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();if(page.Id!=_videoId||page.Extractor!=_extractor||!_live&&page.IsLive||page.IsPlaylist||page.DrmRefused)throw new InvalidDataException("Playback renewal changed identity or denied access.");
        var entries=Entries(page).ToDictionary(r=>r.Key,StringComparer.Ordinal);var next=new Dictionary<string,Resource>(StringComparer.Ordinal);
        if(entries.Values.Select(resource=>resource.Current).Distinct().Count()!=entries.Count)throw new InvalidDataException("Renewed playback resources share an ambiguous address.");
        Resource[] previous;HashSet<string> active;lock(_aliases){CheckOperation(ct);previous=_resources.Values.ToArray();active=new(_active,StringComparer.Ordinal);}
        foreach(var old in previous)
        {
            var valid=entries.TryGetValue(old.Key,out var entry)&&Same(old.Format,entry.Format)&&(old.Caption is not { } caption||entry.Caption is not null&&caption.Language==entry.Caption.Language&&caption.Extension==entry.Caption.Extension&&caption.Automatic==entry.Caption.Automatic)&&!(entry.Expires is { } expires&&expires<=DateTimeOffset.UtcNow);
            if(!valid)
            {
                if(active.Contains(old.Key))throw new InvalidDataException("A used playback resource changed, expired or disappeared during renewal.");
                next.Add(old.Key,old with {Retired=true});continue;
            }
            next.Add(old.Key,entry! with {Canonical=old.Canonical});
        }
        HashSet<string>? ended=null;
        if(_live&&!page.IsLive)
        {
            var media=previous.Where(resource=>active.Contains(resource.Key)&&resource.Format is not null).ToArray();
            if(media.Length==0||media.Any(resource=>resource.Format!.Kind is not (MediaKind.Hls or MediaKind.Dash)))throw new InvalidDataException("Ending playback metadata needs selected adaptive completion evidence.");
            ended=media.Select(resource=>resource.Key).ToHashSet(StringComparer.Ordinal);
        }
        var commitGraph=await _graph.PrepareRefreshAsync(next.Where(p=>!p.Value.Retired).ToDictionary(p=>p.Key,p=>p.Value.Current,StringComparer.Ordinal),_readManifest,ct,ended).ConfigureAwait(false);
        lock(_aliases)
        {
            CheckOperation(ct);
            if(_aliases.Count+next.Count>4096)throw new InvalidDataException("The playback alias inventory exceeds its limit.");
            foreach(var entry in next.Values.Where(resource=>!resource.Retired)){if(_aliases.TryGetValue(entry.Current,out var key)&&key!=entry.Key)throw new InvalidDataException("A renewed URL changed its resource identity.");}
            // Root URLs and adaptive mappings become visible under the same owner lock.
            // A rejected/canceled root generation cannot leave accepted fragment mappings behind.
            commitGraph();
            foreach(var entry in next.Values){_resources[entry.Key]=entry;if(!entry.Retired)_aliases[entry.Current]=entry.Key;}_live=page.IsLive;_generation++;
        }
    }
    private static bool Same(MediaCandidate? a,MediaCandidate? b)=>a is null?b is null:b is not null&&a.Kind==b.Kind&&a.Extension==b.Extension&&a.MimeType==b.MimeType&&a.Height==b.Height&&a.Width==b.Width&&a.VideoCodec==b.VideoCodec&&a.AudioCodec==b.AudioCodec&&a.HasAudio==b.HasAudio&&a.HasVideo==b.HasVideo&&a.IsCompleteMedia==b.IsCompleteMedia&&a.FramesPerSecond==b.FramesPerSecond&&a.FileSize==b.FileSize&&a.AudioLanguage==b.AudioLanguage&&a.IsDefaultAudio==b.IsDefaultAudio;
    private static IEnumerable<Resource> Entries(PageMetadata page)
    {
        foreach(var format in page.Media.Where(m=>m.Kind is MediaKind.Progressive or MediaKind.Hls or MediaKind.Dash))
        {Valid(format.Url);yield return new("format:"+(format.Id??throw new InvalidDataException("Renewable media requires format identities.")),format.Url,format.Url,format.ResourceExpires??Expiry(format.Url),format,null);}
        foreach(var caption in page.Subtitles){Valid(caption.Url);yield return new("caption:"+(caption.ResourceIdentity??caption.Language+":"+caption.Extension+":"+caption.Automatic),caption.Url,caption.Url,caption.ResourceExpires??Expiry(caption.Url),null,caption);}
    }
    internal static DateTimeOffset? Expiry(Uri url)
    {
        return PlaybackResourceExpiry.Read(url);
    }
    private static void Valid(Uri url){if(url.Scheme!="https"||!url.IsDefaultPort||url.UserInfo.Length>0||url.Fragment.Length>0)throw new InvalidDataException("A renewable playback resource requires a secure ordinary address.");}
    // Called under _aliases for already admitted operations. A semaphore waiter
    // can acquire its permit while cancellation races the previous holder's exit.
    // Disposal can also be marked before shutdown callbacks have canceled ct.
    private void CheckOperation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(_disposed)throw new OperationCanceledException("The playback session was disposed.",ct);
    }
    private Operation Begin(CancellationToken ct)
    {
        lock(_aliases){ObjectDisposedException.ThrowIf(_disposed,this);ct.ThrowIfCancellationRequested();var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_shutdown.Token);_operations++;return new(this,linked);}
    }
    private void End()
    {
        lock(_aliases){_operations--;if(_disposed&&!_canceling&&_operations==0){_gate.Dispose();_shutdown.Dispose();}}
    }
    private sealed class Operation(RenewableMediaAuthorization owner,CancellationTokenSource cancellation):IDisposable
    {internal CancellationToken Token=>cancellation.Token;public void Dispose(){cancellation.Dispose();owner.End();}}
    public void Dispose()
    {
        lock(_aliases){if(_disposed)return;_disposed=true;_canceling=true;}
        try{_shutdown.Cancel();}
        finally{lock(_aliases){_canceling=false;_aliases.Clear();_resources.Clear();_active.Clear();_graph.Clear();if(_operations==0){_gate.Dispose();_shutdown.Dispose();}}}
    }
}
