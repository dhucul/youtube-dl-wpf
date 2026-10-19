using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static readonly (string Name, Func<Task> Test)[] RuntimeRegressionChecks =
    [
        ("Rate validation shares native units and byte boundaries", RateValidationAgreement),
        ("Cleared metadata requests reject late failures", () => StaleFetchFailure("clear")),
        ("Discarded metadata requests reject late failures", () => StaleFetchFailure("discard")),
        ("Canceled metadata requests reject late failures", () => StaleFetchFailure("cancel")),
        ("Current metadata requests still publish failures", CurrentFetchFailure),
        ("Canceled format inspection rejects late failures", StaleFormatFailure),
        ("Durations and ETAs preserve hours across day boundaries", DurationDayBoundaries),
        ("Process timeout drains descendants after their parent exits", () => InheritedPipeCancellation(timeout: true)),
        ("Process cancellation drains descendants after their parent exits", () => InheritedPipeCancellation(timeout: false)),
        ("Process pipe drainage preserves final UTF-8 lines and fast exits", ProcessOutputDrainage),
        ("Native progress preserves producer order and completes before publication", NativeProgressDelivery)
    ];

    private static Task RateValidationAgreement()
    {
        foreach (var (text, expected) in new (string?, long?)[]
        {
            (null, null), ("", null), (" ", null), ("1", 1), ("0.1K", 102),
            ("4.2M", (long)(4.2 * (1L << 20))), ("1g", 1L << 30),
            ("1T", 1L << 40), ("1p", 1L << 50), ("1e3", 1000)
        })
        {
            Assert(SettingsValidation.IsValidRateLimit(text), "UI rejected a native rate: " + text);
            Assert(ByteRateLimiter.Parse(text) == expected, "Wrong native byte rate: " + text);
        }
        foreach (var text in new[] { "0", "-1", "0.1", "NaN", "Infinity", "1e1000", "8192P", "9223372036854775808", "1Q" })
        {
            Assert(!SettingsValidation.IsValidRateLimit(text), "UI accepted an invalid native rate: " + text);
            try { _ = ByteRateLimiter.Parse(text); throw new InvalidOperationException("Engine accepted invalid rate: " + text); }
            catch (FormatException) { }
        }
        return Task.CompletedTask;
    }

    private static async Task StaleFetchFailure(string action)
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<VideoInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.Info = (_, _, _) => response.Task;
        using var viewModel = fixture.ViewModel();
        viewModel.UrlInput = "https://example.com/old";
        var fetch = viewModel.FetchCommand.ExecuteAsync(null);
        if (action == "clear") viewModel.ClearInputCommand.Execute(null);
        else if (action == "discard") viewModel.DiscardPreviewCommand.Execute(null);
        else viewModel.CancelFetchCommand.Execute(null);
        response.SetException(new IOException("old request failed"));
        await fetch;
        Assert(viewModel.FetchError == "" && !viewModel.HasPreview && !viewModel.IsFetching,
            "A canceled request restored an error or left its busy state attached.");
    }

    private static async Task CurrentFetchFailure()
    {
        await using var fixture = new Fixture();
        fixture.Engine.Info = (_, _, _) => Task.FromException<VideoInfo>(new IOException("current request failed"));
        using var viewModel = fixture.ViewModel();
        viewModel.UrlInput = "https://example.com/current";
        await viewModel.FetchCommand.ExecuteAsync(null);
        Assert(viewModel.FetchError == "current request failed" && !viewModel.IsFetching,
            "A current request lost its diagnostic.");
    }

    private static async Task StaleFormatFailure()
    {
        await using var fixture = new Fixture();
        var formats = new TaskCompletionSource<IReadOnlyList<FormatInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.Formats = (_, _, _, _) => formats.Task;
        using var viewModel = fixture.ViewModel();
        viewModel.UrlInput = "https://example.com/old";
        await viewModel.FetchCommand.ExecuteAsync(null);
        var inspect = viewModel.InspectFormatsCommand.ExecuteAsync(null);
        viewModel.ClearInputCommand.Execute(null);
        formats.SetException(new IOException("old formats failed"));
        await inspect;
        Assert(viewModel.FetchError == "" && !viewModel.IsInspectingFormats && !viewModel.HasPreview,
            "Canceled format inspection restored its error.");
    }

    private static Task DurationDayBoundaries()
    {
        foreach (var (seconds, expected) in new (double, string)[]
        {
            (59, "0:59"), (3599, "59:59"), (3600, "1:00:00"),
            (86399, "23:59:59"), (86400, "24:00:00"), (90000, "25:00:00"), (172801, "48:00:01")
        })
            Assert(Formatting.FormatDuration(seconds) == expected && Formatting.FormatEta(seconds) == expected,
                "Duration or ETA wrapped at " + seconds + " seconds.");
        Assert(Formatting.FormatDuration(null, "fallback") == "fallback" && Formatting.FormatDuration(double.NaN, "fallback") == "fallback" &&
            Formatting.FormatEta(double.PositiveInfinity) == "" && Formatting.FormatEta(-1) == "", "Invalid time handling changed.");
        _ = Formatting.FormatDuration(TimeSpan.MaxValue.TotalSeconds);
        _ = Formatting.FormatEta(TimeSpan.MaxValue.TotalSeconds);
        return Task.CompletedTask;
    }

    private static bool TryRunRuntimeRegressionHelper(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 1 && args[0] == "--runtime-output-helper")
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.Write("final café line");
            Console.Error.Write("final error line");
            return true;
        }
        if (args.Length != 2) return false;
        if (args[0] == "--runtime-pipe-child")
        {
            File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString());
            File.Move(args[1] + ".tmp", args[1]);
            Thread.Sleep(Timeout.Infinite);
            return true;
        }
        if (args[0] != "--runtime-pipe-parent") return false;
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "--runtime-pipe-child", args[1] }
        }) ?? throw new InvalidOperationException("Could not start inherited-pipe helper.");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(args[1]) && deadline.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
        if (!File.Exists(args[1])) { child.Kill(entireProcessTree: true); exitCode = 1; return true; }
        Console.WriteLine("pipe-parent:" + Environment.ProcessId);
        return true;
    }

    private static async Task InheritedPipeCancellation(bool timeout)
    {
        var childFile = Path.Combine(TestRoot, Guid.NewGuid().ToString("N") + ".pid");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var parentStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = ProcessRunner.RunAsync(Environment.ProcessPath!, ["--runtime-pipe-parent", childFile],
            onStdoutLine: line => { if (line.StartsWith("pipe-parent:", StringComparison.Ordinal)) parentStarted.TrySetResult(int.Parse(line[12..])); },
            ct: cancellation.Token, timeout: timeout ? TimeSpan.FromSeconds(2) : null);
        try
        {
            var parentId = await parentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                using var parent = Process.GetProcessById(parentId);
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException) { /* already exited */ }
            Assert(!run.IsCompleted, "The child did not retain its inherited output pipes.");
            if (!timeout) cancellation.Cancel();
            Exception? failure = null;
            try { await run.WaitAsync(TimeSpan.FromSeconds(6)); }
            catch (Exception ex) { failure = ex; }
            Assert(run.IsCompleted, "The runner remained blocked on inherited pipes.");
            Assert(timeout ? failure is TimeoutException : failure is OperationCanceledException,
                "The runner did not preserve timeout versus caller cancellation.");
            var childId = int.Parse(await File.ReadAllTextAsync(childFile));
            await Until(() => !RuntimeHelperRunning(childId));
        }
        finally
        {
            cancellation.Cancel();
            if (File.Exists(childFile))
            {
                var childId = int.Parse(await File.ReadAllTextAsync(childFile));
                try { using var child = Process.GetProcessById(childId); if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (ArgumentException) { /* already exited */ }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* observe failure and clean up on a failing implementation */ }
        }
    }

    private static bool RuntimeHelperRunning(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task ProcessOutputDrainage()
    {
        var stdout = new List<string>(); var stderr = new List<string>();
        var result = await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--runtime-output-helper"], stdout.Add, stderr.Add,
            timeout: TimeSpan.FromSeconds(5));
        Assert(result.ExitCode == 0 && result.StdOut.TrimEnd() == "final café line" && result.StdErr.TrimEnd() == "final error line" &&
            stdout.SequenceEqual(["final café line"]) && stderr.SequenceEqual(["final error line"]), "Final unterminated pipe lines were lost.");
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var quickExit = await ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "exit", "0"],
                timeout: TimeSpan.FromSeconds(5));
            Assert(quickExit.ExitCode == 0 && quickExit.StdOut == "" && quickExit.StdErr == "", "A fast process exit lost its result.");
        }
    }

    private sealed class HeldProgressContext : SynchronizationContext
    {
        private int _posts;
        public int Posts => Volatile.Read(ref _posts);
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref _posts);
    }

    private sealed class OrderedProgress : IProgress<ProgressUpdate>
    {
        public List<long> Bytes { get; } = [];
        public void Report(ProgressUpdate update) => Bytes.Add(update.DownloadedBytes ?? -1);
    }

    private sealed class OrderedMediaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4096]) });
    }

    private static async Task NativeProgressDelivery()
    {
        var directory = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: _ => new MediaHttp(new OrderedMediaHandler())));
        var settings = new AppSettings(); settings.Network.BufferSize = "1K"; settings.Network.NoResizeBuffer = true;
        var options = new DownloadOptions { OutputDirectory = directory, AddMetadata = false, EmbedChapters = false };
        var progress = new OrderedProgress(); var heldContext = new HeldProgressContext();
        var previous = SynchronizationContext.Current; var reportsAtCompletion = -1;
        Task<DownloadRunResult> download;
        try
        {
            SynchronizationContext.SetSynchronizationContext(heldContext);
            download = adapter.DownloadAsync("https://media.example/progress.mp4", options, settings, null, progress, _ => { }, default,
                _ => reportsAtCompletion = progress.Bytes.Count);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var result = await download;
        Assert(result.ExitCode == 0 && progress.Bytes.SequenceEqual(new long[] { 1024, 2048, 3072, 4096 }) &&
            reportsAtCompletion == 4 && heldContext.Posts == 0,
            "Native progress was queued separately from its producer or delivered after publication.");
    }
}
