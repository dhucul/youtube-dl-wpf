using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class StressRaceChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Stress race completion waits for an overlapping publisher", CompletionPublication),
        ("Stress race chunk receipts tolerate a temporary sharing lock", () => ReceiptSharing(false)),
        ("Stress race chunk receipt sharing waits honor cancellation", () => ReceiptSharing(true)),
        ("Stress race chunk receipts tolerate a delete-pending destination", () => ReceiptSharing(false, deletePending: true)),
        ("Stress race read-only receipts preserve prior state", ReadOnlyReceipt),
        ("Stress race exited recovery workers drain inherited pipes", WorkerDrainage),
        ("Stress race media tools decode UTF-8 independently of console encoding", ToolOutputEncoding)
    ];

    private static async Task CompletionPublication()
    {
        using var files = new Files();
        var target = Path.Combine(files.Directory, "shared.mp4");
        var backup = Path.Combine(files.Directory, "shared.backup");
        var request = new NativeDownloadRequest { Url = new("https://stress.example/shared"), OutputDirectory = files.Directory };
        DownloadWorkspace.Completion completion;
        using (var workspace = await DownloadWorkspace.OpenAsync(target, request.Url, [], DownloadWorkspace.Recipe(request), default))
        {
            var raw = workspace.RawPath(target); await File.WriteAllTextAsync(raw, "complete original");
            using var publication = await DestinationLock.AcquirePublicationAsync(files.Directory, default);
            await workspace.PublishAsync([new(raw, target, [])], false, default);
            completion = workspace.PendingCompletion!;
        }
        var otherPublisher = await DestinationLock.AcquirePublicationAsync(files.Directory, default);
        Task? finish = null;
        try
        {
            File.Move(target, backup);
            finish = DownloadWorkspace.FinishAsync(completion, null, archiveOnly: true);
            await Task.Delay(75);
            Check(!finish.IsCompleted, "Completion validated an output during another publisher's backup/install gap.");
        }
        finally
        {
            if (File.Exists(backup)) File.Move(backup, target);
            otherPublisher.Dispose();
            if (finish is not null) try { await finish; } catch { /* assert above reports the race */ }
        }
        await finish!;
        var notifications = 0;
        await DownloadWorkspace.FinishAsync(completion, path => { Check(File.ReadAllText(path) == "complete original", "Completion observed partial output."); notifications++; });
        Check(notifications == 1 && !Directory.EnumerateDirectories(files.Directory, ".native-download-*").Any(), "Completion did not drain once publication finished.");
    }

    private static async Task ReceiptSharing(bool cancel, bool deletePending = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Files();
        var target = Path.Combine(files.Directory, "chunks.bin");
        var receipt = target + ".part.json";
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FileStream? reader = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var data = Enumerable.Range(0, 24).Select(index => (byte)index).ToArray();
        using var http = new MediaHttp(new Handler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var from = range.From!.Value; var to = Math.Min(range.To!.Value, data.Length - 1);
            if (from == 8)
            {
                reader = new FileStream(receipt, FileMode.Open, FileAccess.Read,
                    deletePending ? FileShare.Read | FileShare.Delete : FileShare.Read, 4096,
                    deletePending ? FileOptions.DeleteOnClose : FileOptions.None);
                locked.TrySetResult();
            }
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[(int)from..((int)to + 1)]) };
            response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
            return response;
        }));
        var download = new FileDownloader(http) { HttpChunkSize = "8" }.DownloadAsync(new("https://stress.example/chunks"), target, null, null, cancellation.Token, retries: 0);
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(75);
            Check(!download.IsCompleted, "A temporary receipt reader aborted the transfer instead of allowing replacement to wait.");
            if (cancel)
            {
                cancellation.Cancel();
                try { await download.WaitAsync(TimeSpan.FromSeconds(2)); throw new InvalidOperationException("A canceled receipt replacement succeeded."); }
                catch (OperationCanceledException) { }
                Check(!File.Exists(target), "Cancellation published an incomplete transfer.");
            }
            else
            {
                reader!.Dispose(); reader = null;
                await download;
                Check((await File.ReadAllBytesAsync(target)).SequenceEqual(data) && !File.Exists(receipt), "Receipt contention changed the completed bytes or retained a completed receipt.");
            }
        }
        finally
        {
            reader?.Dispose(); cancellation.Cancel();
            try { await download; } catch { /* observe the failing implementation */ }
        }
    }

    internal static async Task<int> PipeParent(string directory)
    {
        using var child = Process.Start(new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { typeof(StressRaceChecks).Assembly.Location, "--stress-pipe-child", directory, "unused" }
        })!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(Path.Combine(directory, "child.pid"))) await Task.Delay(10, deadline.Token);
        Console.Error.WriteLine("Worker exited before the recovery marker.");
        return 1;
    }

    private static async Task ReadOnlyReceipt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Files();
        var target = Path.Combine(files.Directory, "readonly.bin"); var receipt = target + ".part.json";
        string? prior = null;
        using var http = new MediaHttp(new Handler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single(); var from = range.From!.Value;
            if (from == 8) { prior = File.ReadAllText(receipt); File.SetAttributes(receipt, FileAttributes.ReadOnly); }
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[8]) };
            response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, from + 7, 16);
            return response;
        }));
        try
        {
            try
            {
                await new FileDownloader(http) { HttpChunkSize = "8" }.DownloadAsync(new("https://stress.example/readonly"), target, null, null, default, retries: 0);
                throw new InvalidOperationException("A read-only resume receipt was replaced.");
            }
            catch (UnauthorizedAccessException) { }
            Check(prior is not null && File.ReadAllText(receipt) == prior && !File.Exists(target) && !Directory.EnumerateFiles(files.Directory, "*.tmp").Any(),
                "A permanent receipt failure changed prior state or leaked a temporary file.");
        }
        finally { if (File.Exists(receipt)) File.SetAttributes(receipt, FileAttributes.Normal); }
    }

    internal static async Task<int> PipeChild(string directory)
    {
        var marker = Path.Combine(directory, "child.pid");
        await File.WriteAllTextAsync(marker + ".tmp", Environment.ProcessId.ToString()); File.Move(marker + ".tmp", marker);
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private static async Task WorkerDrainage()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var files = new Files();
        var worker = InventoryRecoveryChecks.KillWorker(typeof(StressRaceChecks).Assembly.Location, "--stress-pipe-parent", files.Directory, "unused");
        try
        {
            var first = await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(3)));
            Check(ReferenceEquals(first, worker), "An exited crash worker blocked forever while draining an inherited pipe.");
            try { await worker; throw new InvalidOperationException("An unsuccessful crash worker was accepted."); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Worker exited before the recovery marker", StringComparison.Ordinal)) { }
            var childId = int.Parse(await File.ReadAllTextAsync(Path.Combine(files.Directory, "child.pid")));
            try { using var child = Process.GetProcessById(childId); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (ArgumentException) { }
            Check(!Running(childId), "Recovery-worker cleanup left its pipe-owning descendant running.");
        }
        finally
        {
            var marker = Path.Combine(files.Directory, "child.pid");
            if (File.Exists(marker))
            {
                var childId = int.Parse(await File.ReadAllTextAsync(marker));
                try { using var child = Process.GetProcessById(childId); if (!child.HasExited) child.Kill(true); } catch (ArgumentException) { }
            }
            try { await worker.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    private static bool Running(int pid)
    { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }

    internal static async Task Utf8Helper()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("café 日本語");
        await Console.OpenStandardOutput().WriteAsync(bytes);
        await Console.OpenStandardError().WriteAsync(bytes);
    }

    private static async Task ToolOutputEncoding()
    {
        var original = Console.OutputEncoding;
        try
        {
            Console.OutputEncoding = System.Text.Encoding.ASCII;
            var arguments = new[] { typeof(StressRaceChecks).Assembly.Location, "--stress-utf8-helper" };
            var actual = await FfmpegChecks.Run("dotnet", arguments);
            Check(actual == "café 日本語", "UTF-8 media output was decoded using the console code page.");
            try
            {
                await FfmpegChecks.Run("dotnet", arguments, rejectDiagnostics: true);
                throw new InvalidOperationException("Expected UTF-8 tool diagnostics.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("café 日本語", StringComparison.Ordinal)) { }
        }
        finally { Console.OutputEncoding = original; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
