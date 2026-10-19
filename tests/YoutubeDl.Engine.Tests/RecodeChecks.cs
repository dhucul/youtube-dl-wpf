using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers;

internal static class RecodeChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native recode options reject invalid and contradictory jobs before discovery", Options),
        ("Native recode existing processed output skips transfer without requiring tools", Existing),
        ("Native recode post-overwrite protection preserves processed files while retaining new originals", Originals),
        ("Native recode post-overwrite preference does not protect unprocessed downloads", Unprocessed),
        ("Native recode publication protects only flagged media groups and their sidecars", Publication),
        ("Native recode and remux arguments preserve ordered job overrides", Arguments)
    ];
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://recode.example/video.mp4"), OutputDirectory = directory, FileName = "converted", Retries = 0 };
    private static async Task Options()
    {
        var calls = 0; var engine = new NativeEngine(httpFactory: _ => { calls++; throw new InvalidOperationException("Invalid request reached discovery."); });
        foreach (var request in new[] { Request(Path.GetTempPath()) with { RecodeVideo = "../../exe" }, Request(Path.GetTempPath()) with { RecodeVideo = "webm", AudioOnly = true }, Request(Path.GetTempPath()) with { RecodeVideo = "mkv", Container = "mp4" } })
            await Expect<Exception>(() => engine.DownloadAsync(request, null, null, default), ex => ex is ArgumentException or NotSupportedException);
        Assert(calls == 0, "Invalid recode settings requested media.");
    }
    private static async Task Existing()
    {
        using var files = new Files(); var output = Path.Combine(files.Directory, "converted.webm"); await File.WriteAllTextAsync(output, "previous converted file"); var transfers = 0;
        var result = await Engine([1, 2, 3], _ => transfers++).DownloadAsync(Request(files.Directory) with { RecodeVideo = "webm", NoPostOverwrites = true }, null, null, default,
            _ => throw new InvalidOperationException("A preserved file emitted a new-file callback."));
        Assert(result.Succeeded && result.Skipped && transfers == 0 && await File.ReadAllTextAsync(output) == "previous converted file", "Post-overwrite protection did not skip the existing converted output.");
    }
    private static async Task Originals()
    {
        using var files = new Files(); var final = Path.Combine(files.Directory, "converted.webm"); var original = Path.Combine(files.Directory, "converted.mp4");
        await File.WriteAllTextAsync(final, "processed stays"); await File.WriteAllTextAsync(original, "old original"); var callbacks = new List<string>();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var result = await Engine(bytes).DownloadAsync(Request(files.Directory) with { RecodeVideo = "webm", NoPostOverwrites = true, KeepOriginal = true }, null, null, default, callbacks.Add);
        Assert(result.Succeeded && callbacks.SequenceEqual([original]) && await File.ReadAllTextAsync(final) == "processed stays" && (await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "Protecting the processed file lost a requested fresh original.");
    }
    private static async Task Unprocessed()
    {
        using var files = new Files(); var output = Path.Combine(files.Directory, "converted.mp4"); await File.WriteAllTextAsync(output, "old source"); var bytes = new byte[] { 1, 2, 3 };
        var result = await Engine(bytes).DownloadAsync(Request(files.Directory) with { NoPostOverwrites = true }, null, null, default);
        Assert(result.Succeeded && !result.Skipped && (await File.ReadAllBytesAsync(output)).SequenceEqual(bytes), "No-post-overwrites was treated as a global download overwrite ban.");
    }
    private static async Task Publication()
    {
        using var files = new Files(); var final = Path.Combine(files.Directory, "final.mp4"); var sidecar = Path.ChangeExtension(final, ".info.json"); var original = Path.Combine(files.Directory, "original.mkv");
        await File.WriteAllTextAsync(final, "old final"); await File.WriteAllTextAsync(sidecar, "old metadata"); await File.WriteAllTextAsync(original, "old raw");
        using var workspace = new DownloadWorkspace(final, new("https://recode.example/video"));
        var staged = workspace.RawPath("source.mp4"); var metadata = Path.ChangeExtension(staged, ".info.json"); var raw = workspace.RawPath("original.mkv");
        await File.WriteAllTextAsync(staged, "new final"); await File.WriteAllTextAsync(metadata, "new metadata"); await File.WriteAllTextAsync(raw, "new raw");
        var result = await workspace.PublishAsync([new(staged, final, [metadata], true), new(raw, original, [])], false, default);
        Assert(result.NewPaths.SequenceEqual([original]) && await File.ReadAllTextAsync(final) == "old final" && await File.ReadAllTextAsync(sidecar) == "old metadata" && await File.ReadAllTextAsync(original) == "new raw", "Publication did not enforce per-group post-overwrite policy.");
    }
    private static Task Arguments()
    {
        var request = NativeExtraArguments.Apply(Request(Path.GetTempPath()), ["--remux-video", "mp4", "--recode-video", "webm", "--no-post-overwrites"]);
        Assert(request.RecodeVideo == "webm" && request.Container == "" && request.NoPostOverwrites, "Recoding did not override prior remuxing.");
        request = NativeExtraArguments.Apply(request, ["--remux-video=mkv", "--post-overwrites"]);
        Assert(request.RecodeVideo == "" && request.Container == "mkv" && !request.NoPostOverwrites, "Remuxing or overwrite override was ignored.");
        return Task.CompletedTask;
    }
    internal static NativeEngine Engine(byte[] content, Action<HttpRequestMessage>? observe = null, string extension = "mp4", PageMetadata? page = null, bool hasAudio = true) => new([new Provider(page ?? new() { Id = "conversion", Title = "Conversion fixture", Media = [new() { Url = new("https://recode.example/video." + extension), Kind = MediaKind.Progressive, Extension = extension, HasVideo = true, HasAudio = hasAudio, IsCompleteMedia = true }] })],
        _ => new MediaHttp(new Handler(request => { observe?.Invoke(request); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }; })));
    private sealed class Provider(PageMetadata page) : IMediaProvider
    { public MediaProviderInfo Info { get; } = new("recode-fixture", "Recode fixture", 1); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    { internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "native-recode-" + Guid.NewGuid()); public Files() => System.IO.Directory.CreateDirectory(Directory); public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> run, Func<Exception, bool> condition) where T : Exception { try { await run(); } catch (T ex) when (condition(ex)) { return; } throw new InvalidOperationException("Expected validation failure."); }
}
