using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeChecks
{
    private const string VideoId = "abcdefghijk";
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("YouTube URL aliases resolve to a validated video identifier", () => Sync(Addresses)),
        ("Player JSON extraction respects quoted braces and window assignments", () => Sync(PageJson)),
        ("YouTube metadata, thumbnail selection, and captions are preserved", () => Sync(Metadata)),
        ("YouTube formats retain codecs, track types, and availability", () => Sync(Formats)),
        ("Unresolved signature and throttling challenges never expose ready URLs", () => Sync(Challenges)),
        ("SABR-only responses preserve formats without claiming download support", () => Sync(Sabr)),
        ("Unavailable and mismatched YouTube responses fail explicitly", () => Sync(Unavailable)),
        ("Adaptive video plans pair default audio and compatible containers", () => Sync(Planning)),
        ("Native video height and media-type constraints are enforced", () => Sync(Constraints)),
        ("The YouTube provider canonicalizes Shorts without executing player code", Provider),
        ("The C# YouTube path completes an exposed combined-stream download", Download),
        ("Unresolved YouTube formats fail before media transfer", BlockedDownload),
        ("An explicitly selected YouTube video ignores playlist context", PlaylistContext)
    ];

    internal static string FixtureJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "youtube-player.json"));
    internal static string FixtureHtml => "<script>window[\"ytInitialPlayerResponse\"] = " + FixtureJson + ";</script>" +
        "<script>{\"jsUrl\":\"/s/player/fixture/player_es6.vflset/en_US/base.js\"}</script>";
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private static void Addresses()
    {
        foreach (var address in new[]
        {
            "https://www.youtube.com/watch?v=" + VideoId, "https://youtu.be/" + VideoId + "?t=3",
            "https://m.youtube.com/watch?v=" + VideoId, "https://music.youtube.com/watch?v=" + VideoId,
            "https://www.youtube.com/shorts/" + VideoId, "https://www.youtube.com/live/" + VideoId,
            "https://www.youtube.com/embed/" + VideoId, "https://www.youtube-nocookie.com/embed/" + VideoId,
            "https://www.youtube.com/v/" + VideoId
        })
        {
            var parsed = YouTubeAddress.Parse(new Uri(address));
            Assert(parsed.VideoId == VideoId && parsed.WatchUrl!.AbsoluteUri == "https://www.youtube.com/watch?v=" + VideoId, address);
        }
        Assert(!YouTubeAddress.IsYouTubeHost(new Uri("https://youtube.com.evil.example/watch?v=" + VideoId)), "Spoofed host accepted.");
        Assert(!YouTubeAddress.IsYouTubeHost(new Uri("https://user@youtube.com/watch?v=" + VideoId)), "Credentials in a URL accepted.");
        Assert(!YouTubeAddress.IsYouTubeHost(new Uri("https://youtube.com:123/watch?v=" + VideoId)), "A nonstandard port accepted.");
        Throws<ArgumentException>(() => YouTubeAddress.Parse(new Uri("https://youtube.com/watch?v=short")));
        Throws<InvalidDataException>(() => YouTubeAddress.Parse(new Uri("https://youtube.com/watch?v=" + VideoId + "&v=" + VideoId)));
        Throws<NotSupportedException>(() => YouTubeAddress.Parse(new Uri("https://youtube.com/@channel")));
    }

    private static void PageJson()
    {
        var parsed = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        Assert(parsed.Title == "Fixture {title} with \"quotes\"" && parsed.Description!.Contains("};"), "String contents broke JSON boundaries.");
        var assignment = "<script>var ytInitialPlayerResponse = " + FixtureJson + "; window.sideEffect();</script>";
        Assert(YouTubePlayerParser.ParsePage(assignment, VideoId).Id == VideoId, "Ordinary assignment missing.");
        Throws<InvalidDataException>(() => YouTubePageJson.ReadPlayerResponse("ytInitialPlayerResponse={\"unfinished\":1"));
        Throws<InvalidDataException>(() => YouTubePageJson.ReadPlayerResponse("<html>Consent required</html>"));
        Assert(YouTubePageJson.ReadPlayerScriptUrl("{\"jsUrl\":\"https://evil.example/s/player/base.js\"}") is null, "External script address was accepted.");
    }

    private static void Metadata()
    {
        var page = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        Assert(page.Id == VideoId && page.Extractor == "youtube" && page.Uploader == "Test creator" && page.DurationSeconds == 19, "Metadata missing.");
        Assert(page.ThumbnailUrl!.EndsWith("large.jpg") && page.PlayerScriptUrl!.Host == "www.youtube.com", "Thumbnail/player selection wrong.");
        Assert(page.Subtitles.Count == 2 && !page.Subtitles[0].Automatic && page.Subtitles[1].Automatic, "Caption kinds lost.");
        Assert(page.Subtitles.All(track => track.Url.Query.Contains("fmt=vtt")), "Caption format not requested.");
        var live = JsonNode.Parse(FixtureJson)!;
        live["videoDetails"]!["isLiveContent"] = true;
        live["microformat"] = JsonNode.Parse("{\"playerMicroformatRenderer\":{\"liveBroadcastDetails\":{\"isLiveNow\":false}}}");
        Assert(!YouTubePlayerParser.ParseResponse(live.ToJsonString(), VideoId).IsLive, "A finished broadcast was marked live.");
        live["microformat"]!["playerMicroformatRenderer"]!["liveBroadcastDetails"]!["isLiveNow"] = true;
        Assert(YouTubePlayerParser.ParseResponse(live.ToJsonString(), VideoId).IsLive, "A live broadcast was missed.");
    }

    private static void Formats()
    {
        var page = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        Assert(page.ReportedFormats.Count == 7 && page.Media.Count == 4, "Formats were dropped or unresolved URLs exposed.");
        var combined = page.ReportedFormats.Single(format => format.Id == "18");
        Assert(combined.HasAudio == true && combined.HasVideo == true && combined.VideoCodec == "avc1.42001E" && combined.AudioCodec == "mp4a.40.2", "Combined tracks wrong.");
        var video = page.ReportedFormats.Single(format => format.Id == "137");
        Assert(video.HasAudio == false && video.Height == 1080 && video.FramesPerSecond == 30 && video.FileSize == 5, "Adaptive video fields missing.");
        var audio = page.ReportedFormats.Single(format => format.Id == "140:en.0");
        Assert(audio.AudioOnly && audio.HasVideo == false && audio.AudioLanguage == "en" && audio.IsDefaultAudio && audio.Extension == "m4a", "Adaptive audio fields missing.");
    }

    private static void Challenges()
    {
        var page = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        foreach (var id in new[] { "299", "248" })
        {
            var challenged = page.ReportedFormats.Single(format => format.Id == id);
            Assert(challenged.Availability == FormatAvailability.PlayerChallengeRequired && challenged.Url is null && challenged.UnavailableReason is not null, "Unsolved challenge advertised as ready.");
        }
        var clear = JsonNode.Parse(FixtureJson)!;
        var formatNode = clear["streamingData"]!["adaptiveFormats"]!.AsArray().First(item => item!["itag"]!.GetValue<int>() == 299)!;
        formatNode["signatureCipher"] = "url=https%3A%2F%2Fr1.googlevideo.com%2Fvideoplayback%3Ffixture%3Dclear&sp=sig&sig=abc%2Bdef";
        var format = YouTubePlayerParser.ParseResponse(clear.ToJsonString(), VideoId).ReportedFormats.Single(item => item.Id == "299");
        Assert(format.Availability == FormatAvailability.Ready && format.Url!.Query.Contains("sig=abc%2Bdef"), "Clear signatures were double encoded.");
        formatNode["signatureCipher"] = "url=https%3A%2F%2Fexample.com%2Fmedia&sig=plain";
        Throws<InvalidDataException>(() => YouTubePlayerParser.ParseResponse(clear.ToJsonString(), VideoId));
        var protectedResponse = JsonNode.Parse(FixtureJson)!;
        protectedResponse["streamingData"]!["formats"]![0]!["drmFamilies"] = new JsonArray("WIDEVINE");
        Assert(YouTubePlayerParser.ParseResponse(protectedResponse.ToJsonString(), VideoId).ReportedFormats.Single(item => item.Id == "18").Availability == FormatAvailability.DrmProtected,
            "Protected bytes were advertised as playable media.");
    }

    private static void Sabr()
    {
        var node = JsonNode.Parse(FixtureJson)!;
        foreach (var group in new[] { "formats", "adaptiveFormats" })
            foreach (var format in node["streamingData"]![group]!.AsArray())
            {
                format!.AsObject().Remove("url");
                format.AsObject().Remove("signatureCipher");
            }
        var page = YouTubePlayerParser.ParseResponse(node.ToJsonString(), VideoId);
        Assert(page.Media.Count == 0 && page.ReportedFormats.Count == 7 && page.ReportedFormats.All(format => format.Availability == FormatAvailability.AdaptiveTransportRequired),
            "SABR metadata was mistaken for downloadable URLs.");
        Assert(page.Warnings.Count == 1, "Repeated format limitations were not deduplicated.");
    }

    private static void Unavailable()
    {
        foreach (var status in new[] { "ERROR", "LOGIN_REQUIRED", "UNPLAYABLE" })
            Throws<InvalidOperationException>(() => YouTubePlayerParser.ParseResponse("{\"playabilityStatus\":{\"status\":\"" + status + "\",\"reason\":\"Unavailable fixture\"}}", VideoId));
        Throws<InvalidDataException>(() => YouTubePlayerParser.ParseResponse(FixtureJson, "other-video"));
    }

    private static void Planning()
    {
        var page = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        var request = new NativeDownloadRequest { Url = new Uri("https://youtube.com/watch?v=" + VideoId), OutputDirectory = Path.GetTempPath() };
        var plan = MediaDownloadPlan.Create(page, request);
        Assert(plan.Primary.Id == "137" && plan.Audio!.Id == "140:en.0" && plan.Extension == "mp4", "Default compatible audio was not paired.");
        var videoOnly = MediaDownloadPlan.Create(page, new NativeDownloadRequest { Url = request.Url, OutputDirectory = request.OutputDirectory, VideoOnly = true });
        Assert(videoOnly.Primary.Id == "137" && !videoOnly.NeedsMerge, "Video-only planned audio.");
        var audioOnly = MediaDownloadPlan.Create(page, new NativeDownloadRequest { Url = request.Url, OutputDirectory = request.OutputDirectory, AudioOnly = true });
        Assert(audioOnly.Primary.IsAudioOnly && !audioOnly.NeedsMerge, "Audio-only selected a silent video stream.");
        Throws<NotSupportedException>(() => MediaDownloadPlan.Create(page, new NativeDownloadRequest { Url = request.Url, OutputDirectory = request.OutputDirectory, FormatId = "299" }));
    }

    private static void Constraints()
    {
        var page = YouTubePlayerParser.ParsePage(FixtureHtml, VideoId);
        var limited = QualitySelector.Select(page.Media, QualityPreference.MaximumHeight, 720, false);
        Assert(limited.Id == "18", "Resolution cap was exceeded.");
        Throws<InvalidOperationException>(() => QualitySelector.Select(page.Media, QualityPreference.Best, 100, false));
        Throws<InvalidOperationException>(() => QualitySelector.Select(page.Media.Where(item => item.HasAudio == false).ToArray(), QualityPreference.Best, null, true));
        Throws<InvalidOperationException>(() => MediaDownloadPlan.Create(page, new NativeDownloadRequest
        {
            Url = new Uri("https://youtube.com/watch?v=" + VideoId), OutputDirectory = Path.GetTempPath(), FormatId = "137", MaximumHeight = 720
        }));
    }

    private static async Task Provider()
    {
        var requests = new List<string>();
        var engine = Engine(requests);
        var info = await engine.InspectAsync(new NativeDownloadRequest { Url = new Uri("https://youtube.com/shorts/" + VideoId), OutputDirectory = Path.GetTempPath() }, default);
        Assert(requests.Single() == "https://www.youtube.com/watch?v=" + VideoId, "Shorts was not canonicalized.");
        Assert(info.Id == VideoId && info.Formats.Count == 7 && info.Subtitles.Count == 2 && info.PlayerScriptUrl is not null, "The engine lost provider metadata.");
    }

    private static async Task Download()
    {
        using var files = new Files();
        var requests = new List<string>();
        var result = await Engine(requests).DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://youtu.be/" + VideoId), OutputDirectory = files.Directory, FormatId = "18", Retries = 0
        }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "combined", "The native YouTube flow did not complete.");
        Assert(requests.Count == 2, "Player scripts or an external fallback were requested.");
    }

    private static async Task BlockedDownload()
    {
        using var files = new Files();
        var requests = new List<string>();
        await Expect<NotSupportedException>(() => Engine(requests).DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://youtube.com/watch?v=" + VideoId), OutputDirectory = files.Directory, FormatId = "299"
        }, null, null, default));
        Assert(requests.Count == 1 && !Directory.EnumerateFiles(files.Directory).Any(), "A challenged format started media transfer.");
        await Expect<InvalidOperationException>(() => Engine(requests).DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://youtube.com/watch?v=" + VideoId), OutputDirectory = files.Directory
        }, null, null, default));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Missing ffmpeg was discovered after media transfer.");
    }

    private static async Task PlaylistContext()
    {
        using var files = new Files();
        var requests = new List<string>();
        var single = await Engine(requests).DownloadAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://youtube.com/watch?v=" + VideoId + "&list=PLfixture"), OutputDirectory = files.Directory, FormatId = "18", DownloadPlaylist = false
        }, null, null, default);
        Assert(single.Succeeded, "The explicitly selected single video was refused.");
        Assert(requests.Count == 2 && requests[0] == "https://www.youtube.com/watch?v=" + VideoId, "Playlist context was not excluded.");
    }

    private static NativeEngine Engine(List<string> requests) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        requests.Add(request.RequestUri!.AbsoluteUri);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri.Host == "www.youtube.com" ? new StringContent(FixtureHtml) : new ByteArrayContent(Encoding.UTF8.GetBytes("combined"))
        };
    })));

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory)) File.Delete(file);
            System.IO.Directory.Delete(Directory);
        }
    }
}
