using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class ProcessingOptionsChecks
{
    internal static NativeDownloadRequest Request => new() { Url = new("https://processing.example/watch"), OutputDirectory = Path.GetTempPath() };
    internal static readonly string[] Valid = ["ffmpeg:-metadata title='a:b c'", "-metadata title='a:b c'", "ExtractAudio:-b:a 192k", "VideoConvertor+ffmpeg_o1:-crf 18 -preset fast", "Metadata+ffmpeg_i1:-probesize 1048576", "ffmpeg_i:-threads 1", "ffmpeg_o:-metadata:s:a:0 language=eng", "Merger+ffmpeg:-movflags +faststart", "Fixup:-metadata comment=repair"];
    internal static readonly string[] Invalid = ["", "secret-value", "ffprobe:-threads 1", "Fake+ffmpeg:-metadata title=x", "ffmpeg_o2:-metadata title=x", "ffmpeg_i:-metadata title=x", "ffmpeg:-i https://foreign.example/media", "ffmpeg:-f tee", "ffmpeg:-map 0", "ffmpeg:-attach file", "ffmpeg:-metadata", "ffmpeg:output.mp4 x", "ffmpeg:-metadata title='open", "ffmpeg:-metadata x\ny", "ffmpeg:-/metadata file", "ffmpeg:-vf scale=1:1", "ffmpeg:-ss 2", "ffmpeg:-t 1"];
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        Valid.Select((value, index) => ("Processing options valid " + index, (Func<Task>)(() => { NativeProcessingOptions.Validate(null, [value]); return Task.CompletedTask; })))
        .Concat(Invalid.Select((value, index) => ("Processing options refusal " + index, (Func<Task>)(() => Reject(value)))))
        .Concat(new[] { ("Processing options stage precedence and input placement", (Func<Task>)Placement), ("Processing options concurrent scopes remain isolated", (Func<Task>)Concurrent), ("Processing options analysis discards all processing values", (Func<Task>)Analysis), ("Processing options defaults retain serialized recipe", (Func<Task>)Defaults), ("Processing options invalid policy precedes HTTP", (Func<Task>)BeforeHttp) })
        .Concat(new[] { "never", "warn", "detect_or_warn", "detect_or_fix", "force" }.Select(mode => ("Processing options fixup policy " + mode, (Func<Task>)(() => { NativeProcessingOptions.Validate(mode, null); return Task.CompletedTask; }))));
    private static Task Reject(string value)
    {
        try { NativeProcessingOptions.Validate(null, [value]); throw new Exception("An unsupported processing override was accepted."); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { Assert(!ex.Message.Contains("foreign.example", StringComparison.Ordinal), "Override diagnostics echoed a configured value."); }
        return Task.CompletedTask;
    }
    private static Task Placement()
    {
        var request = NativeExtraArguments.Apply(Request, ["--ppa", "ffmpeg:-metadata title=first", "--postprocessor-args", "ExtractAudio+ffmpeg_o:-metadata title=last", "--ppa", "ffmpeg_i2:-threads 1", "--fixup", "never"]);
        using (NativeProcessingOptions.Enter(request))
        {
            var result = NativeProcessingOptions.Apply(["-y", "-i", "a", "-i", "b", "-c", "copy", "out"], "ExtractAudio");
            Assert(result.SequenceEqual(new[] { "-y", "-i", "a", "-threads", "1", "-i", "b", "-c", "copy", "-metadata", "title=first", "-metadata", "title=last", "out" }), "Stage/input placement or argument order changed.");
            try { NativeProcessingOptions.Apply(["-i", "a", "out"], "ExtractAudio"); throw new Exception("A missing input was silently ignored."); } catch (NotSupportedException) { }
        }
        Assert(NativeProcessingOptions.Apply(["in", "out"], "ExtractAudio").Count == 2, "A disposed processing scope leaked."); return Task.CompletedTask;
    }
    private static async Task Concurrent()
    {
        var barrier = new TaskCompletionSource(); var entered = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            using var scope = NativeProcessingOptions.Enter(Request with { PostprocessorArguments = ["ffmpeg:-metadata title=" + index] });
            if (Interlocked.Increment(ref entered) == 8) barrier.SetResult(); await barrier.Task;
            Assert(NativeProcessingOptions.Apply(["-i", "in", "out"], "Metadata")[3] == "title=" + index, "Concurrent jobs exchanged overrides.");
        }));
    }
    private static Task Analysis()
    {
        Assert(NativeAnalysisArguments.Select(["--ppa", "--proxy", "--fixup=force", "--user-agent", "agent"]).SequenceEqual(["--user-agent", "agent"]), "A processing value was reinterpreted as a connection flag."); return Task.CompletedTask;
    }
    private static Task Defaults()
    {
        var text = System.Text.Json.JsonSerializer.Serialize(Request); Assert(!text.Contains("Fixup", StringComparison.Ordinal) && !text.Contains("PostprocessorArguments", StringComparison.Ordinal), "Default processing options changed old request serialization.");
        Assert(YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(Request) != YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(Request with { Fixup = "force" }), "An explicit processing policy did not change the publication recipe."); return Task.CompletedTask;
    }
    private static async Task BeforeHttp()
    {
        var reads = 0; var engine = Engine([], "mp4", () => reads++);
        foreach (var request in new[] { Request with { Fixup = "secret-invalid" }, Request with { PostprocessorArguments = ["ffmpeg:-t 1"] } })
        {
            try { await engine.DownloadAsync(request, null, null, default); throw new Exception("Invalid processing options reached extraction."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        Assert(reads == 0, "Invalid processing options opened HTTP.");
    }
    internal static NativeEngine Engine(byte[] data, string extension, Action? read = null) => new([new Provider(extension)], _ => AdaptiveAccountChecks.Http((_, _) => { read?.Invoke(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }); }));
    private sealed class Provider(string extension) : IMediaProvider
    {
        public MediaProviderInfo Info => new("processing", "Owned processing fixture", 1000);
        public bool CanHandle(Uri url) => url.Host == "processing.example";
        public YoutubeDl.Engine.Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("processing", "fixture");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata { Id = "fixture", Extractor = "processing", Title = "Processing fixture", DurationSeconds = 3, Media = [new() { Url = new("https://processing.example/media." + extension), Extension = extension, Kind = MediaKind.Progressive, HasAudio = true, HasVideo = extension != "m4a", Source = "processing" }] });
    }
    internal static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
