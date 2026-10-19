using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Newgrounds;

namespace YoutubeDl.Engine.BrowserRuntime;

/// <summary>An owned Windows browser session supplies raw public metadata; the C# core downloads media.</summary>
public sealed class NewgroundsBrowserPageSource : INewgroundsPageSource
{
    private readonly string _profileRoot;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<NativeDownloadRequest, CancellationToken, Task<PageMetadata>>? _runner;
    internal Action<MediaDocument>? DocumentObserver { get; init; }

    public NewgroundsBrowserPageSource(string? profileRoot = null, TimeSpan? captureTimeout = null)
    {
        _profileRoot = Path.GetFullPath(profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeDlModern", "native-newgrounds-sessions"));
        _timeout = captureTimeout ?? TimeSpan.FromMinutes(2);
        if (_timeout < TimeSpan.FromSeconds(1) || _timeout > TimeSpan.FromMinutes(3)) throw new ArgumentOutOfRangeException(nameof(captureTimeout));
    }
    internal NewgroundsBrowserPageSource(Func<NativeDownloadRequest, CancellationToken, Task<PageMetadata>> runner, TimeSpan? timeout = null) : this(captureTimeout: timeout) => _runner = runner;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, CancellationToken ct)
    {
        Validate(request); ct.ThrowIfCancellationRequested();
        var cookies = await Task.Run(() => LoadCookies(request, ct), ct).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(_timeout); var entered = false;
        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            return await (_runner is null ? OnBrowserThread(request, cookies, deadline.Token) : _runner(request, deadline.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("The public Newgrounds browser session did not become ready before its time limit."); }
        finally { if (entered) _gate.Release(); }
    }
    internal static void Validate(NativeDownloadRequest request)
    {
        if (!new NewgroundsProvider().CanHandle(request.Url)) throw new ArgumentException("Use a supported public Newgrounds address.");
        _ = BrowserNetworkSettings.Arguments(request); _ = ByteRateLimiter.Parse(request.RateLimit);
        if (!string.IsNullOrWhiteSpace(request.SourceAddress) || request.IpVersion != 0 || request.TimeoutSeconds != 30 || request.HttpHeaders.Count != 0)
            throw new NotSupportedException("Newgrounds browser discovery requires additional support for the configured connection timeout, address or custom headers.");
        if (request.Referer is { Length: > 0 } referer && (referer.Length > 32768 || referer.Any(char.IsControl) || !Uri.TryCreate(referer, UriKind.Absolute, out var page) ||
            page.Scheme is not ("http" or "https") || page.UserInfo.Length != 0)) throw new ArgumentException("The browser referrer must be a public HTTP page address.");
        if (request.UserAgent is { } agent && (agent.Length > 4096 || agent.Any(char.IsControl))) throw new ArgumentException("The browser user agent is invalid.");
    }
    private static Cookie[] LoadCookies(NativeDownloadRequest request, CancellationToken ct)
    {
        var jar = !string.IsNullOrWhiteSpace(request.CookieFile) ? NetscapeCookieFile.Load(request.CookieFile, ct) :
            !string.IsNullOrWhiteSpace(request.BrowserCookies) ? BrowserCookieReader.Load(request.BrowserCookies, ct) : null;
        if (jar is null) return [];
        var cookies = jar.GetAllCookies().Cast<Cookie>().Where(cookie => !cookie.Expired && (cookie.Domain.TrimStart('.').Equals("newgrounds.com", StringComparison.OrdinalIgnoreCase) ||
            cookie.Domain.EndsWith(".newgrounds.com", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (cookies.Length > 2000 || cookies.Any(cookie => cookie.Version != 0 || cookie.Port.Length != 0 || Encoding.UTF8.GetByteCount(cookie.Name + cookie.Value + cookie.Domain + cookie.Path) > 4096))
            throw new NotSupportedException("The selected Newgrounds browser cookie set exceeds its supported limits.");
        return cookies;
    }
    private Task<PageMetadata> OnBrowserThread(NativeDownloadRequest request, Cookie[] cookies, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<PageMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            PageMetadata? page = null; Exception? failure = null;
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(async () =>
                {
                    try { page = await CaptureAsync(request, cookies, ct); } catch (Exception ex) { failure = ex; }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                });
                Dispatcher.Run();
                if (failure is OperationCanceledException) completion.TrySetCanceled(ct);
                else if (failure is not null) completion.TrySetException(failure);
                else if (page is not null) completion.TrySetResult(page);
                else completion.TrySetException(new InvalidOperationException("The browser session ended without public metadata."));
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Newgrounds native discovery" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private async Task<PageMetadata> CaptureAsync(NativeDownloadRequest request, Cookie[] cookies, CancellationToken ct)
    {
        var profile = Path.GetFullPath(Path.Combine(_profileRoot, Guid.NewGuid().ToString("N")));
        if (!profile.StartsWith(_profileRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The browser profile must remain inside its owned session directory.");
        var browser = new WebView2(); var window = new Window { Width = 1280, Height = 720, Opacity = 0, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Content = browser };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var operations = new List<Task>();
        CoreWebView2Environment? environment = null; TaskCompletionSource<HttpResponseMessage>? pending = null; HttpRequestMessage? pendingRequest = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct); var token = stop.Token; var closing = false; var clean = false;
        try
        {
            Directory.CreateDirectory(profile); window.Show();
            environment = await CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions(BrowserNetworkSettings.Arguments(request))).WaitAsync(token);
            environment.BrowserProcessExited += (_, _) => stopped.TrySetResult(); await browser.EnsureCoreWebView2Async(environment).WaitAsync(token);
            var core = browser.CoreWebView2; await BrowserNetworkSettings.ApplyAsync(core, request, token);
            core.IsMuted = true; core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            if (!string.IsNullOrWhiteSpace(request.UserAgent)) core.Settings.UserAgent = request.UserAgent;
            foreach (var cookie in cookies)
            {
                token.ThrowIfCancellationRequested(); var value = core.CookieManager.CreateCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
                value.IsSecure = cookie.Secure; value.IsHttpOnly = cookie.HttpOnly; if (cookie.Expires != DateTime.MinValue) value.Expires = cookie.Expires.ToUniversalTime();
                core.CookieManager.AddOrUpdateCookie(value);
            }
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, e) => e.Handled = true; core.DownloadStarting += (_, e) => e.Cancel = true; core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, _) => pending?.TrySetException(new InvalidOperationException("The browser discovery session stopped unexpectedly."));
            core.NavigationStarting += (_, e) =>
            {
                if (closing || pendingRequest is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !NewgroundsProvider.IsMetadataAddress(uri, request.Url))
                { e.Cancel = true; if (!closing) pending?.TrySetException(new NotSupportedException("Newgrounds requested a page requiring additional interaction.")); }
            };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) => { if (!closing && e.ResourceContext == CoreWebView2WebResourceContext.Media) e.Response = environment.CreateWebResourceResponse(Stream.Null, 204, "Media stays in native core", ""); };
            core.WebResourceResponseReceived += (_, e) =>
            {
                if (closing || pendingRequest is null || pending is null || e.Request.Method != pendingRequest.Method.Method || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) ||
                    !NewgroundsProvider.IsMetadataAddress(uri, pendingRequest.RequestUri!) || operations.Count >= 256) return;
                var target = pending; var original = pendingRequest; operations.Add(ReadResponseAsync(e, uri, original, target));
            };
            using var http = new MediaHttp(new NavigationHandler(async (message, cancel) =>
            {
                if (message.RequestUri is null || !NewgroundsProvider.IsMetadataAddress(message.RequestUri, request.Url) ||
                    message.Method != HttpMethod.Get && (message.Method != HttpMethod.Post || message.RequestUri.AbsolutePath != "/visual-links-fetch"))
                    throw new NotSupportedException("Browser discovery is limited to the requested public Newgrounds metadata scope.");
                return await window.Dispatcher.InvokeAsync(async () =>
                {
                    cancel.ThrowIfCancellationRequested(); pendingRequest = message; pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var headers = string.Join("\r\n", message.Headers.Where(header => header.Key is not ("User-Agent" or "Cookie" or "Authorization")).Select(header => header.Key + ": " + string.Join(", ", header.Value)));
                    var bytes = message.Content is null ? [] : await message.Content.ReadAsByteArrayAsync(cancel); if (bytes.Length > 64 * 1024) throw new InvalidDataException("The browser metadata request exceeds its body limit.");
                    using var body = new MemoryStream(bytes, writable: false);
                    if (message.Content?.Headers.ContentType is { } contentType) headers += "\r\nContent-Type: " + contentType;
                    var resource = environment.CreateWebResourceRequest(message.RequestUri.AbsoluteUri, message.Method.Method, body, headers);
                    core.NavigateWithWebResourceRequest(resource); return await pending.Task.WaitAsync(cancel);
                }).Task.Unwrap().ConfigureAwait(false);
            }), new MediaHttpOptions { UserAgent = core.Settings.UserAgent, Referer = request.Referer ?? "", TimeoutSeconds = 300 });
            var result = await new NewgroundsProvider().ExtractAsync(request, http, token);
            return result with { PlaybackHeaders = new Dictionary<string, string> { ["User-Agent"] = core.Settings.UserAgent, ["Referer"] = request.Referer ?? request.Url.AbsoluteUri } };

            async Task ReadResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args, Uri uri, HttpRequestMessage original, TaskCompletionSource<HttpResponseMessage> target)
            {
                try
                {
                    using var content = await args.Response.GetContentAsync(); if (content is null) return;
                    var bytes = await ReadBoundedAsync(content, token); var text = new UTF8Encoding(false, true).GetString(bytes);
                    if (args.Response.StatusCode is >= 300 and < 400) return;
                    if (args.Response.StatusCode == 403 && text.Contains("<title>NG Guard</title>", StringComparison.Ordinal)) return;
                    DocumentObserver?.Invoke(new MediaDocument(uri, text, args.Response.Headers.Contains("Content-Type") ? args.Response.Headers.GetHeader("Content-Type") : null));
                    var response = new HttpResponseMessage((HttpStatusCode)args.Response.StatusCode) { Content = new ByteArrayContent(bytes), RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri) };
                    if (args.Response.Headers.Contains("Content-Type")) response.Content.Headers.TryAddWithoutValidation("Content-Type", args.Response.Headers.GetHeader("Content-Type"));
                    if (!target.TrySetResult(response)) response.Dispose();
                }
                catch (Exception ex) { if (!closing) target.TrySetException(ex); }
            }
        }
        catch (WebView2RuntimeNotFoundException) { throw new NotSupportedException("Newgrounds discovery requires the Microsoft Edge WebView2 runtime, which was not found."); }
        finally
        {
            closing = true; stop.Cancel(); pending?.TrySetCanceled(token); browser.Dispose(); window.Content = null; window.Close();
            try { await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            if (environment is not null) { try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { } }
            for (var attempt = 0; attempt < 8 && !clean; attempt++)
            {
                try { if ((File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0) throw new IOException("The browser profile is no longer an owned directory."); Directory.Delete(profile, true); clean = true; }
                catch (DirectoryNotFoundException) { clean = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (attempt < 7) await Task.Delay(250); }
            }
            if (!clean) throw new IOException("The temporary Newgrounds browser profile could not be removed.");
        }
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken ct)
    {
        using var output = new MemoryStream(); var bytes = new byte[64 * 1024];
        while (true) { var count = await input.ReadAsync(bytes, ct); if (count == 0) return output.ToArray(); if (output.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("The browser metadata exceeds its size limit."); await output.WriteAsync(bytes.AsMemory(0, count), ct); }
    }
    private sealed class NavigationHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
}
