using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Bilibili;

internal static class BilibiliChecks
{
    internal const string Bvid = "BV1Zs411o7Ee";
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.bilibili.com/video/" + Bvid + "/?p=1"), OutputDirectory = directory, FileName = "bilibili", Retries = 0 };
    internal static JsonNode Metadata() => JsonNode.Parse("""
        {"code":0,"data":{"aid":899574,"bvid":"BV1Zs411o7Ee","cid":1445374,"state":0,"videos":2,"title":"Multipart film","desc":"A public film","owner":{"name":"Uploader"},"duration":4,
        "pic":"http://i0.hdslb.com/bfs/archive/cover.jpg","rights":{"pay":0,"ugc_pay":0,"ugc_pay_preview":0,"arc_pay":0,"is_stein_gate":0},
        "pages":[{"cid":1445374,"page":1,"from":"vupload","part":"First","duration":2},{"cid":1445375,"page":2,"from":"vupload","part":"Second","duration":2}]}}
        """)!;
    internal static JsonNode Playback(long cid = 1445374) => JsonNode.Parse("""
        {"code":0,"data":{"quality":80,"accept_quality":[120,80,64,32,16],"timelength":2000,"dash":{"video":[
        {"id":32,"codecid":7,"baseUrl":"https://upos-test.bilivideo.com/upgcxcode/74/53/CID/CID_test-30032.m4s?token=a%2Bb%2Fc&x=1&x=2","mimeType":"video/mp4","codecs":"avc1.64001F","width":128,"height":96,"frameRate":"24/1","bandwidth":500000},
        {"id":16,"codecid":7,"base_url":"https://upos-test.bilivideo.com/upgcxcode/74/53/CID/CID_test-30016.m4s","mime_type":"video/mp4","codecs":"avc1.64001E","width":64,"height":48,"frame_rate":"24.000","bandwidth":100000}],
        "audio":[{"id":30280,"codecid":0,"baseUrl":"https://upos-test.akamaized.net/upgcxcode/74/53/CID/CID_test-30280.m4s","mimeType":"audio/mp4","codecs":"mp4a.40.2","bandwidth":65000}]}}}
        """.Replace("CID", cid.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;
    internal static JsonNode Progressive(long cid = 1445374)
    {
        var data = Playback(cid); data["data"]!["dash"] = null;
        data["data"]!["durl"] = new JsonArray(new JsonObject { ["order"] = 1, ["length"] = 2000, ["size"] = 5,
            ["url"] = "https://upos-test.bilivideo.com/video/" + cid + ".mp4?token=a%2Bb" });
        return data;
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Bilibili multipart inventory keeps part identities without fetching playback", Inventory),
        ("Bilibili BV AV player and mobile aliases resolve the selected part", Aliases),
        ("Bilibili rejects invalid URL identity and part combinations", InvalidAddresses),
        ("Bilibili preserves offered format facts without fabricating accepted qualities", Facts),
        ("Bilibili playlist simulation resolves only the chosen part and preserves forced archive identity", SelectedPart),
        ("Bilibili explicit part and no-playlist select one complete video", Single),
        ("Bilibili generic player iframe delegates to the native provider", Embed),
        ("Bilibili complete single progressive descriptor preserves size and access query", ProgressiveDownload),
        ("Bilibili paid interactive exclusive and preview metadata fail before playback", RestrictedMetadata),
        ("Bilibili preview protected and shortened playback never requests media", RestrictedPlayback),
        ("Bilibili malformed identities inventories and ambiguous representations fail", InvalidData),
        ("Bilibili malformed chunk inventories cannot publish just the first chunk", Chunks),
        ("Bilibili isolated archives distinguish parts and skip previously saved media", Archive),
        ("Bilibili supplied session headers override provider defaults", Headers),
        ("Bilibili HTTP API failures and cancellation do not publish outputs", Failures),
        ("Bilibili missing supported video cannot silently become an audio-only download", UnsupportedVideo),
        ("Bilibili rejects unrelated rendition origins and incomplete progressive durations", ResourceBoundary),
        ("Bilibili verification cancellation drains its child before preserving prior outputs", VerificationCancellation)
    ];
    private static async Task Inventory()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://www.bilibili.com/video/" + Bvid) }, default);
        Assert(info.IsPlaylist && info.Id == Bvid && info.Entries.Count == 2 && info.DurationSeconds == 4 && calls.Count == 1, "Inventory requested playback or lost entries.");
        Assert(info.Entries[1].Id == Bvid + "_1445375" && info.Entries[1].Position == 2 && info.Entries[1].Url!.Query == "?p=2", "Part identity changed.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://www.bilibili.com/video/av899574/?p=2", "https://m.bilibili.com/video/" + Bvid + "?p=2", "https://www.bilibili.com/s/video/" + Bvid + "?p=2",
            "https://player.bilibili.com/player.html?bvid=" + Bvid + "&aid=899574&cid=1445375&page=2", "https://player.bilibili.com/player.html?aid=899574&cid=1445375" })
        {
            var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default);
            Assert(info.Id == Bvid + "_1445375" && !info.IsPlaylist && calls[1].Query.Contains("cid=1445375"), "An alias selected the wrong part.");
        }
    }
    private static Task InvalidAddresses()
    {
        var provider = new BilibiliProvider();
        foreach (var url in new[] { "https://bilibili.com.evil.example/video/" + Bvid, "https://user@www.bilibili.com/video/" + Bvid, "https://www.bilibili.com:444/video/" + Bvid,
            "https://www.bilibili.com/video/av0", "https://www.bilibili.com/video/BVbad", "https://www.bilibili.com/video/" + Bvid + "?p=0",
            "https://www.bilibili.com/video/" + Bvid + "?p=1&p=2", "https://player.bilibili.com/player.html?aid=0", "https://player.bilibili.com/player.html?bvid=" + Bvid + "&p=1&page=2",
            "https://www.bilibili.com/video/av899574?aid=12", "https://www.bilibili.com/video/" + Bvid + "?bvid=BV0000000000" })
            Assert(!provider.CanHandle(new(url)), "An invalid URL was claimed.");
        return Task.CompletedTask;
    }
    private static async Task Facts()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()), default);
        var hd = info.Formats.Single(format => format.Id == "video-32-7");
        Assert(info.Formats.Count == 3 && hd.Width == 128 && hd.Height == 96 && hd.FramesPerSecond == 24 && hd.VideoCodec == "avc1.64001F" && hd.HasAudio == false && info.Uploader == "Uploader", "Actual representation facts were changed.");
        Assert(info.Formats.Single(format => format.Id == "audio-30280").HasVideo == false && calls.Count == 2, "Separate audio lost its media role.");
        using var files = new Files(); calls.Clear();
        await Expect<InvalidOperationException>(() => Engine(calls).DownloadAsync(Request(files.Directory) with { VideoOnly = true, MaximumHeight = 48 }, null, null, default));
        Assert(calls.Count == 2 && !Directory.EnumerateFiles(files.Directory).Any(), "Missing verification tools allowed an unverified media transfer.");
        var prior = Path.Combine(files.Directory, "bilibili.mp4"); await File.WriteAllTextAsync(prior, "already complete"); calls.Clear();
        var skipped = await Engine(calls).DownloadAsync(Request(files.Directory) with { VideoOnly = true, NoOverwrites = true }, null, null, default);
        Assert(skipped.Succeeded && skipped.Skipped && calls.Count == 2 && await File.ReadAllTextAsync(prior) == "already complete", "No-overwrite tried to verify or replace an existing output.");
    }
    private static async Task SelectedPart()
    {
        using var files = new Files(); var calls = new List<Uri>(); var completed = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var result = await Engine(calls, progressive: true).DownloadAsync(Request(files.Directory) with { Url = new("https://www.bilibili.com/video/" + Bvid), PlaylistItems = "2", DownloadArchiveFile = archive, Simulate = true, ForceWriteArchive = true }, null, null, default, completed.Add);
        Assert(result.Succeeded && result.Simulated && result.OutputPaths.Count == 0 && completed.Count == 0 && calls.Count == 3 && calls[2].Query.Contains("cid=1445375") && !calls.Any(url => url.Query.Contains("cid=1445374")), "Unselected playback was requested or simulation transferred media.");
        Assert((await File.ReadAllTextAsync(archive)).Trim() == "bilibili " + Bvid + "_1445375", "Archive did not bind the selected part.");
    }
    private static async Task Single()
    {
        foreach (var request in new[] { Request(Path.GetTempPath()), Request(Path.GetTempPath()) with { Url = new("https://www.bilibili.com/video/" + Bvid), DownloadPlaylist = false } })
        {
            var info = await Engine([]).InspectAsync(request, default);
            Assert(!info.IsPlaylist && info.Id == Bvid + "_1445374" && info.DurationSeconds == 2, "Single-video selection expanded the multipart upload.");
        }
    }
    private static async Task Embed()
    {
        var calls = new List<Uri>(); var info = await Engine(calls).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default);
        Assert(info.Id == Bvid + "_1445375" && calls.Count == 3, "Generic iframe did not dispatch to native Bilibili.");
    }
    private static async Task ProgressiveDownload()
    {
        using var files = new Files(); var calls = new List<Uri>();
        var info = await Engine(calls, progressive: true).InspectAsync(Request(files.Directory), default);
        var format = info.Formats.Single();
        Assert(format.Id == "progressive" && format.Extension == "mp4" && format.FileSize == 5 && format.Url!.Query == "?token=a%2Bb", "Complete progressive facts or literal access query changed.");
    }
    private static async Task RestrictedMetadata()
    {
        foreach (var flag in new[] { "pay", "ugc_pay", "ugc_pay_preview", "arc_pay", "is_stein_gate", "is_upower_exclusive", "is_upower_play", "is_upower_preview", "is_chargeable_season" })
        {
            var metadata = Metadata(); var field = flag.StartsWith("is_upower") || flag == "is_chargeable_season" ? metadata["data"]! : metadata["data"]!["rights"]!;
            field[flag] = 1; var calls = new List<Uri>();
            await Expect<NotSupportedException>(() => Engine(calls, metadata).InspectAsync(Request(Path.GetTempPath()), default));
            Assert(calls.Count == 1, "Restricted metadata requested playback.");
        }
    }
    private static async Task RestrictedPlayback()
    {
        using var files = new Files();
        foreach (var change in new Action<JsonNode>[] { data => data["data"]!["is_preview"] = 1, data => data["data"]!["drm_tech_type"] = 2,
            data => data["data"]!["timelength"] = 1, data => data["data"]!["dash"]!["video"]![0]!["drm"] = true })
        {
            var playback = Playback(); change(playback); var metadata = Metadata();
            if (playback["data"]!["timelength"]!.GetValue<int>() == 1) metadata["data"]!["pages"]![0]!["duration"] = 100;
            var calls = new List<Uri>(); await Expect<NotSupportedException>(() => Engine(calls, metadata, _ => playback).DownloadAsync(Request(files.Directory), null, null, default));
            Assert(calls.Count == 2 && !Directory.EnumerateFiles(files.Directory).Any(), "Restricted playback transferred or published media.");
        }
    }
    private static async Task InvalidData()
    {
        foreach (var change in new Action<JsonNode>[] { data => data["data"]!["bvid"] = "BV0000000000", data => data["data"]!["videos"] = 3,
            data => data["data"]!["cid"] = 1445375, data => data["data"]!["pages"]![1]!["cid"] = 1445374, data => data["data"]!["pages"]![1]!["page"] = 5 })
        { var data = Metadata(); change(data); await Expect<InvalidDataException>(() => Engine([], data).InspectAsync(Request(Path.GetTempPath()), default)); }
        foreach (var change in new Action<JsonNode>[] { data => data["data"]!["cid"] = 2, data => data["data"]!["dash"]!["video"]![0]!["base_url"] = "https://upos-test.bilivideo.com/other.m4s",
            data => ((JsonArray)data["data"]!["dash"]!["video"]!).Add(data["data"]!["dash"]!["video"]![0]!.DeepClone()) })
        { var data = Playback(); change(data); await Expect<InvalidDataException>(() => Engine([], playback: _ => data).InspectAsync(Request(Path.GetTempPath()), default)); }
        foreach (var url in new[] { "https://www.bilibili.com/video/" + Bvid + "?p=3", "https://player.bilibili.com/player.html?bvid=" + Bvid + "&cid=1445374&page=2" })
            await Expect<InvalidDataException>(() => Engine([]).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default));
        var other = Playback(1445375); await Expect<NotSupportedException>(() => Engine([], playback: _ => other).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Chunks()
    {
        using var files = new Files(); var calls = new List<Uri>(); var data = Progressive();
        ((JsonArray)data["data"]!["durl"]!).Add(data["data"]!["durl"]![0]!.DeepClone());
        await Expect<InvalidDataException>(() => Engine(calls, playback: _ => data).DownloadAsync(Request(files.Directory), null, null, default));
        Assert(calls.Count == 2 && !Directory.EnumerateFiles(files.Directory).Any(), "A partial chunk was downloaded or published.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "bilibili " + Bvid + "_1445374\n");
        var calls = new List<Uri>(); var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        var result = await Engine(calls, progressive: true).DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.Skipped && calls.Count == 2, "Archived part fetched media.");
        calls.Clear(); result = await Engine(calls, progressive: true).DownloadAsync(request with { Url = new("https://www.bilibili.com/video/av899574?p=2"), Simulate = true, ForceWriteArchive = true }, null, null, default);
        Assert(result.Succeeded && result.Simulated && calls.Count == 2 && (await File.ReadAllLinesAsync(archive)).Length == 2, "An unrelated part was treated as archived.");
    }
    private static async Task Headers()
    {
        using var files = new Files(); var count = 0;
        var result = await Engine([], progressive: true, observe: request => { count++; Assert(request.Headers.Referrer?.AbsoluteUri == "https://embedding.example/page" && request.Headers.UserAgent.ToString() == "NativeTest/1.0", "Caller headers were overwritten."); })
            .InspectAsync(Request(files.Directory) with { UserAgent = "NativeTest/1.0", Referer = "https://embedding.example/page" }, default);
        Assert(result.Formats.Count == 1 && count == 2, "Not all discovery requests used the supplied session.");
    }
    private static async Task Failures()
    {
        using var files = new Files(); var calls = new List<Uri>();
        await Expect<HttpRequestException>(() => Engine(calls, status: HttpStatusCode.Forbidden).DownloadAsync(Request(files.Directory), null, null, default));
        Assert(calls.Count == 1, "Denial triggered an alternate endpoint.");
        var metadata = Metadata(); metadata["code"] = -403;
        await Expect<NotSupportedException>(() => Engine([], metadata).InspectAsync(Request(files.Directory), default));
        using var stop = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => Engine([], observe: _ => stop.Cancel()).DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Cancellation or denial published an output.");
    }
    private static async Task UnsupportedVideo()
    {
        using var files = new Files(); var calls = new List<Uri>(); var data = Playback();
        foreach (var video in (JsonArray)data["data"]!["dash"]!["video"]!) video!["codecs"] = "unknown";
        await Expect<NotSupportedException>(() => Engine(calls, playback: _ => data).DownloadAsync(Request(files.Directory), null, null, default));
        Assert(calls.Count == 2 && !Directory.EnumerateFiles(files.Directory).Any(), "Unsupported video became a successful audio-only output.");
    }
    private static async Task ResourceBoundary()
    {
        foreach (var url in new[] { "https://upos-test.bilivideo.com.evil.example/upgcxcode/74/53/1445374/1445374_test.m4s", "https://other.akamaized.net/upgcxcode/74/53/1445374/1445374_test.m4s" })
        {
            var data = Playback(); data["data"]!["dash"]!["video"]![0]!["baseUrl"] = url;
            await Expect<NotSupportedException>(() => Engine([], playback: _ => data).InspectAsync(Request(Path.GetTempPath()), default));
        }
        var progressive = Progressive(); progressive["data"]!["durl"]![0]!["length"] = 1500;
        await Expect<InvalidDataException>(() => Engine([], playback: _ => progressive).InspectAsync(Request(Path.GetTempPath()), default));
        var shortPreview = Playback(); shortPreview["data"]!["timelength"] = 1;
        await Expect<NotSupportedException>(() => Engine([], playback: _ => shortPreview).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task VerificationCancellation()
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        const string variable = "YOUTUBEDL_NATIVE_TEST_WORKER_FILE";
        var previous = Environment.GetEnvironmentVariable(variable); var worker = Path.Combine(files.Directory, "verification.pid");
        var prior = Path.Combine(files.Directory, "bilibili.mp4"); await File.WriteAllTextAsync(prior, "previous complete video");
        var archive = Path.Combine(files.Directory, "archive.txt"); var calls = new List<Uri>(); var completed = 0;
        Task<NativeDownloadResult>? run = null; int? processId = null;
        try
        {
            Environment.SetEnvironmentVariable(variable, worker);
            run = Engine(calls).DownloadAsync(Request(files.Directory) with { VideoOnly = true, DownloadArchiveFile = archive,
                FfmpegPath = Path.Combine(AppContext.BaseDirectory, "YoutubeDl.Engine.Tests.exe") }, null, null, stop.Token, _ => completed++);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (processId is null)
            {
                // The atomic record can briefly remain locked by Windows file observers.
                try
                {
                    if (File.Exists(worker))
                    {
                        var record = await File.ReadAllTextAsync(worker);
                        if (!int.TryParse(record, out var readyId) || readyId <= 0) throw new InvalidOperationException("The verifier child record is invalid.");
                        processId = readyId; break;
                    }
                }
                catch (IOException) { }
                if (run.IsCompleted || DateTime.UtcNow >= deadline) throw new InvalidOperationException("The verifier child did not start.");
                await Task.Delay(10);
            }
            stop.Cancel();
            await Expect<OperationCanceledException>(() => run);
            try { using var child = System.Diagnostics.Process.GetProcessById(processId.Value); Assert(child.HasExited, "The verifier outlived cancellation."); }
            catch (ArgumentException) { }
            Assert(completed == 0 && !File.Exists(archive) && await File.ReadAllTextAsync(prior) == "previous complete video" && calls.Count == 3, "Canceled verification changed published data or continued fetching media.");
        }
        finally
        {
            stop.Cancel();
            if (run is not null) { try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { } }
            Environment.SetEnvironmentVariable(variable, previous);
            if (processId is { } id)
            {
                try { using var child = System.Diagnostics.Process.GetProcessById(id); if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
        }
    }
    internal static NativeEngine Engine(List<Uri> calls, JsonNode? metadata = null, Func<long, JsonNode>? playback = null, bool progressive = false,
        Func<Uri, byte[]>? media = null, Action<HttpRequestMessage>? observe = null, HttpStatusCode status = HttpStatusCode.OK,
        Func<HttpRequestMessage, HttpResponseMessage>? mediaResponse = null) => new(httpFactory: configuration => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!; calls.Add(url); observe?.Invoke(request);
        if (url.Host == "article.example") return new(HttpStatusCode.OK) { Content = new StringContent("<iframe src=\"https://player.bilibili.com/player.html?bvid=" + Bvid + "&amp;page=2\"></iframe>") };
        if (url.Host == "api.bilibili.com")
        {
            var cid = url.Query.Contains("cid=1445375") ? 1445375L : 1445374L;
            var data = url.AbsolutePath == "/x/web-interface/view" ? metadata ?? Metadata() : playback?.Invoke(cid) ?? (progressive ? Progressive(cid) : Playback(cid));
            return new(status) { Content = new StringContent(data.ToJsonString()) };
        }
        return mediaResponse?.Invoke(request) ?? new(HttpStatusCode.OK) { Content = new ByteArrayContent(media?.Invoke(url) ?? [1, 2, 3, 4, 5]) };
    }), new MediaHttpOptions { UserAgent = configuration.UserAgent ?? "", Referer = configuration.Referer ?? "" }));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
