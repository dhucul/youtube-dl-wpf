using System.Diagnostics;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class ExecSponsorChecks
{
    internal const string Video = "abcDEF123_-";
    internal sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", "exec-sponsor-" + Guid.NewGuid().ToString("N"));
        internal Files() { System.IO.Directory.CreateDirectory(Directory); }
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
    internal sealed class Fixture
    {
        internal byte[] Data = "complete native fixture"u8.ToArray(); internal int Transfers, Api; internal bool Youtube, Denied; internal double Duration = 3;
        internal object[] Segments = [new { segment = new[] { 1m, 2m }, category = "sponsor", actionType = "skip", UUID = "segment-one", videoDuration = 3 }];
        internal string CaptionFormat = "vtt";
        internal string Title = "literal & | \"quoted\" 日本語 %PATH% $(never-run)"; internal string Extension = "mp4"; internal Action<HttpRequestMessage>? ObserveApi;
        internal NativeEngine Engine => new([new Provider(this)], _ => AdaptiveAccountChecks.Http((request, _) =>
        {
            if (request.RequestUri!.Host == "sponsor.ajay.app")
            {
                Api++; ObserveApi?.Invoke(request);
                return Task.FromResult(new HttpResponseMessage(Denied ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new { videoID = Video, segments = Segments } })) });
            }
            Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Data) });
        }));
        private sealed class Provider(Fixture owner) : IMediaProvider
        {
            public MediaProviderInfo Info => new("exec-sponsor-fixture", "Owned exec/SponsorBlock fixture", 999);
            public bool CanHandle(Uri url) => true;
            public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("exec-sponsor-fixture", Video);
            public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(owner.Metadata);
        }
        internal PageMetadata Metadata => new() { Id = Video, Extractor = Youtube ? "youtube" : "exec-sponsor-fixture", Title = Title, DurationSeconds = Duration,
            Media = [new() { Url = new("https://fixture.example/source." + Extension), Extension = Extension, Kind = MediaKind.Progressive, HasVideo = Extension == "mp4", HasAudio = true }],
            Chapters = [new() { StartSeconds = 0, EndSeconds = Duration, Title = "Source chapter" }],
            Subtitles = [new() { Url = new("https://fixture.example/caption.vtt"), Language = "en", Extension = CaptionFormat, Content = CaptionFormat == "ttml" ? "<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"0.5s\" end=\"2.5s\">Across the removed interval.</p><p begin=\"1.2s\" end=\"1.8s\">Removed payload.</p></div></body></tt>" : CaptionFormat == "srt" ? "1\n00:00:00,500 --> 00:00:02,500\nAcross the removed interval.\n\n2\n00:00:01,200 --> 00:00:01,800\nRemoved payload.\n" : "WEBVTT\n\n00:00:00.500 --> 00:00:02.500\nAcross the removed interval.\n\n00:00:01.200 --> 00:00:01.800\nRemoved payload.\n" }] };
    }
    internal static NativeDownloadRequest Request(string folder) => new() { Url = new("https://www.youtube.com/watch?v=" + Video), OutputDirectory = folder, FileName = "recording", DownloadArchiveFile = Path.Combine(folder, "archive.txt"), Retries = 0 };
    internal static string Command(string mode, string marker, bool fields = false) => "dotnet \"" + typeof(ExecSponsorChecks).Assembly.Location + "\" --exec-helper " + mode + " \"" + marker + "\"" + (fields ? " {} %(title)q %(id)q" : "");
    internal static Task<int> Helper(string mode, string marker, string[] fields)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(marker).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("The exec helper marker is outside its owned test root.");
        File.AppendAllText(marker, JsonSerializer.Serialize(new { fields, Exists = File.Exists(fields[0]), Pid = Environment.ProcessId }) + "\n");
        if (mode == "tamper") File.WriteAllText(fields[0], "changed by configured helper");
        if (mode == "noisy") { for (var i = 0; i < 256; i++) { Console.Out.Write(new string('a', 4096)); Console.Error.Write(new string('b', 4096)); } }
        return mode == "wait" ? Wait() : Task.FromResult(mode == "failure" ? 7 : 0);
        static async Task<int> Wait() { await Task.Delay(TimeSpan.FromMinutes(2)); return 0; }
    }
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        new[] { "success", "fields", "ordered", "failure", "cancel", "noisy", "tamper", "skip", "simulate", "no-exec" }.Select(mode => ("Exec Sponsor command " + mode, (Func<Task>)(() => CommandCase(mode))))
        .Concat(new[] { "exec-intent:0", "exec-started", "exec-completed:0", "committed" }.Select(phase => ("Exec Sponsor crash " + phase, (Func<Task>)(() => Crash(phase)))))
        .Concat(new[] { "before_dl:program", "cmd /c echo {}", "pwsh -Command {}", "program | other", "program %(title)s", "program prefix%(title)q", "program '{}suffix'", "program 'unterminated", "node -e %(title)q", "node -e {}" }.Select((value, index) => ("Exec Sponsor grammar refusal " + index, (Func<Task>)(() => { Reject(() => NativeExec.Validate([value])); return Task.CompletedTask; }))))
        .Concat(new[] { "sponsor", "all,-preview", "default", "intro,outro", "all,-all", "all,-filler,-chapter,-poi_highlight" }.Select((value, index) => ("Exec Sponsor categories " + index, (Func<Task>)(() => { NativeSponsorBlock.Validate(value, value.Contains("chapter", StringComparison.Ordinal) ? null : value); return Task.CompletedTask; }))))
        .Concat(new[] { "unknown", "all,", "poi_highlight", "chapter" }.Select((value, index) => ("Exec Sponsor category refusal " + index, (Func<Task>)(() => { Reject(() => NativeSponsorBlock.Validate(null, value)); return Task.CompletedTask; }))))
        .Concat(new[] { "valid", "overlap", "point", "foreign-duration", "negative", "reverse", "category", "denied", "saved", "corrupt", "all-removed", "headers" }.Select(mode => ("Exec Sponsor plan " + mode, (Func<Task>)(() => PlanCase(mode)))))
        .Concat(new[] { ("Exec Sponsor precedence and analysis", (Func<Task>)Precedence), ("Exec Sponsor defaults retain serialization", (Func<Task>)Defaults), ("Exec Sponsor fixup tools precede media transfer", (Func<Task>)MissingTools) });
    private static async Task CommandCase(string mode)
    {
        using var files = new Files(); var fixture = new Fixture(); var engine = fixture.Engine; var marker = Path.Combine(files.Directory, "executions.jsonl"); var output = Path.Combine(files.Directory, "recording.mp4");
        await File.WriteAllTextAsync(output, "previous media"); var callbacks = new List<string>();
        var commandMode = mode is "cancel" ? "wait" : mode is "failure" or "tamper" or "noisy" ? mode : "success";
        var request = Request(files.Directory) with { ExecCommands = [Command(commandMode, marker, mode == "fields")], Simulate = mode == "simulate", NoOverwrites = mode == "skip" };
        if (mode == "ordered") request = request with { ExecCommands = [Command("success", marker), Command("success", marker)] };
        if (mode == "no-exec") request = NativeExtraArguments.Apply(request, ["--no-exec"]);
        using var cancel = new CancellationTokenSource();
        if (mode == "cancel") PublicationFaults.Observer = phase => { if (phase == "exec-started") cancel.Cancel(); return Task.CompletedTask; };
        NativeDownloadResult? result = null;
        try { result = await engine.DownloadAsync(request, null, null, cancel.Token, callbacks.Add); }
        catch (Exception ex) when (mode is "failure" or "tamper" or "cancel" && ex is InvalidOperationException or IOException or OperationCanceledException or AggregateException) { }
        finally { PublicationFaults.Observer = null; }
        if (mode is "failure" or "cancel" or "tamper")
        {
            Assert(result?.Succeeded != true && callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Failed exec published archive/callback state.");
            if (mode != "tamper") Assert(await File.ReadAllTextAsync(output) == "previous media", "Failed or cancelled exec did not restore the prior output.");
            else Assert(System.IO.Directory.EnumerateFiles(files.Directory, ".native-backup-*", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == "previous media"), "A modifying exec lost the prior backup.");
            if (mode != "tamper") { var transfers = fixture.Transfers; try { await engine.DownloadAsync(request, null, null, default); throw new Exception("An uncertain exec was replayed."); } catch (IOException) { } Assert(fixture.Transfers == transfers, "Uncertain exec fetched media before refusal."); }
            return;
        }
        Assert(result!.Succeeded, "The native exec download failed.");
        if (mode is "skip" or "simulate" or "no-exec") { Assert(!File.Exists(marker), "A disabled/skipped/simulated exec ran."); return; }
        var lines = await File.ReadAllLinesAsync(marker); Assert(lines.Length == (mode == "ordered" ? 2 : 1), "Exec order/count changed.");
        using var json = JsonDocument.Parse(lines[0]); var fields = json.RootElement.GetProperty("fields"); Assert(json.RootElement.GetProperty("Exists").GetBoolean() && fields[0].GetString() == output, "Exec did not see the final installed path.");
        if (mode == "fields") Assert(fields[1].GetString() == fixture.Title && fields[2].GetString() == Video, "Exec quoting changed metadata arguments.");
        var before = fixture.Transfers; await engine.DownloadAsync(request, null, null, default, callbacks.Add); Assert(fixture.Transfers == before && (await File.ReadAllLinesAsync(marker)).Length == lines.Length && callbacks.Count == 1, "Archived exec repeated side effects/transfers/callbacks.");
    }
    internal static async Task<int> Worker(string folder, string phase)
    {
        PublicationFaults.Observer = async value => { if (value != phase) return; await File.WriteAllTextAsync(Path.Combine(folder, "worker-ready"), Environment.ProcessId.ToString()); await Task.Delay(Timeout.Infinite); };
        await new Fixture().Engine.DownloadAsync(Request(folder) with { ExecCommands = [Command("success", Path.Combine(folder, "executions.jsonl"))] }, null, null, default); return 1;
    }
    private static async Task Crash(string phase)
    {
        using var files = new Files(); var output = Path.Combine(files.Directory, "recording.mp4"); await File.WriteAllTextAsync(output, "previous media");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(ExecSponsorChecks).Assembly.Location, "--exec-crash-worker", files.Directory, phase }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { for (var i = 0; i < 200 && !File.Exists(Path.Combine(files.Directory, "worker-ready")) && !process.HasExited; i++) await Task.Delay(25); Assert(File.Exists(Path.Combine(files.Directory, "worker-ready")), "Exec interruption worker did not reach its phase."); }
            finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); }
        }
        var fixture = new Fixture(); var marker = Path.Combine(files.Directory, "executions.jsonl"); var request = Request(files.Directory) with { ExecCommands = [Command("success", marker)] };
        if (phase is "exec-intent:0" or "exec-started") { try { await fixture.Engine.DownloadAsync(request, null, null, default); throw new Exception("An interrupted command was replayed."); } catch (IOException) { } Assert(fixture.Transfers == 0 && !File.Exists(request.DownloadArchiveFile) && File.ReadAllText(output) == "previous media", "Interrupted exec failed to preserve prior state before retry."); }
        else { var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && File.ReadAllLines(marker).Length == 1, "Completed exec did not recover without replay."); }
    }
    private static async Task PlanCase(string mode)
    {
        using var files = new Files(); var fixture = new Fixture { Youtube = true }; var request = Request(files.Directory) with { SponsorBlockMark = "all", SponsorBlockRemove = mode is "overlap" or "all-removed" ? "sponsor" : null };
        fixture.ObserveApi = message => Assert(message.Headers.Authorization is null && message.Headers.Referrer is null && !message.Headers.Contains("Cookie") && !message.Headers.Contains("X-Private"), "SponsorBlock received source credentials or referrer.");
        if (mode != "valid" && mode is not ("saved" or "corrupt")) fixture.Segments = mode switch
        {
            "overlap" => [Segment(0.5m, 1.5m), Segment(1m, 2m)], "point" => [Segment(1m, 1m, "poi_highlight", "poi")], "foreign-duration" => [new { segment = new[] { 1, 2 }, category = "sponsor", actionType = "skip", UUID = "wrong-duration", videoDuration = 30 }], "negative" => [Segment(-1, 2)], "reverse" => [Segment(2, 1)], "category" => [Segment(1, 2, "unknown")], "all-removed" => [Segment(0, 3)], _ => fixture.Segments
        };
        fixture.Denied = mode == "denied";
        using var http = AdaptiveAccountChecks.Http((message, _) => { fixture.ObserveApi(message); fixture.Api++; return Task.FromResult(new HttpResponseMessage(fixture.Denied ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new { videoID = Video, segments = fixture.Segments } })) }); });
        using var scoped = http.WithHeaders(new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-private", ["X-Private"] = "fixture-value", ["Referer"] = "https://fixture.example/private" });
        SponsorBlockPlan? plan = null;
        try { plan = await NativeSponsorBlock.ReadAsync(request, fixture.Metadata, scoped, files.Directory, default); }
        catch (Exception ex) when (mode is "foreign-duration" or "negative" or "reverse" or "category" or "denied" or "all-removed" && ex is InvalidDataException or HttpRequestException or NotSupportedException) { return; }
        Assert(plan is not null && mode is not ("foreign-duration" or "negative" or "reverse" or "category" or "denied" or "all-removed"), "Invalid SponsorBlock data was accepted.");
        if (mode == "overlap") { Assert(plan!.Kept(request).Sum(span => span.Duration) == 1.5m, "Overlapping sponsor intervals were removed twice."); var caption = plan.Captions(fixture.Metadata.Subtitles[0].Content!, "vtt", request, default); Assert(!caption.Contains("Removed payload", StringComparison.Ordinal), "Removed caption text remained."); }
        if (mode == "saved") { fixture.Denied = true; await NativeSponsorBlock.ReadAsync(request, fixture.Metadata, scoped, files.Directory, default); Assert(fixture.Api == 1, "Retry replaced its pinned SponsorBlock plan."); }
        if (mode == "corrupt") { await File.WriteAllTextAsync(Path.Combine(files.Directory, ".sponsorblock-plan.json"), "{}"); try { await NativeSponsorBlock.ReadAsync(request, fixture.Metadata, scoped, files.Directory, default); throw new Exception("A corrupt sponsor plan was accepted."); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { } }
    }
    internal static object Segment(decimal start, decimal end, string category = "sponsor", string action = "skip") => new { segment = new[] { start, end }, category, actionType = action, UUID = Guid.NewGuid().ToString("N"), videoDuration = 3 };
    private static Task Precedence() { var request = NativeExtraArguments.Apply(Request("unused") with { ExecCommands = ["program"] }, ["--no-exec", "--exec", "after_move:other {}", "--sponsorblock-mark", "all", "--no-sponsorblock", "--sponsorblock-remove", "sponsor"]); Assert(request.ExecCommands!.Single() == "after_move:other {}" && request.SponsorBlockMark is null && request.SponsorBlockRemove == "sponsor" && NativeAnalysisArguments.Select(["--exec", "--proxy", "--sponsorblock-remove", "all"]).Count == 0, "Exec/SponsorBlock precedence or analysis arity changed."); return Task.CompletedTask; }
    private static async Task MissingTools() { using var files = new Files(); var fixture = new Fixture(); try { await fixture.Engine.DownloadAsync(Request(files.Directory) with { Fixup = "force" }, null, null, default); throw new Exception("Fixup with missing tools reached media transfer."); } catch (FileNotFoundException) { Assert(fixture.Transfers == 0, "Missing fixup tools fetched media."); } }
    private static Task Defaults() { var json = JsonSerializer.Serialize(Request("unused")); Assert(!json.Contains("ExecCommands", StringComparison.Ordinal) && !json.Contains("SponsorBlock", StringComparison.Ordinal), "Default processing serialization changed."); return Task.CompletedTask; }
    private static void Reject(Action action) { try { action(); throw new Exception("Unsupported exec/SponsorBlock grammar was accepted."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } }
    internal static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
