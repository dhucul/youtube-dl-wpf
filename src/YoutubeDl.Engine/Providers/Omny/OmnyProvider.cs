using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Omny;

public sealed partial class OmnyProvider : IMediaProvider
{
    private const string Api="https://api.omny.fm/";
    public MediaProviderInfo Info{get;}=new("omnyfm","Omny public clips programs playlists and players",200);
    public bool CanHandle(Uri url)=>OmnyAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request){var address=OmnyAddress.Parse(request.Url);return address.Ids&&address.Clip is {} id?new("omnyfm",id):null;}
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        var address=OmnyAddress.Parse(request.Url);using var anonymous=http.CreateAnonymousSession();
        if(address.Clip is {} clip)
        {
            var endpoint=address.Ids?OmnyAddress.ClipId(address.Organization!,clip):new Uri(Api+"programs/"+address.Program+(address.Playlist is null?"/clips/":"/playlists/"+address.Playlist+"/clips/")+clip);
            using var document=await JsonAsync(endpoint,anonymous,ct).ConfigureAwait(false);var root=document.RootElement;
            var id=Id(root);var org=Id(root,"OrganizationId");var program=Id(root,"ProgramId");var alias=Text(root,"ProgramSlug")??throw new InvalidDataException("Omny omitted the owning program slug.");
            if(address.Ids?(id!=clip||org!=address.Organization):(Text(root,"Slug")!=clip||alias!=address.Program))throw new InvalidDataException("Omny returned another requested clip or program.");
            using var owner=await JsonAsync(new(Api+"programs/"+Uri.EscapeDataString(alias)),anonymous,ct).ConfigureAwait(false);ValidateProgram(owner.RootElement,alias,org,program);
            if(address.Playlist is {} playlist)
            {
                using var membership=await JsonAsync(new(Api+"programs/"+address.Program+"/playlists/"+playlist),anonymous,ct).ConfigureAwait(false);
                ValidatePlaylist(membership.RootElement,address.Program!,playlist,org,program);if(!PlaylistIds(root).Contains(Id(membership.RootElement),StringComparer.Ordinal))throw new InvalidDataException("Omny returned a clip outside the requested playlist.");
            }
            return Clip(root,owner.RootElement,request);
        }
        return await CollectionAsync(address,request,anonymous,ct).ConfigureAwait(false);
    }
    private static void ValidateProgram(JsonElement root,string alias,string? org=null,string? program=null)
    {
        if(Text(root,"Slug")!=alias||org is not null&&Id(root,"OrganizationId")!=org||program is not null&&Id(root)!=program||string.IsNullOrWhiteSpace(Text(root,"Name")))throw new InvalidDataException("Omny returned another or unidentified program.");
        _=Id(root);_=Id(root,"OrganizationId");if(Flag(root,"Hidden"))throw new NotSupportedException("This hidden Omny program needs additional access support.");
    }
    private static void ValidatePlaylist(JsonElement root,string alias,string? slug,string org,string program)
    {
        if(Id(root,"OrganizationId")!=org||Id(root,"ProgramId")!=program||Text(root,"ProgramSlug")!=alias||slug is not null&&Text(root,"Slug")!=slug)
            throw new InvalidDataException("Omny returned another playlist owner or slug.");
        _=Id(root);if(Text(root,"Visibility") is not ("Public" or "Unlisted"))throw new NotSupportedException("This Omny playlist requires additional access support.");
    }
    private static PageMetadata Clip(JsonElement root,JsonElement program,NativeDownloadRequest request)
    {
        var id=Id(root);var org=Id(root,"OrganizationId");var owner=Id(root,"ProgramId");
        if(Text(root,"PublishState")!="Published"||Text(root,"Visibility") is not ("Public" or "Unlisted")||Text(root,"State")!="Ready")throw new NotSupportedException("This Omny clip is not an available published recording.");
        var duration=Number(root,"DurationSeconds");if(duration is not >0 or >604800)throw new InvalidDataException("Omny did not declare a supported complete duration.");
        var published=MetadataFacts.Timestamp(Text(root,"PublishedUtc"));if(published is null||published>DateTimeOffset.UtcNow)throw new NotSupportedException("This Omny clip is not yet published.");
        var dynamic=Dynamic(root);var media=new List<MediaCandidate>();var descriptors=Object(root,"MediaUrls");
        AddSource("AudioUrl",false,MediaKind.Progressive,"mp3","mp3");AddSource("VideoUrl",true,MediaKind.Progressive,"mp4","mov");AddSource("HlsVideoUrl",true,MediaKind.Hls,"ts","mpegts");
        if(media.Count==0)throw new NotSupportedException("Omny did not offer a complete clear audio or video source.");
        var videoTiming=!request.AudioOnly&&request.FormatId!="audio"&&request.FormatSelector!="audio"&&media.Any(candidate=>candidate.HasVideo==true);
        var dynamicTiming=dynamic||videoTiming&&Flag(root,"HasPreRollVideoAd",optional:true);
        var chapters=new List<ChapterMarker>();string? chapterError=null;
        try
        {
            var markers=Array(root,"Chapters");if(markers.Length>1000)throw new InvalidDataException();
            if(dynamicTiming&&markers.Length>0)throw new NotSupportedException("Omny chapter alignment after dynamic ads requires additional native support.");
            foreach(var marker in markers)
            {
                var position=Duration(Text(marker,"Position"));var title=Text(marker,"Name");
                if(position is null||position<0||position>=duration||string.IsNullOrWhiteSpace(title)||chapters.Count>0&&position<=chapters[^1].StartSeconds)throw new InvalidDataException("Omny returned invalid chapter timing.");
                if(chapters.Count>0){var prior=chapters[^1];chapters[^1]=new(){Title=prior.Title,StartSeconds=prior.StartSeconds,EndSeconds=position.Value};}
                chapters.Add(new(){Title=title,StartSeconds=position.Value,EndSeconds=duration.Value});
            }
        }
        catch(Exception ex)when(ex is InvalidDataException or NotSupportedException){chapters=[];chapterError=ex is NotSupportedException?ex.Message:"Omny chapter metadata is invalid; requested embedding cannot proceed.";}
        var captions=new List<SubtitleTrack>();var warnings=new List<string>();
        if(Flag(root,"HasPublishedTranscript"))
        {
            if(dynamicTiming)
            {
                if(request.WriteSubs||request.WriteAutoSubs||request.EmbedSubs)throw new NotSupportedException("Omny transcript alignment after dynamic ads requires additional native support.");
                warnings.Add("Omny advertises a transcript; alignment for this dynamic delivery remains pending.");
            }
            else
            {
                var language=Text(program,"Language")??"und";if(!Regex.IsMatch(language,@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))throw new InvalidDataException("Omny returned an invalid transcript language.");
                captions.Add(new(){Url=new(Api+"orgs/"+org+"/clips/"+id+"/transcript?format=WebVTT"),Language=language,Extension="vtt"});
            }
        }
        if(chapterError is not null)warnings.Add(chapterError);
        var canonical=Published(root,id,org,owner);
        return new(){Id=id,Extractor="omnyfm",ArchiveIdentity=new("omnyfm",id),Title=Text(root,"Title")??id,Description=Text(root,"Description")??Text(root,"DescriptionHtml"),
            Uploader=Text(program,"Author")??Text(program,"Name"),DurationSeconds=duration,UploadTimestamp=published,UploadDate=MetadataFacts.Day(published),
            AgeLimit=Text(root,"ContentRating") switch{"Explicit"=>18,"Clean"=>0,_=>null},ThumbnailUrl=Image(Text(root,"ImageUrl")),Media=media,
            SourcePageUrl=canonical,AnonymousPlayback=true,Subtitles=captions,Chapters=chapters,ChapterRefusalReason=chapterError,Warnings=warnings,
            MetadataFields=new Dictionary<string,object?>{["tags"]=root.TryGetProperty("Tags",out var tags)?tags.Clone():null,["season_number"]=Integer(root,"Season"),["episode_number"]=Integer(root,"Episode")}};
        void AddSource(string field,bool video,MediaKind kind,string extension,string demuxer)
        {
            var nested=Text(descriptors,field);var top=Text(root,field);var raw=nested??top;if(string.IsNullOrWhiteSpace(raw))return;
            var source=MediaUrl(raw,org,owner,id);
            if(nested is not null&&top is not null&&MediaUrl(top,org,owner,id)!=source)throw new InvalidDataException("Omny returned conflicting offered source descriptors.");
            media.Add(new(){Id=field=="AudioUrl"?"audio":field=="VideoUrl"?"video":"hls-video",Url=source,Kind=kind,Extension=extension,Source="omnyfm",
                MimeType=kind==MediaKind.Hls?"application/vnd.apple.mpegurl":video?"video/mp4":"audio/mpeg",HasAudio=true,HasVideo=video,IsCompleteMedia=true,
                FileSize=video?null:Long(root,"PublishedAudioSizeInBytes"),VerificationDemuxer=demuxer,VerificationMinimumDurationSeconds=duration,
                VerificationFileSize=!video&&!dynamic?Long(root,"PublishedAudioSizeInBytes"):null});
        }
    }
    private static bool Dynamic(JsonElement root)=>Flag(Object(root,"AudioOptions"),"IncludeIntroOutro",optional:true)||
        Flag(Object(root,"Monetization"),"PreRoll",optional:true)||Flag(Object(root,"Monetization"),"PostRoll",optional:true)||Array(Object(root,"Monetization"),"MidRolls").Length>0||Array(root,"AdMarkers").Length>0;
    private static bool Flag(JsonElement root,string field,bool optional=false)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(field,out var value)){if(optional)return false;throw new InvalidDataException("Omny omitted an explicit public state.");}
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False?value.GetBoolean():throw new InvalidDataException("Omny returned an invalid public state.");
    }
    private static string Id(JsonElement root,string field="Id")=>Text(root,field) is {} id&&OmnyAddress.Uuid(id)?id.ToLowerInvariant():throw new InvalidDataException("Omny returned an invalid immutable identity.");
    private static string[] PlaylistIds(JsonElement root)=>Array(root,"PlaylistIds").Select(value=>value.ValueKind==JsonValueKind.String&&OmnyAddress.Uuid(value.GetString()!)?value.GetString()!.ToLowerInvariant():throw new InvalidDataException("Omny returned an invalid playlist identity.")).ToArray();
    private static Uri PublicUrl(string? raw)=>Uri.TryCreate(raw,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Fragment.Length==0&&raw!.Length<=32768?url:throw new InvalidDataException("Omny returned an invalid public resource.");
    private static Uri MediaUrl(string raw,string org,string program,string clip)
    {
        var url=PublicUrl(raw);
        if(url.Host is not ("traffic.omny.fm" or "www.omnycontent.com" or "omnycontent.com")||!url.AbsolutePath.StartsWith("/d/clips/"+org+"/"+program+"/"+clip+"/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Omny returned media outside the requested immutable clip.");
        return url;
    }
    private static string? Image(string? raw){if(string.IsNullOrWhiteSpace(raw))return null;var url=PublicUrl(raw);if(url.Host is not ("www.omnycontent.com" or "omnycontent.com"))throw new NotSupportedException("This Omny image origin needs additional support.");return url.AbsoluteUri;}
    private static Uri Published(JsonElement root,string id,string org,string program)
    {
        var url=PublicUrl(Text(root,"PublishedUrl"));var parsed=OmnyAddress.Parse(url);
        if(parsed.Ids||parsed.Program!=Text(root,"ProgramSlug")||parsed.Clip!=Text(root,"Slug"))throw new InvalidDataException("Omny returned another canonical clip address.");
        return url;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint,MediaHttp http,CancellationToken ct)
    {
        var response=await http.GetDocumentAsync(endpoint,8*1024*1024,ct).ConfigureAwait(false);if(response.Url!=endpoint)throw new InvalidDataException("Omny redirected a public API identity.");
        JsonDocument document;try{document=JsonDocument.Parse(response.Text,new(){MaxDepth=64});}catch(JsonException){throw new InvalidDataException("Omny returned malformed public metadata.");}
        try
        {
            var nodes=new Stack<JsonElement>();nodes.Push(document.RootElement);var count=0;
            while(nodes.TryPop(out var node))
            {
                ct.ThrowIfCancellationRequested();if(++count>200000)throw new InvalidDataException("Omny returned excessive metadata fields.");
                if(node.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var field in node.EnumerateObject()){if(!names.Add(field.Name))throw new InvalidDataException("Omny repeated a metadata field.");nodes.Push(field.Value);}}
                else if(node.ValueKind==JsonValueKind.Array)foreach(var child in node.EnumerateArray())nodes.Push(child);
            }
            return document;
        }
        catch{document.Dispose();throw;}
    }
}
