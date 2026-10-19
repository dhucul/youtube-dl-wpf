using System.Diagnostics;
using System.Globalization;
using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class DownloadDelayChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Download delay fixed and randomized intervals preserve finite inclusive bounds", Bounds),
        ("Download delay invalid ranges fail before discovery or filesystem effects", Invalid),
        ("Download delay argument aliases and overrides preserve fractional seconds", Arguments),
        ("Download delay waits before the first progressive media byte", Fixed),
        ("Download delay random intervals stay within the configured range", Randomized),
        ("Download delay cancellation protects published files archive and callbacks", Cancel),
        ("Download delay archive skips avoid discovery and waiting", ArchiveSkip),
        ("Download delay title filters avoid media waiting and publication", Filtered),
        ("Download delay simulation and skip-media do not wait", MetadataOnly),
        ("Download delay existing no-overwrite media is skipped without waiting", Existing),
        ("Download delay playlists wait before each selected source in order", Playlist),
        ("Download delay cancelled later playlist items retain prior completions", PlaylistCancel)
    ];

    private static Task Bounds()
    {
        var request = Request(Path.GetTempPath());
        var fixedDelay = new NativeDownloadDelay(request with { SleepIntervalSeconds = 2.5 });
        var range = new NativeDownloadDelay(request with { SleepIntervalSeconds = 1, MaximumSleepIntervalSeconds = 3 });
        Assert(fixedDelay.Select(0) == 2.5 && fixedDelay.Select(1) == 2.5, "The fixed interval changed.");
        Assert(range.Select(0) == 1 && range.Select(1) == 3 && range.Select(0.5) == 2, "Random bounds changed.");
        var large = new NativeDownloadDelay(request with { SleepIntervalSeconds = double.MaxValue / 2, MaximumSleepIntervalSeconds = double.MaxValue });
        Assert(double.IsFinite(large.Select(0.99999)), "Finite large bounds overflowed.");
        return Task.CompletedTask;
    }

    private static async Task Invalid()
    {
        using var files = new Files();
        var output = Path.Combine(files.Path, "untouched");
        var engine = new NativeEngine([new Provider()], _ => throw new InvalidOperationException("Invalid delays contacted the network."));
        var request = Request(output);
        foreach (var invalid in new[] {
            request with { SleepIntervalSeconds = -1 }, request with { SleepIntervalSeconds = double.NaN },
            request with { SleepIntervalSeconds = double.PositiveInfinity }, request with { MaximumSleepIntervalSeconds = 1 },
            request with { SleepIntervalSeconds = 2, MaximumSleepIntervalSeconds = 1 },
            request with { SleepIntervalSeconds = 1, MaximumSleepIntervalSeconds = double.NegativeInfinity } })
            await Expect<ArgumentException>(() => engine.DownloadAsync(invalid, null, null, default));
        Assert(!Directory.Exists(output), "Invalid delays created output directories.");
    }

    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()) with { SleepIntervalSeconds = 3, MaximumSleepIntervalSeconds = 4 },
            ["--sleep-interval", "1.5", "--min-sleep-interval=0.125", "--max-sleep-interval", "2.5"]);
        Assert(request.SleepIntervalSeconds == 0.125 && request.MaximumSleepIntervalSeconds == 2.5, "Fractional delay overrides were lost.");
        foreach (var value in new[] { "NaN", "Infinity", "-1", "invalid" })
            ExpectSync<ArgumentException>(() => NativeExtraArguments.Apply(request, ["--sleep-interval", value]));
        return Task.CompletedTask;
    }

    private static async Task Fixed()
    {
        using var files = new Files();
        var stopwatch = Stopwatch.StartNew(); var mediaAt = TimeSpan.Zero; var waits = 0;
        var engine = Engine(_ => mediaAt = stopwatch.Elapsed);
        var result = await engine.DownloadAsync(Request(files.Path) with { SleepIntervalSeconds = 0.12 }, null,
            line => { if (Waiting(line)) waits++; }, default);
        Assert(result.Succeeded && waits == 1 && mediaAt >= TimeSpan.FromMilliseconds(100), "Media began before its configured delay.");
        Assert(await File.ReadAllTextAsync(result.OutputPath!) == "complete media", "Waiting changed the transferred media.");
    }

    private static async Task Randomized()
    {
        using var files = new Files(); var seconds = 0d;
        var stopwatch = Stopwatch.StartNew(); var mediaAt = TimeSpan.Zero;
        var result = await Engine(_ => mediaAt = stopwatch.Elapsed).DownloadAsync(
            Request(files.Path) with { SleepIntervalSeconds = 0.10, MaximumSleepIntervalSeconds = 0.15 }, null,
            line => { if (Waiting(line)) seconds = double.Parse(line.Split(' ')[2], CultureInfo.InvariantCulture); }, default);
        Assert(result.Succeeded && seconds is >= 0.10 and <= 0.15 && mediaAt >= TimeSpan.FromMilliseconds(90), "Random delay escaped its bounds or transferred early.");
    }

    private static async Task Cancel()
    {
        using var files = new Files(); var prior = Path.Combine(files.Path, "a.mp4"); var archive = Path.Combine(files.Path, "archive.txt");
        await File.WriteAllTextAsync(prior, "prior media"); await File.WriteAllTextAsync(archive, "other preserved\n");
        using var cancel = new CancellationTokenSource(); var reads = 0; var callbacks = 0;
        await Expect<OperationCanceledException>(() => Engine(_ => reads++).DownloadAsync(
            Request(files.Path) with { SleepIntervalSeconds = 86400, DownloadArchiveFile = archive }, null,
            line => { if (Waiting(line)) cancel.Cancel(); }, cancel.Token, _ => callbacks++));
        Assert(reads == 0 && callbacks == 0 && await File.ReadAllTextAsync(prior) == "prior media" &&
            await File.ReadAllTextAsync(archive) == "other preserved\n", "Cancellation during the wait changed user data.");
        Assert(Directory.GetDirectories(files.Path, ".native-download-*").Length == 0, "Cancelled waiting staged media.");
    }

    private static async Task ArchiveSkip()
    {
        using var files = new Files(); var archive = Path.Combine(files.Path, "archive.txt"); await File.WriteAllTextAsync(archive, "delay a\n");
        var engine = new NativeEngine([new Provider()], _ => throw new InvalidOperationException("Archived media created a session."));
        var waits = 0;
        var result = await engine.DownloadAsync(Request(files.Path) with { SleepIntervalSeconds = 86400, DownloadArchiveFile = archive },
            null, line => { if (Waiting(line)) waits++; }, default);
        Assert(result.Skipped && waits == 0, "Archive skips waited.");
    }

    private static async Task Filtered()
    {
        using var files = new Files(); var waits = 0; var reads = 0;
        var result = await Engine(_ => reads++).DownloadAsync(Request(files.Path) with { SleepIntervalSeconds = 86400, MatchTitle = "unmatched" },
            null, line => { if (Waiting(line)) waits++; }, default);
        Assert(result.Filtered && waits == 0 && reads == 0 && Directory.GetFiles(files.Path).Length == 0, "Filtered items waited or transferred media.");
    }

    private static async Task MetadataOnly()
    {
        using var files = new Files(); var waits = 0; var reads = 0; var engine = Engine(_ => reads++);
        var request = Request(files.Path) with { SleepIntervalSeconds = 86400, WriteDescription = true };
        var simulation = await engine.DownloadAsync(request with { Simulate = true }, null, Log, default);
        var skipped = await engine.DownloadAsync(request with { SkipDownload = true }, null, Log, default);
        Assert(simulation.Simulated && skipped.Succeeded && waits == 0 && reads == 0 && skipped.SidecarPaths.Count == 1, "Metadata-only jobs waited or lost sidecars.");
        void Log(string line) { if (Waiting(line)) waits++; }
    }

    private static async Task Existing()
    {
        using var files = new Files(); await File.WriteAllTextAsync(Path.Combine(files.Path, "a.mp4"), "saved media"); var waits = 0;
        var result = await Engine(_ => throw new InvalidOperationException("Existing media transferred again.")).DownloadAsync(
            Request(files.Path) with { NoOverwrites = true, SleepIntervalSeconds = 86400 }, null,
            line => { if (Waiting(line)) waits++; }, default);
        Assert(result.Skipped && waits == 0 && await File.ReadAllTextAsync(result.OutputPath!) == "saved media", "No-overwrite skips waited or changed output.");
    }

    private static async Task Playlist()
    {
        using var files = new Files(); var order = new List<string>(); var callbacks = 0;
        var result = await Engine(url => order.Add(url.AbsolutePath)).DownloadAsync(
            Request(files.Path) with { Url = new("https://delay.example/list"), SleepIntervalSeconds = 0.03 }, null,
            line => { if (Waiting(line)) order.Add("wait"); }, default, _ => callbacks++);
        Assert(result.Succeeded && callbacks == 2 && order.SequenceEqual(new[] { "wait", "/media/a.mp4", "wait", "/media/b.mp4" }), "Playlist items lost separate waits or source order.");
    }

    private static async Task PlaylistCancel()
    {
        using var files = new Files(); using var cancel = new CancellationTokenSource(); var waits = 0; var reads = 0; var completions = new List<string>();
        var archive = Path.Combine(files.Path, "archive.txt");
        await Expect<OperationCanceledException>(() => Engine(_ => reads++).DownloadAsync(
            Request(files.Path) with { Url = new("https://delay.example/list"), SleepIntervalSeconds = 0.03, DownloadArchiveFile = archive }, null,
            line => { if (Waiting(line) && ++waits == 2) cancel.Cancel(); }, cancel.Token, completions.Add));
        Assert(reads == 1 && completions.Count == 1 && File.Exists(completions[0]) && !File.Exists(Path.Combine(files.Path, "b.mp4")), "Later cancellation lost completed media or published the pending item.");
        Assert((await File.ReadAllTextAsync(archive)).Contains("delay a", StringComparison.Ordinal) &&
            !(await File.ReadAllTextAsync(archive)).Contains("delay b", StringComparison.Ordinal), "Cancelled waiting changed completed archive identities.");
    }

    private static NativeDownloadRequest Request(string directory) => new() { Url = new("https://delay.example/a"), OutputDirectory = directory, OutputTemplate = "%(id)s.%(ext)s" };
    private static NativeEngine Engine(Action<Uri> media) => new([new Provider()], _ => new MediaHttp(new Handler(media)));
    private static bool Waiting(string line) => line.StartsWith("[native] Waiting ", StringComparison.Ordinal);
    private sealed class Provider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("delay", "Delay fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => request.Url.AbsolutePath == "/list" ? null : new("delay", request.Url.AbsolutePath.Trim('/'));
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(request.Url.AbsolutePath == "/list" ? new PageMetadata { Id = "list", PlaylistId = "list", IsPlaylist = true, Title = "Ordered delay list",
                Entries = new[] { "a", "b" }.Select((id, index) => new NativePlaylistEntry(index + 1, id, id, new Uri("https://delay.example/" + id), null, null, true, Episode(id), new("delay", id))).ToArray() } : Episode(request.Url.AbsolutePath.Trim('/')));
        }
        private static PageMetadata Episode(string id) => new() { Id = id, Extractor = "delay", ArchiveIdentity = new("delay", id), Title = "Alpha " + id, Description = "Complete description",
            Media = [new() { Url = new("https://delay.example/media/" + id + ".mp4"), Extension = "mp4", HasAudio = true, HasVideo = true }] };
    }
    private sealed class Handler(Action<Uri> media) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); media(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("complete media") });
        }
    }
    private sealed class Files : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "delay-" + Guid.NewGuid().ToString("N"));
        internal Files() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void ExpectSync<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
