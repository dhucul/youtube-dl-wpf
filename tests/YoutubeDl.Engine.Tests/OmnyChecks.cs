using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Omny;

internal static class OmnyChecks
{
    internal const string Org="11111111-1111-4111-8111-111111111111",Program="22222222-2222-4222-8222-222222222222",Playlist="33333333-3333-4333-8333-333333333333",Id="44444444-4444-4444-8444-444444444444",Other="55555555-5555-4555-8555-555555555555";
    internal static string Source(string id=Id,string suffix="audio.mp3")=>"https://traffic.omny.fm/d/clips/"+Org+"/"+Program+"/"+id+"/"+suffix+"?sig=A%2Bb&case=C%2Fd";
    internal static NativeDownloadRequest Request(string directory,bool list=false,bool playlist=false)=>new(){Url=new(list?"https://omny.fm/shows/fixture"+(playlist?"/playlists/set":""):"https://omny.fm/shows/fixture/first"),OutputDirectory=directory,OutputTemplate="omny.%(ext)s",DownloadPlaylist=list};
    internal static JsonNode ProgramData()=>new JsonObject{["Id"]=Program,["OrganizationId"]=Org,["Slug"]="fixture",["Name"]="Fixture program",["Author"]="Publisher",["Language"]="en",["Hidden"]=false,["ModifiedAtUtc"]="2020-01-01T00:00:00Z"};
    internal static JsonNode PlaylistData(int count=2)=>new JsonObject{["Id"]=Playlist,["OrganizationId"]=Org,["ProgramId"]=Program,["ProgramSlug"]="fixture",["Slug"]="set",["Title"]="Fixture playlist",["Visibility"]="Public",["NumberOfClips"]=count,["ModifiedAtUtc"]="2020-01-01T00:00:00Z"};
    internal static JsonNode Clip(string id=Id,double duration=2,bool dynamic=false,string kind="audio")=>new JsonObject
    {
        ["Id"]=id,["OrganizationId"]=Org,["ProgramId"]=Program,["ProgramSlug"]="fixture",["Slug"]=id==Id?"first":"second",["Title"]="Fixture clip",["Description"]="Native clear playback",
        ["PublishState"]="Published",["Visibility"]="Public",["State"]="Ready",["DurationSeconds"]=duration,["PublishedUtc"]="2020-01-01T00:00:00Z",["ContentRating"]="Clean",
        ["PublishedUrl"]="https://omny.fm/shows/fixture/"+(id==Id?"first":"second"),["PlaylistIds"]=new JsonArray(Playlist),["HasPublishedTranscript"]=false,["HasPreRollVideoAd"]=false,
        ["AudioOptions"]=new JsonObject{["IncludeIntroOutro"]=false},["Monetization"]=new JsonObject{["PreRoll"]=dynamic,["PostRoll"]=false,["MidRolls"]=new JsonArray()},
        ["MediaUrls"]=new JsonObject{[kind=="audio"?"AudioUrl":kind=="video"?"VideoUrl":"HlsVideoUrl"]=Source(id,kind=="audio"?"audio.mp3":kind=="video"?"video.mp4":"video.m3u8")},["ModifiedAtUtc"]="2020-01-01T00:00:00Z"
    };
    internal static JsonNode Page(bool playlist=false,string? cursor=null,bool more=false,bool previous=false,params JsonNode[] clips)=>playlist?
        new JsonObject{["Clips"]=new JsonArray(clips.Length==0?[Clip(),Clip(Other)]:clips),["NextClipsAvailable"]=more,["PreviousClipsAvailable"]=previous}:
        new JsonObject{["Clips"]=new JsonArray(clips.Length==0?[Clip(),Clip(Other)]:clips),["TotalCount"]=2,["Cursor"]=cursor};
    internal static IEnumerable<(string Name,Func<Task> Test)> All=>
    [
        ("Omny public clips bind immutable org program clip source and publication facts",Facts),
        ("Omny page JSON widget and ID aliases retain stable archives",Aliases),
        ("Omny restricted manually paged and unsupported routes cannot fall through",Boundaries),
        ("Omny wrong clip org program canonical and source identities fail",Identities),
        ("Omny private unpublished unavailable upcoming and conflicting source records fail",Restrictions),
        ("Omny direct video and HLS descriptors preserve complete source roles",Video),
        ("Omny static transcripts chapters and dynamic timing boundaries remain explicit",Timed),
        ("Omny complete counted programs retain exact cursor scope and original positions",Programs),
        ("Omny ordered playlists use exclusive forward navigation and membership",Playlists),
        ("Omny duplicate changed missing premature and excessive inventories fail",Incomplete),
        ("Omny unavailable listed clips retain original source positions",Positions),
        ("Omny inspected selection refreshes immutable selected playback only",Snapshots),
        ("Omny recorded direct and selected clips skip playback and completion effects",Archives),
        ("Omny public credential isolation cancellation denial and redirects stay bounded",Transport)
    ];
    private static async Task Facts(){var calls=new List<Uri>();var page=await Extract(calls);var media=page.Media.Single();Assert(page.Id==Id&&page.ArchiveIdentity!.Extractor=="omnyfm"&&page.UploadDate==new DateOnly(2020,1,1)&&page.AgeLimit==0&&media.Url.AbsoluteUri==Source()&&media.VerificationMinimumDurationSeconds==2&&media.VerificationDemuxer=="mp3"&&calls.Count==2,"Omny facts changed.");}
    private static async Task Aliases(){foreach(var url in new[]{"https://omny.fm/shows/fixture/first/embed?style=Cover&media=Audio&size=Wide","https://www.omny.fm/shows/fixture/first.json","https://api.omny.fm/orgs/"+Org+"/clips/"+Id}){var page=await Extract([],request:Request(Path.GetTempPath())with{Url=new(url)});Assert(page.Id==Id,"Omny alias changed ID.");}}
    private static Task Boundaries(){foreach(var url in new[]{"https://omny.fm/shows/fixture/first?accessToken=secret","https://omny.fm/shows/fixture/first?clipId="+Other,"https://omny.fm/shows/fixture/first#t=20","https://user:secret@omny.fm/shows/fixture/first","https://omny.fm:8443/shows/fixture/first","https://omny.fm/shows/fixture/first?source=a&source=b"})Assert(!OmnyAddress.Matches(new(url)),"Unsupported Omny URL accepted.");Assert(!new YoutubeDl.Engine.Providers.GenericPageProvider().CanHandle(new("https://omny.fm/private/first")),"Omny private route reached generic extraction.");return Task.CompletedTask;}
    private static async Task Identities(){foreach(var change in new Action<JsonNode>[]{c=>c["Id"]=Other,c=>c["OrganizationId"]=Other,c=>c["ProgramId"]=Other,c=>c["PublishedUrl"]="https://omny.fm/shows/fixture/second",c=>c["MediaUrls"]!["AudioUrl"]=Source(Other)}){var clip=Clip();change(clip);await Expect<InvalidDataException>(()=>Extract([],clip:clip));}}
    private static async Task Restrictions(){foreach(var change in new Action<JsonNode>[]{c=>c["Visibility"]="Private",c=>c["Visibility"]="Restricted",c=>c["PublishState"]="Unpublished",c=>c["State"]="Processing",c=>c["PublishedUtc"]="2099-01-01T00:00:00Z"}){var clip=Clip();change(clip);await Expect<NotSupportedException>(()=>Extract([],clip:clip));}var conflict=Clip();conflict["AudioUrl"]=Source(Other);await Expect<InvalidDataException>(()=>Extract([],clip:conflict));}
    private static async Task Video(){foreach(var kind in new[]{"video","hls"}){var page=await Extract([],clip:Clip(kind:kind));Assert(page.Media.Single().HasVideo==true&&page.Media.Single().HasAudio==true&&page.Media.Single().VerificationMinimumDurationSeconds==2,"Omny video roles/minimum changed.");}}
    private static async Task Timed(){var clip=Clip();clip["HasPublishedTranscript"]=true;clip["HasPreRollVideoAd"]=true;clip["Chapters"]=new JsonArray(new JsonObject{["Name"]="One",["Position"]="00:00:00"},new JsonObject{["Name"]="Two",["Position"]="00:00:01"});var page=await Extract([],clip:clip);Assert(page.Subtitles.Single().Url.Query=="?format=WebVTT"&&page.Chapters.Count==2&&page.Chapters[0].EndSeconds==1,"Static audio was incorrectly gated by a video-only ad flag.");var dynamic=clip.DeepClone();dynamic["Monetization"]!["PreRoll"]=true;await Expect<NotSupportedException>(()=>Extract([],clip:dynamic,request:Request(Path.GetTempPath())with{WriteSubs=true}));var limited=await Extract([],clip:dynamic);Assert(limited.ChapterRefusalReason is not null&&limited.Subtitles.Count==0,"Dynamic timing was silently presented as aligned.");}
    private static async Task Programs(){var calls=new List<Uri>();var page=await Extract(calls,request:Request(Path.GetTempPath(),true),first:Page(cursor:"next",clips:[Clip()]),second:Page(clips:[Clip(Other)]));Assert(page.Entries.Count==2&&page.Entries[1].Position==2&&calls.Any(url=>url.Query.Contains("cursor=next")),"Omny counted program pagination changed.");}
    private static async Task Playlists(){var calls=new List<Uri>();var page=await Extract(calls,request:Request(Path.GetTempPath(),true,true),first:Page(true,more:true,clips:[Clip()]),second:Page(true,previous:true,clips:[Clip(Other)]));Assert(page.Entries.Count==2&&calls.Any(url=>url.Query.Contains("clipId="+Id+"&direction=AfterExclusive")),"Omny playlist order/navigation changed.");}
    private static async Task Incomplete(){await Expect<InvalidDataException>(()=>Extract([],request:Request(Path.GetTempPath(),true),first:Page(cursor:"next",clips:[Clip()]),second:Page(clips:[Clip()])));var missing=Page();((JsonObject)missing).Remove("Cursor");await Expect<InvalidDataException>(()=>Extract([],request:Request(Path.GetTempPath(),true),first:missing));await Expect<NotSupportedException>(()=>Extract([],request:Request(Path.GetTempPath(),true)with{MaximumPlaylistEntries=1}));await Expect<InvalidDataException>(()=>Extract([],request:Request(Path.GetTempPath(),true),recheck:Page(clips:[Clip(Other)])));}
    private static async Task Positions(){var unavailable=Clip();unavailable["State"]="Processing";var page=await Extract([],request:Request(Path.GetTempPath(),true),first:Page(clips:[unavailable,Clip(Other)]));Assert(!page.Entries[0].Available&&page.Entries[1].Position==2,"Omny unavailable source positions changed.");}
    private static async Task Snapshots(){using var files=new Files();var calls=new List<Uri>();var engine=new NativeEngine([new OmnyProvider()],_=>Http(calls));var request=Request(files.Path,true)with{PlaylistItems="2",Simulate=true};var inspected=await engine.InspectAsync(request,default);calls.Clear();var result=await engine.DownloadAsync(request with{PlaylistSnapshot=inspected.PlaylistSnapshot},null,null,default);Assert(result.Succeeded&&calls.Any(url=>url.AbsolutePath.EndsWith("/"+Other))&&!calls.Any(url=>url.AbsolutePath.EndsWith("/"+Id)),"Omny inspected selection refreshed unselected clip.");}
    private static async Task Archives(){using var files=new Files();var archive=Path.Combine(files.Path,"archive.txt");await File.WriteAllTextAsync(archive,"omnyfm "+Id+"\n");var engine=new NativeEngine([new OmnyProvider()],_=>throw new InvalidOperationException("Archived Omny clip contacted API."));var request=Request(files.Path)with{Url=OmnyAddress.ClipId(Org,Id),DownloadArchiveFile=archive};var result=await engine.DownloadAsync(request,null,null,default);Assert(result.Succeeded&&result.Skipped,"Omny ID archive did not skip.");}
    private static async Task Transport(){using var http=Http([],observe:r=>{if(r.Headers.Contains("Authorization")||r.Headers.Contains("Cookie"))throw new InvalidOperationException("Omny public API received credentials.");},options:new(){Headers={["Authorization"]="Bearer fixture",["Cookie"]="private=fixture"}});await new OmnyProvider().ExtractAsync(Request(Path.GetTempPath()),http,default);using var canceled=new CancellationTokenSource();canceled.Cancel();await Expect<OperationCanceledException>(()=>new OmnyProvider().ExtractAsync(Request(Path.GetTempPath()),http,canceled.Token));using var denied=Http([],status:HttpStatusCode.Forbidden);await Expect<HttpRequestException>(()=>new OmnyProvider().ExtractAsync(Request(Path.GetTempPath()),denied,default));}
    private static async Task<PageMetadata> Extract(List<Uri> calls,JsonNode? clip=null,NativeDownloadRequest? request=null,JsonNode? first=null,JsonNode? second=null,JsonNode? recheck=null){using var http=Http(calls,clip:clip,first:first,second:second,recheck:recheck);return await new OmnyProvider().ExtractAsync(request??Request(Path.GetTempPath()),http,default);}
    internal static MediaHttp Http(List<Uri> calls,JsonNode? clip=null,JsonNode? first=null,JsonNode? second=null,JsonNode? recheck=null,Func<HttpRequestMessage,HttpResponseMessage>? media=null,Action<HttpRequestMessage>? observe=null,MediaHttpOptions? options=null,HttpStatusCode status=HttpStatusCode.OK)
    {
        var reads=0;return new(new Handler(r=>{var url=r.RequestUri!;calls.Add(url);observe?.Invoke(r);if(url.Host!="api.omny.fm")return media?.Invoke(r)??throw new InvalidOperationException("Unexpected Omny media request.");JsonNode data;
            if(url.AbsolutePath.EndsWith("/clips")||url.AbsolutePath.EndsWith("/clips/v2")){var list=url.AbsolutePath.Contains("/playlists/");data=url.Query.Contains("cursor=")||url.Query.Contains("clipId=")?second??Page(list):++reads>1&&recheck is not null?recheck:first??Page(list);}
            else if(url.AbsolutePath.EndsWith("/transcript"))return new(status){Content=new StringContent("WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nFirst words\n\n00:00:01.000 --> 00:00:02.000\nLast words\n"),RequestMessage=r};
            else if(url.AbsolutePath.Contains("/clips/"))data=clip??Clip(url.AbsolutePath.EndsWith(Other)?Other:Id);
            else if(url.AbsolutePath.Contains("/playlists/"))data=PlaylistData();else data=ProgramData();return new(status){Content=new StringContent(data.ToJsonString()),RequestMessage=r};}),options??new());
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> run):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(run(request));}}
    private sealed class Files:IDisposable{internal string Path{get;}=System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","omny-"+Guid.NewGuid().ToString("N"));internal Files()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Expect<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
