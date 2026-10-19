using YoutubeDl.Engine;
using YoutubeDl.Engine.BrowserRuntime;
using YoutubeDl.Engine.Discovery;

internal static partial class Program
{
    private static NativeDownloadRequest NewgroundsBrowserRequest() => new() { Url = new("https://www.newgrounds.com/audio/listen/42"), OutputDirectory = TestRoot };
    private static async Task NewgroundsBrowserContract()
    {
        var calls = 0; var source = new NewgroundsBrowserPageSource((request, ct) => { ct.ThrowIfCancellationRequested(); calls++; return Task.FromResult(new PageMetadata { Id = "42", Title = "Audio" }); });
        var page = await source.ExtractAsync(NewgroundsBrowserRequest(), default);
        Assert(page.Id == "42" && calls == 1, "Newgrounds browser metadata contract changed.");
        var referring = await source.ExtractAsync(NewgroundsBrowserRequest() with { Referer = "https://artist.newgrounds.com/audio" }, default);
        Assert(referring.Id == "42" && calls == 2, "Selected collection referrers were rejected by browser discovery.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await source.ExtractAsync(NewgroundsBrowserRequest(), cancel.Token); throw new InvalidOperationException("Pre-cancelled Newgrounds source started."); }
        catch (OperationCanceledException) { Assert(calls == 2, "Cancelled browser source invoked a runner."); }
    }
    private static async Task NewgroundsBrowserValidation()
    {
        var source = new NewgroundsBrowserPageSource((request, ct) => throw new InvalidOperationException("Invalid browser settings started a session."));
        foreach (var request in new[] { NewgroundsBrowserRequest() with { Url = new("https://www.newgrounds.com/passport") }, NewgroundsBrowserRequest() with { SourceAddress = "127.0.0.1" },
            NewgroundsBrowserRequest() with { IpVersion = 4 }, NewgroundsBrowserRequest() with { TimeoutSeconds = 5 },
            NewgroundsBrowserRequest() with { HttpHeaders = new Dictionary<string, string> { ["X-Private"] = "fixture" } }, NewgroundsBrowserRequest() with { Proxy = "https://user:secret@proxy.example" },
            NewgroundsBrowserRequest() with { Referer = "file:///private" } })
        {
            try { await source.ExtractAsync(request, default); throw new InvalidOperationException("Unsupported browser settings were accepted."); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
    }
    private static async Task NewgroundsBrowserTimeout()
    {
        var source = new NewgroundsBrowserPageSource(async (request, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new PageMetadata(); }, TimeSpan.FromSeconds(1));
        try { await source.ExtractAsync(NewgroundsBrowserRequest(), default); throw new InvalidOperationException("Browser timeout was ignored."); }
        catch (TimeoutException) { }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        try { await source.ExtractAsync(NewgroundsBrowserRequest(), cancel.Token); throw new InvalidOperationException("Caller cancellation was ignored."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    }
    private static async Task NewgroundsBrowserGate()
    {
        var active = 0; var maximum = 0; var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new NewgroundsBrowserPageSource(async (request, ct) =>
        {
            var count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count); firstEntered.TrySetResult();
            try { await release.Task.WaitAsync(ct); return new PageMetadata { Id = "42" }; } finally { Interlocked.Decrement(ref active); }
        });
        var first = source.ExtractAsync(NewgroundsBrowserRequest(), default); await firstEntered.Task;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        try { await source.ExtractAsync(NewgroundsBrowserRequest(), cancel.Token); throw new InvalidOperationException("Queued browser cancellation was ignored."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        release.SetResult(); await first; await source.ExtractAsync(NewgroundsBrowserRequest(), default);
        Assert(maximum == 1 && active == 0, "Browser gate leaked or allowed overlapping sessions.");
    }
}
