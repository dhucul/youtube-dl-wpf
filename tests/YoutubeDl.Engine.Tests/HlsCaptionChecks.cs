using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static class HlsCaptionChecks
{
    internal const string Master = """
        #EXTM3U
        #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="subs",NAME="English",LANGUAGE="en",DEFAULT=YES,URI="en.m3u8"
        #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="subs",NAME="French",LANGUAGE="fr",URI="fr.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=128x96,SUBTITLES="subs",CLOSED-CAPTIONS=NONE
        video.m3u8
        """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("HLS variants retain subtitle groups, language flags and segment durations", () => Sync(Manifest)),
        ("HLS WebVTT timestamp maps align to media and deduplicate spanning cues", () => Sync(Timing)),
        ("HLS WebVTT handles 33-bit clock wrap and negative probed origins", () => Sync(Wrap)),
        ("HLS WebVTT initialization headers and unchanged styles survive joining", () => Sync(Initialization)),
        ("Malformed caption maps and ambiguous timing fail before publication", () => Sync(Invalid)),
        ("Missing HLS timing tools fail before media segments are downloaded", MissingProbe),
        ("Unrequested HLS subtitles do not create a media-tool dependency", Unselected),
        ("In-band HLS caption requests remain explicit unsupported capabilities", InBand),
        ("Canceled HLS caption assembly emits no subtitle document", () => Sync(Cancellation))
    ];
    private static void Manifest()
    {
        var master = HlsPlaylistParser.Parse(Master, new Uri("https://media.example/master.m3u8"));
        Assert(master.Variants.Single().SubtitleGroup == "subs" && master.Variants.Single().ClosedCaptionGroup is null && master.SubtitleRenditions.Count == 2 &&
            master.SubtitleRenditions[0].Default && master.SubtitleRenditions[0].Language == "en", "Subtitle group facts were lost.");
        var media = HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:1.25,\na.vtt\n#EXTINF:2.75,\nb.vtt\n#EXT-X-ENDLIST\n", new Uri("https://media.example/en.m3u8"));
        Assert(media.MediaSegments.Select(segment => segment.DurationSeconds).SequenceEqual(new double?[] { 1.25, 2.75 }), "HLS caption durations were lost.");
        Throws<InvalidDataException>(() => HlsPlaylistParser.Parse(Master.Replace(",URI=\"en.m3u8\"", ""), new Uri("https://media.example/master.m3u8")));
    }
    private static void Timing()
    {
        var text = HlsWebVttReader.Join([
            new("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:900000\n\ncue\n00:00:00.500 --> 00:00:04.000 align:start\nspanning\n", 0, 2),
            new("WEBVTT\nX-TIMESTAMP-MAP=MPEGTS:1080000,LOCAL:00:00:02.000\n\ncue\n00:00:00.500 --> 00:00:04.000 align:start\nspanning\n\n00:00:04.500 --> 00:00:07.000\nfinal\n", 2, 4)
        ], 10, 6);
        Assert(text.Contains("cue\n00:00:00.500 --> 00:00:04.000 align:start\nspanning") && text.Contains("00:00:04.500 --> 00:00:06.000") && text.Split(" --> ").Length == 3 && !text.Contains("X-TIMESTAMP-MAP"), "Caption maps, deduplication or clipping changed.");
        var rounded = HlsWebVttReader.Join([new("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00.000,MPEGTS:128090\n\n00:00.000 --> 00:01.000\nprecise\n", 0, 2)], 1.423222m, 2);
        Assert(rounded.Contains("00:00:00.000 --> 00:00:01.000"), "Probe decimal rounding moved the cue end by a millisecond.");
        var clippedDuplicate = HlsWebVttReader.Join([new("WEBVTT\n\n00:03.837 --> 00:07.300\nshared text\n", 0, 4), new("WEBVTT\n\n00:04.000 --> 00:07.300\nshared text\n", 4, 4)], 0, 8);
        Assert(clippedDuplicate.Split(" --> ").Length == 2 && clippedDuplicate.Contains("00:00:03.837 --> 00:00:07.300"), "A boundary-clipped repeat duplicated a visible cue.");
    }
    private static void Wrap()
    {
        foreach (var origin in new[] { (8589934592m - 90000) / 90000, -1m })
        {
            var text = HlsWebVttReader.Join([
                new("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:8589844592\n\n00:00.000 --> 00:02.000\nbefore\n", 0, 2),
                new("WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:90000\n\n00:00.000 --> 00:02.000\nafter\n", 2, 2)
            ], origin, 4);
            Assert(text.Contains("00:00:00.000 --> 00:00:02.000") && text.Contains("00:00:02.000 --> 00:00:04.000"), "MPEG clock rollover shifted captions.");
        }
    }
    private static void Initialization()
    {
        const string header = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00.000,MPEGTS:0\n\nSTYLE\n::cue { color: lime; }\n\n";
        var text = HlsWebVttReader.Join([new("00:00.000 --> 00:01.000\none\n", 0, 1, header), new("00:01.000 --> 00:02.000\none\n", 1, 1, header)], 0, 2);
        Assert(text.Contains("STYLE\n::cue { color: lime; }") && text.Contains("00:00:00.000 --> 00:00:02.000"), "Initialization style or continuing cue was lost.");
    }
    private static void Invalid()
    {
        foreach (var document in new[] { "<html>error</html>", "WEBVTT\n00:00.000 --> 00:01.000\nmissing separator", "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00.000,MPEGTS:8589934592\n\n", "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:bad,MPEGTS:0\n\n", "WEBVTT\n\n00:02.000 --> 00:01.000\nreversed\n" })
            Throws<InvalidDataException>(() => HlsWebVttReader.Join([new(document, 0, 2)], 0, 2));
        Throws<NotSupportedException>(() => HlsWebVttReader.Join([new("WEBVTT\n\n00:00:00.000 --> 20:00:00.000\nambiguous\n", 0, 2)], 0, 2));
    }
    private static async Task MissingProbe()
    {
        using var files = new Files(); var calls = new List<string>();
        var engine = Engine(calls);
        await Expect<FileNotFoundException>(() => engine.DownloadAsync(Request(files) with { WriteSubs = true }, null, null, default));
        Assert(calls.SequenceEqual(new[] { "/master.m3u8", "/video.m3u8" }), "A missing timing tool was discovered after media transfer.");
    }
    private static async Task Unselected()
    {
        using var files = new Files(); var calls = new List<string>();
        foreach (var request in new[] { Request(files), Request(files) with { WriteAutoSubs = true }, Request(files) with { WriteSubs = true, SubLanguages = "de" } })
        {
            calls.Clear(); var result = await Engine(calls).DownloadAsync(request, null, null, default);
            Assert(result.Succeeded && !calls.Any(path => path is "/en.m3u8" or "/fr.m3u8"), "An unselected subtitle track affected downloading.");
        }
    }
    private static async Task InBand()
    {
        using var files = new Files(); var calls = new List<string>();
        var master = Master.Replace("TYPE=SUBTITLES", "TYPE=CLOSED-CAPTIONS,INSTREAM-ID=\"CC1\"").Replace("SUBTITLES=\"subs\",CLOSED-CAPTIONS=NONE", "CLOSED-CAPTIONS=\"subs\"");
        await Expect<NotSupportedException>(() => Engine(calls, master).DownloadAsync(Request(files) with { WriteSubs = true }, null, null, default));
        Assert(!calls.Contains("/segment.ts"), "Unsupported in-band captions reached media transfer.");
    }
    private static void Cancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Throws<OperationCanceledException>(() => HlsWebVttReader.Join([new("WEBVTT\n\n", 0, 1)], 0, 1, cancel.Token));
    }
    private static NativeEngine Engine(List<string> calls, string master = Master) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var path = request.RequestUri!.AbsolutePath; calls.Add(path);
        return new(HttpStatusCode.OK) { Content = new StringContent(path switch { "/master.m3u8" => master, "/video.m3u8" => "#EXTM3U\n#EXTINF:1,\nsegment.ts\n#EXT-X-ENDLIST\n", "/segment.ts" => "media", _ => throw new InvalidOperationException("Unexpected caption request.") }) };
    })));
    private static NativeDownloadRequest Request(Files files) => new() { Url = new Uri("https://media.example/master.m3u8"), OutputDirectory = files.Directory, FileName = "result", Retries = 0 };
    private sealed class Files : IDisposable { public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "hls-captions-" + Guid.NewGuid().ToString("N")); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
