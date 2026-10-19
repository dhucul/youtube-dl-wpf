using System.Text.Json.Nodes;
using YoutubeDl.Engine;

internal static class BilibiliSequenceChecks
{
    internal static JsonNode Playback(string extension = "mp4", int count = 2)
    {
        var result = BilibiliChecks.Progressive(); var parts = new JsonArray();
        for (var index = 0; index < count; index++) parts.Add(new JsonObject { ["order"] = index + 1, ["length"] = 1000, ["size"] = 5,
            ["url"] = "https://upos-test.bilivideo.com/video/1445374/part-" + (index + 1) + "." + extension + "?token=a%2Bb&part=" + (index + 1) });
        result["data"]!["durl"] = parts; result["data"]!["timelength"] = count * 1000;
        return result;
    }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Bilibili ordered files expose one complete sequence instead of first-chunk alternatives", Inventory),
        ("Bilibili ordered files validate positions durations origins and duplicate resources", Invalid),
        ("Bilibili ordered-file tool preflight prevents media requests", Tools),
        ("Bilibili ordered-file no-overwrite skips preserve existing data without tools", Existing),
        ("Bilibili ordered-file simulation preserves complete format selection without tools", Simulation)
    ];
    private static async Task Inventory()
    {
        var calls = new List<Uri>();
        var info = await BilibiliChecks.Engine(calls, playback: _ => Playback()).InspectAsync(BilibiliChecks.Request(Path.GetTempPath()), default);
        var format = info.Formats.Single();
        Assert(!info.IsPlaylist && info.DurationSeconds == 2 && format.Id == "progressive" && format.Protocol == "http_sequence" && format.Url is null && format.FileSize is null && format.Extension == "mp4", "A sequence became a playlist, partial direct link or inaccurate merged size.");
        Assert(calls.Count == 2, "Inspection downloaded an ordered file.");
    }
    private static async Task Invalid()
    {
        foreach (var mutate in new Action<JsonNode>[] {
            data => data["data"]!["durl"]![1]!["order"] = 1,
            data => data["data"]!["durl"]![1]!["order"] = 3,
            data => data["data"]!["durl"]![1]!["length"] = 0,
            data => data["data"]!["durl"]![1]!["length"] = 1200,
            data => data["data"]!["durl"]![1]!["url"] = data["data"]!["durl"]![0]!["url"]!.GetValue<string>(),
            data => data["data"]!["durl"]![1]!["url"] = "https://upos-test.bilivideo.com/video/1445374/part-2.flv" })
        {
            var data = Playback(); mutate(data); var calls = new List<Uri>();
            await Expect<InvalidDataException>(() => BilibiliChecks.Engine(calls, playback: _ => data).InspectAsync(BilibiliChecks.Request(Path.GetTempPath()), default));
            Assert(calls.Count == 2, "Malformed inventory triggered a chunk request.");
        }
        var excessive = Playback(count: 4097); var metadata = BilibiliChecks.Metadata(); metadata["data"]!["pages"]![0]!["duration"] = 4097;
        await Expect<InvalidDataException>(() => BilibiliChecks.Engine([], metadata, _ => excessive).InspectAsync(BilibiliChecks.Request(Path.GetTempPath()), default));
    }
    private static async Task Tools()
    {
        var calls = new List<Uri>();
        await Expect<FileNotFoundException>(() => BilibiliChecks.Engine(calls, playback: _ => Playback()).DownloadAsync(BilibiliChecks.Request(Path.GetTempPath()), null, null, default));
        Assert(calls.Count == 2, "Missing concatenation tools allowed media transfers.");
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, "ffprobe.exe"); await File.WriteAllTextAsync(probe, "unused presence marker");
        var handler = new ForbiddenMediaHandler();
        try
        {
            var engine = new NativeEngine([new SequenceToolProvider()], _ => new YoutubeDl.Engine.Http.MediaHttp(handler));
            await Expect<InvalidOperationException>(() => engine.DownloadAsync(BilibiliChecks.Request(directory) with { FfmpegPath = Path.Combine(directory, "missing-ffmpeg.exe") }, null, null, default));
            Assert(handler.Calls == 0, "A generic sequence provider downloaded media when only ffprobe existed.");
        }
        finally { File.Delete(probe); Directory.Delete(directory); }
    }
    private static async Task Existing()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bilibili.mp4"); await File.WriteAllTextAsync(path, "existing complete media");
        try
        {
            var calls = new List<Uri>();
            var result = await BilibiliChecks.Engine(calls, playback: _ => Playback()).DownloadAsync(BilibiliChecks.Request(directory) with { NoOverwrites = true }, null, null, default);
            Assert(result.Succeeded && result.Skipped && calls.Count == 2 && await File.ReadAllTextAsync(path) == "existing complete media", "An existing complete sequence was replaced or required tools.");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
    private static async Task Simulation()
    {
        var calls = new List<Uri>();
        var request = BilibiliChecks.Request(Path.GetTempPath()) with { Simulate = true, FormatSelector = "best[protocol=http_sequence]" };
        var result = await BilibiliChecks.Engine(calls, playback: _ => Playback()).DownloadAsync(request, null, null, default);
        Assert(result.Succeeded && result.Simulated && result.OutputPaths.Count == 0 && calls.Count == 2, "Sequence simulation fetched media or lost its protocol identity.");
        await Expect<NotSupportedException>(() => BilibiliChecks.Engine([], playback: _ => Playback()).DownloadAsync(request with { MaximumHeight = 480 }, null, null, default));
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class ForbiddenMediaHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Unexpected media request."); }
    }
    private sealed class SequenceToolProvider : YoutubeDl.Engine.Providers.IMediaProvider
    {
        public YoutubeDl.Engine.Providers.MediaProviderInfo Info { get; } = new("sequence-fixture", "Sequence fixture", 0);
        public bool CanHandle(Uri url) => true;
        public Task<YoutubeDl.Engine.Discovery.PageMetadata> ExtractAsync(NativeDownloadRequest request, YoutubeDl.Engine.Http.MediaHttp http, CancellationToken ct) => Task.FromResult(new YoutubeDl.Engine.Discovery.PageMetadata
        {
            Id = "sequence", Title = "sequence", Media = [new() { Url = request.Url, Kind = YoutubeDl.Engine.Discovery.MediaKind.HttpSequence, Extension = "mp4", HasVideo = true, HasAudio = false, IsCompleteMedia = true,
                Parts = [new(new("https://media.example/part1.mp4"), "mp4", 1), new(new("https://media.example/part2.mp4"), "mp4", 1)] }]
        });
    }
}
