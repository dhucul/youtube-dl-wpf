using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine.BrowserRuntime;

/// <summary>Short-lived, anonymous Windows browser session acquisition. Downloads remain in the C# core.</summary>
public sealed partial class YouTubeBrowserPlaybackSource : IYouTubePlaybackSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _profileRoot;
    private readonly TimeSpan _timeout;
    private readonly Func<string, NativeDownloadRequest, CancellationToken, Task<PageMetadata>>? _runner;
    public YouTubeBrowserPlaybackSource(string? profileRoot = null, TimeSpan? captureTimeout = null)
    {
        _profileRoot = Path.GetFullPath(profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeDlModern", "native-browser-sessions"));
        _timeout = captureTimeout ?? TimeSpan.FromSeconds(90);
        if (_timeout < TimeSpan.FromSeconds(1) || _timeout > TimeSpan.FromMinutes(3)) throw new ArgumentOutOfRangeException(nameof(captureTimeout));
    }
    internal YouTubeBrowserPlaybackSource(Func<string, NativeDownloadRequest, CancellationToken, Task<PageMetadata>> runner, TimeSpan? timeout = null) : this(captureTimeout: timeout) => _runner = runner;
    public async Task<PageMetadata> CaptureAsync(string videoId, NativeDownloadRequest request, CancellationToken ct)
    {
        Validate(videoId, request); ct.ThrowIfCancellationRequested();
        var cookies = await Task.Run(() => YouTubePlaybackCookies.LoadRequested(request, ct), ct).ConfigureAwait(false);
        return await CaptureAsync(videoId, request, cookies, ct).ConfigureAwait(false);
    }
    public async Task<PageMetadata> CaptureAsync(string videoId, NativeDownloadRequest request, YouTubePlaybackCookies cookies, CancellationToken ct)
    {
        Validate(videoId, request); ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout);
        var entered = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            return await (_runner is null ? OnBrowserThread(videoId, request, cookies, deadline.Token) : _runner(videoId, request, deadline.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("The public YouTube browser session did not become ready before its time limit."); }
        finally { if (entered) _gate.Release(); }
    }
    internal static void Validate(string videoId, NativeDownloadRequest request)
    {
        if (!Regex.IsMatch(videoId, @"\A[A-Za-z0-9_-]{11}\z", RegexOptions.CultureInvariant)) throw new ArgumentException("A valid YouTube video ID is required.");
        _ = BrowserNetworkSettings.Arguments(request);
        _ = Download.ByteRateLimiter.Parse(request.RateLimit);
        var unsupported = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.SourceAddress) || request.IpVersion != 0) unsupported.Add("local address or IP-version selection");
        if (request.TimeoutSeconds != 30) unsupported.Add("a custom browser connection timeout");
        if (request.HttpHeaders.Count != 0) unsupported.Add("custom request headers");
        if(!string.IsNullOrWhiteSpace(request.Referer)&&(!Uri.TryCreate(request.Referer,UriKind.Absolute,out var reference)||reference.Scheme is not ("https" or "http")||!reference.IsDefaultPort||reference.UserInfo.Length>0||reference.Fragment.Length>0||request.Referer.Length>8192||request.Referer.Any(char.IsControl)))throw new ArgumentException("The browser publisher referrer is invalid.");
        if (unsupported.Count > 0) throw new NotSupportedException("Public browser playback does not yet support " + string.Join(", ", unsupported) + ".");
        if (request.UserAgent is { } agent && (agent.Length > 4096 || agent.Any(char.IsControl))) throw new ArgumentException("The browser user agent is invalid.");
    }
    internal static bool IsWatchAddress(string raw, string videoId)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "www.youtube.com" ||
            !url.IsDefaultPort || url.UserInfo.Length != 0 || url.AbsolutePath != "/watch") return false;
        var ids = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair[0] == "v")
            .Select(pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "").ToArray();
        return ids.Length == 1 && ids[0] == videoId;
    }
    private Task<PageMetadata> OnBrowserThread(string videoId, NativeDownloadRequest request, YouTubePlaybackCookies cookies, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<PageMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            PageMetadata? page = null; Exception? failure = null;
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(async () =>
                {
                    try { page = await CaptureOnThreadAsync(videoId, request, cookies, ct); }
                    catch (Exception ex) { failure = ex; }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                });
                Dispatcher.Run();
                if (failure is OperationCanceledException) completion.TrySetCanceled(ct);
                else if (failure is not null) completion.TrySetException(failure);
                else if (page is not null) completion.TrySetResult(page);
                else completion.TrySetException(new InvalidOperationException("The browser session ended without playback data."));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "YouTube native session" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
