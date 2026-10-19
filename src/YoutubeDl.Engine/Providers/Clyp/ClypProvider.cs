using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Clyp;

public sealed class ClypProvider:IMediaProvider
{
    public MediaProviderInfo Info{get;}=new("clyp","Clyp public audio and selected widgets",200);
    public bool CanHandle(Uri url)=>ClypAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)=>new("clyp",ClypAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        var address=ClypAddress.Parse(request.Url);ct.ThrowIfCancellationRequested();using var anonymous=http.CreateAnonymousSession();
        var response=await anonymous.GetDocumentAsync(address.Api,1024*1024,ct,new Dictionary<string,string>{["Accept"]="application/json"}).ConfigureAwait(false);
        if(response.Url!=address.Api||response.ContentType!="application/json")throw new InvalidDataException("Clyp redirected recording metadata or returned an unsupported document.");
        using var json=JsonDocument.Parse(response.Text,new JsonDocumentOptions{MaxDepth=32});var data=json.RootElement;
        if(data.ValueKind!=JsonValueKind.Object||data.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=data.EnumerateObject().Count())throw new InvalidDataException("Clyp recording metadata is ambiguous.");
        var canonicalUrl=Resource(Text(data,"Url"));var canonical=ClypAddress.Parse(canonicalUrl);
        if(Text(data,"AudioFileId")!=address.Id||canonical.Id!=address.Id||canonical.Widget||canonical.Format is not null||canonicalUrl.Host is not("clyp.it" or "www.clyp.it"))throw new InvalidDataException("Clyp metadata declares another recording identity.");
        var status=Text(data,"Status");if(status is not("Public" or "DownloadDisabled"))throw new NotSupportedException("This Clyp recording does not expose supported public playback.");
        if(address.DownloadGateway&&status!="Public")throw new NotSupportedException("This Clyp recording does not expose a public download gateway.");
        var duration=Number(data,"Duration");if(duration is not >0 or >604800)throw new InvalidDataException("Clyp omitted a supported complete recording duration.");
        var title=Text(data,"Title");if(string.IsNullOrWhiteSpace(title)||title.Length>4096)throw new InvalidDataException("Clyp omitted a supported recording title.");
        var urls=new Dictionary<string,Uri>(StringComparer.Ordinal);
        foreach(var pair in new[]{("mp3","Mp3Url","SecureMp3Url"),("ogg","OggUrl","SecureOggUrl")})
        {
            var plain=Text(data,pair.Item2);var secure=Text(data,pair.Item3);
            if(!string.IsNullOrEmpty(plain))ValidateMedia(plain,address.Id,pair.Item1,requireHttps:false);
            if(!string.IsNullOrEmpty(secure))ValidateMedia(secure,address.Id,pair.Item1,requireHttps:true);
            var selected=!string.IsNullOrEmpty(secure)?secure:!string.IsNullOrEmpty(plain)&&plain.StartsWith("https://",StringComparison.Ordinal)?plain:null;
            if(selected is not null)urls[pair.Item1]=ValidateMedia(selected,address.Id,pair.Item1,true);
        }
        if(urls.Count==0||address.Format is not null&&!urls.ContainsKey(address.Format))throw new NotSupportedException("Clyp did not supply the selected complete secure audio format.");
        if(address.Widget)await ValidateWidgetAsync(address,data,urls,anonymous,ct).ConfigureAwait(false);
        var sources=new List<MediaCandidate>();var formats=new List<NativeFormat>();
        foreach(var pair in urls.Where(pair=>address.Format is null||pair.Key==address.Format))
        {
            using var head=new HttpRequestMessage(HttpMethod.Head,pair.Value);head.Headers.Referrer=address.Web;
            using var descriptor=await anonymous.SendAsync(head,ct).ConfigureAwait(false);descriptor.EnsureSuccessStatusCode();ct.ThrowIfCancellationRequested();
            var mime=descriptor.Content.Headers.ContentType?.MediaType;
            if(descriptor.StatusCode!=HttpStatusCode.OK||(descriptor.RequestMessage?.RequestUri??pair.Value)!=pair.Value||descriptor.Content.Headers.ContentLength is not >0||
                !(pair.Key=="mp3"?mime is "audio/mpeg" or "audio/mp3":mime is "audio/ogg" or "application/ogg"))throw new InvalidDataException("Clyp's supplied audio descriptor changed identity, media type or complete byte count.");
            var size=descriptor.Content.Headers.ContentLength!.Value;
            sources.Add(new(){Id=pair.Key,Url=pair.Value,Kind=MediaKind.Progressive,Source="clyp",Extension=pair.Key,MimeType=pair.Key=="mp3"?"audio/mpeg":"audio/ogg",HasAudio=true,HasVideo=false,IsCompleteMedia=true,FileSize=size,
                ProviderQualityOrder=pair.Key=="mp3"?2:1,VerificationDemuxer=pair.Key,VerificationFileSize=size,VerificationDurationSeconds=duration,VerificationMinimumDurationSeconds=duration,VerificationMinimumDurationToleranceSeconds=0.15});
            formats.Add(new(){Id=pair.Key,Url=pair.Value,Extension=pair.Key,MimeType=pair.Key=="mp3"?"audio/mpeg":"audio/ogg",Protocol="https",AudioOnly=true,HasAudio=true,HasVideo=false,FileSize=size});
        }
        var timestamp=MetadataFacts.Timestamp(Text(data,"DateCreated"));
        return new(){Id=address.Id,ArchiveIdentity=new("clyp",address.Id),Extractor="clyp",Title=title.Trim(),Description=Text(data,"Description"),DurationSeconds=duration,SourcePageUrl=address.Web,
            UploadTimestamp=timestamp,UploadDate=MetadataFacts.Day(timestamp),AnonymousPlayback=true,PlaybackHeaders=new Dictionary<string,string>{["Referer"]=address.Web.AbsoluteUri},Media=sources,ReportedFormats=formats,
            MetadataFields=new Dictionary<string,object?>{["clyp_status"]=status,["download_allowed"]=status=="Public"}};
    }
    private static async Task ValidateWidgetAsync(ClypAddress address,JsonElement data,IReadOnlyDictionary<string,Uri> urls,MediaHttp http,CancellationToken ct)
    {
        var page=await http.GetDocumentAsync(address.Player,256*1024,ct).ConfigureAwait(false);var final=ClypAddress.Parse(page.Url);
        if(final.Id!=address.Id||!final.Widget||page.ContentType is not("text/html" or "application/xhtml+xml"))throw new InvalidDataException("Clyp redirected another selected widget.");
        var clean=Regex.Replace(page.Text,@"<!--.*?-->","",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var bodies=Regex.Matches(clean,@"<script\b[^>]*>(?<body>.*?)</script\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(match=>match.Groups["body"].Value)
            .Where(body=>Regex.IsMatch(body,@"\A\s*createPlayer\s*\(",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))).ToArray();
        if(bodies.Length!=1)throw new InvalidDataException("Clyp did not expose one selected player configuration.");
        var prefix=Regex.Match(bodies[0],@"\A\s*createPlayer\s*\(\s*",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var model=JavaScriptDataLiteral.ObjectArgument(bodies[0],prefix.Length,out var end,ct);
        var tail=bodies[0][end..].TrimStart();if(!tail.StartsWith(");",StringComparison.Ordinal))throw new InvalidDataException("Clyp appended an executable player-data expression.");
        using var parsed=JsonDocument.Parse(model.ToJsonString());var widget=parsed.RootElement;
        var tokenPresent=widget.TryGetProperty("UserProvidedToken",out var token)&&token.ValueKind!=JsonValueKind.Null&&!(token.ValueKind==JsonValueKind.String&&token.GetString()=="");
        if(Text(widget,"AudioFileId")!=address.Id||Number(widget,"Duration") is not >0||Math.Abs(Number(widget,"Duration")!.Value-Number(data,"Duration")!.Value)>0.001||tokenPresent)
            throw new InvalidDataException("Clyp's widget changed recording identity, access or duration.");
        var web=ClypAddress.Parse(Resource(Text(widget,"PlayerPageUrl")));var embedded=ClypAddress.Parse(Resource(Text(widget,"PlayerWidgetUrl")));
        if(web.Id!=address.Id||web.Widget||web.Format is not null||embedded.Id!=address.Id||!embedded.Widget)throw new InvalidDataException("Clyp's player links identify another recording.");
        foreach(var pair in urls)if(Resource(Text(widget,pair.Key=="mp3"?"Mp3Url":"OggUrl"))!=pair.Value)throw new InvalidDataException("Clyp's widget and metadata disagree on playback resources.");
    }
    private static Uri ValidateMedia(string text,string id,string extension,bool requireHttps)
    {
        var url=Resource(text);
        if(requireHttps&&url.Scheme!="https"||url.Host!="a.clyp.it"||url.AbsolutePath!="/"+id+"."+extension)throw new InvalidDataException("Clyp supplied audio for another recording or storage route.");
        return url;
    }
    private static Uri Resource(string? text)=>text is not null&&text.Length<=16384&&Uri.TryCreate(text,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Fragment.Length==0?url:throw new InvalidDataException("Clyp returned an invalid resource address.");
}
