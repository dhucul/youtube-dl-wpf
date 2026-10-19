using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.YouTube;

internal static class MetadataSelectionChecks
{
    private static readonly Uri Url = new("https://metadata.example/video");
    private static NativeDownloadRequest Request(string output = "") => new() { Url = Url, OutputDirectory = output.Length == 0 ? Path.GetTempPath() : output, OutputTemplate = "%(id)s.%(ext)s", DownloadPlaylist = false };
    private static PageMetadata Facts() => new()
    {
        Id = "01", Title = "Cats & Dogs", Description = "Cats & dogs are here", Extractor = "metadata", Uploader = "Public Creator", DurationSeconds = 75,
        UploadDate = new(2026, 1, 31), ViewCount = 250, AgeLimit = 12,
        UploadTimestamp = DateTimeOffset.Parse("2026-01-31T12:00:00.250Z", System.Globalization.CultureInfo.InvariantCulture),
        MetadataFields = new Dictionary<string,object?> { ["like_count"] = 0L, ["tags"] = JsonSerializer.SerializeToElement(new[] { "cats", "dogs" }) },
        Media = [new() { Id = "one", Url = new("https://metadata.example/file.mp4"), Extension = "mp4", HasVideo = true, HasAudio = true }]
    };
    private static bool Accept(NativeDownloadRequest request, PageMetadata? facts = null, bool incomplete = false) =>
        new DownloadSelectionState(request).Rejection(facts ?? Facts(), request, default, incomplete) is null;
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Metadata selection exact and inclusive date ranges preserve explicit-date precedence", Dates),
        ("Metadata selection relative UTC dates use calendar months and leap-year clamping", Relative),
        ("Metadata selection missing legacy date views and age remain permitted", Missing),
        ("Metadata selection known view age boundaries include zero and full-width counts", Bounds),
        ("Metadata selection numeric predicates retain units decimal precision and duration values", Numbers),
        ("Metadata selection strings retain original types case and comparison operators", Strings),
        ("Metadata selection presence distinguishes missing boolean false and numeric zero", Presence),
        ("Metadata selection escaped conjunctions quotes and regexes remain declarative", Escapes),
        ("Metadata selection repeated alternatives and explicit reset preserve argument order", Alternatives),
        ("Metadata selection rejection has no media output sidecar archive or callback effects", SideEffects),
        ("Metadata selection simulation limits count accepted items and preserve source positions", Limits),
        ("Metadata selection invalid filters dates and bounds fail before any HTTP access", Invalid),
        ("Metadata selection regex timeouts stop rather than repeatedly failing an ignored playlist", RegexTimeout),
        ("Metadata selection provider facts survive native inspection templates and info JSON", Publication),
        ("Metadata selection YouTube facts reject before observed browser playback", YouTubeEarly),
        ("Metadata selection public feed and schema facts use publisher dates and counters", PublisherFacts)
    ];
    private static Task Dates()
    {
        Assert(Accept(Request() with { Date = "20260131" }), "Exact upload date did not match.");
        Assert(!Accept(Request() with { DateBefore = "20260130" }), "Inclusive latest date admitted a later upload.");
        Assert(Accept(Request() with { DateAfter = "20260131", DateBefore = "20260131" }), "Inclusive range excluded its boundary.");
        Assert(Accept(Request() with { Date = "20260131", DateBefore = "invalid", DateAfter = "20270101" }), "Explicit date did not take precedence over the range.");
        return Task.CompletedTask;
    }
    private static Task Relative()
    {
        var today = new DateOnly(2024, 3, 31);
        Assert(NativeDateRange.Parse("today-1month", today) == new DateOnly(2024, 2, 29) && NativeDateRange.Parse("yesterday-2weeks", today) == new DateOnly(2024, 3, 16), "Calendar or yesterday-relative date arithmetic changed.");
        Assert(NativeDateRange.Parse("now", today) == today && NativeDateRange.Parse("today-1year", new(2024,2,29)) == new DateOnly(2023,2,28), "Now or leap-year clamping changed.");
        return Task.CompletedTask;
    }
    private static Task Missing()
    {
        var empty = Facts() with { UploadDate = null, UploadTimestamp = null, ViewCount = null, AgeLimit = null };
        Assert(Accept(Request() with { Date = "20200101", MinimumViews = 100, MaximumViews = 200, MaximumAge = 0 }, empty), "Missing legacy metadata was treated as zero or rejected.");
        Assert(!Accept(Request() with { MatchFilter = "view_count>100" }, empty) && Accept(Request() with { MatchFilter = "view_count>?100" }, empty), "Missing-inclusive comparison semantics changed.");
        Assert(Accept(Request() with { MatchFilter = "view_count>100" }, empty, true), "Incomplete player facts were rejected before they could be refreshed.");
        return Task.CompletedTask;
    }
    private static Task Bounds()
    {
        Assert(Accept(Request() with { MinimumViews = 250, MaximumViews = 250, MaximumAge = 12 }), "An exact view/age boundary was rejected.");
        Assert(!Accept(Request() with { MinimumViews = 251 }) && !Accept(Request() with { MaximumAge = 11 }), "Known bounds were ignored.");
        Assert(Accept(Request() with { MaximumViews = 0, MaximumAge = 0 }, Facts() with { ViewCount = 0, AgeLimit = 0 }), "Explicit zero bounds became disabled.");
        Assert(Accept(Request() with { MatchFilter = "view_count=9223372036854775807" }, Facts() with { ViewCount = long.MaxValue }), "A full-width view count lost numeric precision.");
        return Task.CompletedTask;
    }
    private static Task Numbers()
    {
        Assert(Accept(Request() with { MatchFilter = "duration>=1:00 & duration<90 & view_count<1K" }), "Units or colon-duration comparisons failed.");
        Assert(Accept(Request() with { MatchFilter = "duration>75.1 & duration<75.9" }, Facts() with { DurationSeconds = 75.25 }), "Numeric threshold rounding changed fractional source metadata.");
        Assert(!Accept(Request() with { MatchFilter = "view_count<1K" }, Facts() with { ViewCount = 1010 }) && Accept(Request() with { MatchFilter = "view_count<1KiB" }, Facts() with { ViewCount = 1010 }), "Decimal and binary units were conflated.");
        Assert(!Accept(Request() with { MatchFilter = "view_count!=250" }) && Accept(Request() with { MatchFilter = "view_count!<250" }), "Negated numeric comparisons changed.");
        return Task.CompletedTask;
    }
    private static Task Strings()
    {
        Assert(!Accept(Request() with { MatchFilter = "id=1" }) && Accept(Request() with { MatchFilter = "id='01'" }), "String IDs were coerced to numbers.");
        Assert(Accept(Request() with { MatchFilter = "uploader^=Public & uploader$=Creator & uploader*=lic & upload_date>=20260131" }), "String operators or date-string ordering failed.");
        Assert(!Accept(Request() with { MatchFilter = "uploader*=public" }) && Accept(Request() with { MatchFilter = "tags*=cats" }), "Case or list-membership behavior changed.");
        return Task.CompletedTask;
    }
    private static Task Presence()
    {
        Assert(Accept(Request() with { MatchFilter = "!is_live & like_count & !missing_field" }), "Boolean false, present numeric zero or absent-field conditions changed.");
        Assert(!Accept(Request() with { MatchFilter = "is_live" }) && !Accept(Request() with { MatchFilter = "!like_count" }), "Boolean/presence conditions became ordinary truthiness.");
        Assert(Accept(Request() with { MatchFilter = "description" }, Facts() with { Description = "" }), "An empty present string became missing.");
        return Task.CompletedTask;
    }
    private static Task Escapes()
    {
        Assert(Accept(Request() with { MatchFilter = @"description~='(?i)\bcats \& dogs\b' & title='Cats \& Dogs'" }), "Escaped conjunctions or regex boundaries were discarded.");
        Assert(Accept(Request() with { MatchFilter = @"title='A \'quote\''" }, Facts() with { Title = "A 'quote'" }), "Escaped quotes were not literal.");
        return Task.CompletedTask;
    }
    private static Task Alternatives()
    {
        var request = NativeExtraArguments.Apply(Request() with { MatchFilter = "view_count>1000" }, ["--match-filters", "duration>70", "--match-filter", "is_live"]);
        Assert(Accept(request), "Repeated metadata filters were combined as conjunctions or replaced the saved filter.");
        var reset = NativeExtraArguments.Apply(request, ["--no-match-filters", "--match-filters", "duration<70"]);
        Assert(!Accept(reset) && reset.MatchFilter == "" && reset.MatchFilters.Count == 1, "Explicit reset did not clear saved and earlier alternatives.");
        var values = NativeExtraArguments.Apply(Request(), ["--date", "20260131", "--datebefore", "20250101", "--min-views", "2", "--max-views", "9", "--age-limit", "0"]);
        Assert(values.Date == "20260131" && values.MinimumViews == 2 && values.MaximumViews == 9 && values.MaximumAge == 0, "Typed selection extra arguments lost requested values.");
        return Task.CompletedTask;
    }
    private static async Task SideEffects()
    {
        using var files = new Files(); var media = 0; var callbacks = 0;
        var engine = Engine(Facts(), _ => media++);
        var result = await engine.DownloadAsync(Request(files.Path) with { MatchFilter = "duration>100", ForceWriteArchive = true, WriteInfoJson = true,
            WriteDescription = true, WriteThumbnail = true, DownloadArchiveFile = Path.Combine(files.Path,"archive.txt") }, null, null, default, _ => callbacks++);
        Assert(result.Succeeded && result.Skipped && result.Filtered && media == 0 && callbacks == 0 && !Directory.EnumerateFileSystemEntries(files.Path).Any(), "Metadata rejection caused file or archive side effects.");
    }
    private static async Task Limits()
    {
        using var files = new Files(); var leaf = Facts(); var entries = Enumerable.Range(1,3).Select(index => new NativePlaylistEntry(index,index.ToString(),"Listing",Url,null,null,true,
            leaf with { Id=index.ToString(),ViewCount=index==1?10:500 })).ToArray();
        var page = new PageMetadata { Id="list",Title="List",IsPlaylist=true,Entries=entries };
        var logs = new List<string>(); var result = await Engine(page).DownloadAsync(Request(files.Path) with { DownloadPlaylist=true,Simulate=true,MinimumViews=100,MaxDownloads=1 },null,logs.Add,default);
        Assert(result.Succeeded && result.DownloadLimitReached && logs.Count(line=>line.StartsWith("[native] simulated "))==1, "Filtered metadata consumed the completed-item limit.");
    }
    private static async Task Invalid()
    {
        var engine = new NativeEngine(httpFactory:_=>throw new InvalidOperationException("Invalid selection reached HTTP."));
        foreach(var request in new[] { Request() with { Date="20260229" },Request() with { Date="today+1day" },Request() with { DateAfter="20260131",DateBefore="20260130" },
            Request() with { MatchFilter="duration>cats" },Request() with { MatchFilter="duration~='number'" },Request() with { MatchFilter="title~='['" },Request() with { MatchFilter="duration>10 && title" },
            Request() with { MatchFilter="-" },Request() with { MinimumViews=-1 },Request() with { MinimumViews=100,MaximumViews=99 },Request() with { MaximumAge=100 } })
            await Expect<NativeSelectionException>(()=>engine.DownloadAsync(request,null,null,default));
        await Expect<NativeSelectionException>(()=>Task.FromResult(NativeDateRange.Parse("today-999999999999999999years",new(2026,1,1))));
    }
    private static async Task RegexTimeout()
    {
        var request=Request() with { MatchFilter="description~='^(a+)+$'" }; var facts=Facts() with { Description=new string('a',200000)+"b" };
        await Expect<NativeSelectionException>(()=>Task.FromResult(Accept(request,facts)));
    }
    private static async Task Publication()
    {
        using var files=new Files(); var request=Request(files.Path) with { OutputTemplate="%(upload_date)s-%(view_count)04d.%(ext)s",WriteInfoJson=true };
        var engine=Engine(Facts()); var inspected=await engine.InspectAsync(request,default);
        Assert(inspected.UploadDate==new DateOnly(2026,1,31)&&inspected.ViewCount==250&&inspected.AgeLimit==12,"Native inspection discarded filtering facts.");
        var result=await engine.DownloadAsync(request,null,null,default);
        using var info=JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single()));
        Assert(Path.GetFileName(result.OutputPath)=="20260131-0250.mp4"&&info.RootElement.GetProperty("upload_date").GetString()=="20260131"&&info.RootElement.GetProperty("view_count").GetInt64()==250,
            "Selection facts changed in output templates or info JSON.");
    }
    private static async Task YouTubeEarly()
    {
        var player=JsonSerializer.Serialize(new { playabilityStatus=new { status="OK" },videoDetails=new { videoId="jNQXAC9IVRw",title="Fixture",lengthSeconds="18",viewCount="12",author="Uploader" },
            microformat=new { playerMicroformatRenderer=new { uploadDate="2005-04-23",isFamilySafe=true } },streamingData=new { serverAbrStreamingUrl="https://googlevideo.com/sabr" } });
        var parsed=YouTubePlayerParser.ParseResponse(player,"jNQXAC9IVRw");
        Assert(parsed.UploadDate==new DateOnly(2005,4,23)&&parsed.ViewCount==12&&parsed.AgeLimit==0,"Public player facts were not retained.");
        var request=Request() with { Url=new("https://www.youtube.com/watch?v=jNQXAC9IVRw"),MinimumViews=100 };
        var engine=new NativeEngine([new YouTubeProvider(playbackSource:new DeniedPlayback())],_=>new MediaHttp(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("<script>var ytInitialPlayerResponse="+player+";</script>")})));
        var result=await engine.DownloadAsync(request,null,null,default);
        Assert(result.Succeeded&&result.Filtered,"A metadata-rejected YouTube video attempted playback.");
    }
    private static Task PublisherFacts()
    {
        var feed="<rss version='2.0'><channel><title>Feed</title><link>https://feed.example/</link><item><guid>one</guid><title>One</title><pubDate>Wed, 01 Jan 2020 12:00:00 GMT</pubDate><enclosure url='https://feed.example/one.mp3' type='audio/mpeg'/></item></channel></rss>";
        var entry=YoutubeDl.Engine.Providers.Feeds.MediaFeedParser.Parse(feed,new("https://feed.example/feed"),10).Metadata.Entries.Single();
        Assert(entry.ResolvedMedia!.UploadDate==new DateOnly(2020,1,1),"Public feed publication facts were discarded.");
        var html="<script type='application/ld+json'>{\"@context\":\"https://schema.org\",\"@type\":\"VideoObject\",\"name\":\"Movie\",\"contentUrl\":\"https://metadata.example/movie.mp4\",\"uploadDate\":\"2026-01-31\",\"interactionStatistic\":{\"@type\":\"InteractionCounter\",\"interactionType\":{\"@type\":\"WatchAction\"},\"userInteractionCount\":250}}</script>";
        var schema=StructuredMediaReader.Read(html,Url,10,default).Media!;
        Assert(schema.UploadDate==new DateOnly(2026,1,31)&&schema.ViewCount==250,"Structured publisher facts were not typed correctly.");
        return Task.CompletedTask;
    }
    private static NativeEngine Engine(PageMetadata page,Action<HttpRequestMessage>? observe=null)=>new([new Provider(page)],_=>new MediaHttp(new Handler(request=>{observe?.Invoke(request);return new(HttpStatusCode.OK){Content=new ByteArrayContent("fixture media"u8.ToArray())};})));
    private sealed class Provider(PageMetadata page):IMediaProvider
    {
        public MediaProviderInfo Info{get;}=new("metadata","Metadata fixture",1000); public bool CanHandle(Uri url)=>true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)=>Task.FromResult(page);
    }
    private sealed class DeniedPlayback:IYouTubePlaybackSource
    {
        public Task<PageMetadata> CaptureAsync(string videoId,NativeDownloadRequest request,CancellationToken ct)=>throw new InvalidOperationException("Rejected metadata started browser playback.");
        public Task<PageMetadata> CaptureAsync(string videoId,NativeDownloadRequest request,YouTubePlaybackCookies cookies,CancellationToken ct)=>throw new InvalidOperationException("Rejected metadata started browser playback.");
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(response(request));} }
    private sealed class Files:IDisposable
    {
        internal string Path{get;}=System.IO.Path.Combine(AppContext.BaseDirectory,"test-data","metadata-"+Guid.NewGuid().ToString("N"));
        internal Files()=>Directory.CreateDirectory(Path); public void Dispose()=>Directory.Delete(Path,true);
    }
    private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Expect<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
