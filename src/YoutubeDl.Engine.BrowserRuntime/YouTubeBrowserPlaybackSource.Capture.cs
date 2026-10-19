using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine.BrowserRuntime;

public sealed partial class YouTubeBrowserPlaybackSource
{
    private async Task<PageMetadata> CaptureOnThreadAsync(string videoId, NativeDownloadRequest request, YouTubePlaybackCookies cookies, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var profile = Path.GetFullPath(Path.Combine(_profileRoot, Guid.NewGuid().ToString("N")));
        if (!profile.StartsWith(_profileRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The browser profile must remain inside its owned session directory.");
        var browser = new WebView2();
        var window = new Window { Width = 1280, Height = 720, Opacity = 0, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Content = browser };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new List<Task>(); var buffer = new YouTubeCaptureBuffer(videoId);
        CoreWebView2Environment? environment = null; var closing = false; var clean = false; var observing = false;
        PageMetadata? result = null;
        using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = captureStop.Token;
        try
        {
            Directory.CreateDirectory(profile);
            window.Show();
            var creating = CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions(BrowserNetworkSettings.Arguments(request)));
            environment = await creating.WaitAsync(token);
            environment.BrowserProcessExited += (_, _) => stopped.TrySetResult();
            var initializing = browser.EnsureCoreWebView2Async(environment); operations.Add(initializing);
            await initializing.WaitAsync(token);
            var core = browser.CoreWebView2;
            await BrowserNetworkSettings.ApplyAsync(core, request, token);
            await BrowserCookieSettings.ImportAsync(core, cookies, token);
            core.IsMuted = true; core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            if (!string.IsNullOrWhiteSpace(request.UserAgent)) core.Settings.UserAgent = request.UserAgent;
            var agent = core.Settings.UserAgent;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) =>
            {
                if (closing) { e.Cancel = true; return; }
                if (!IsWatchAddress(e.Uri, videoId))
                { e.Cancel = true; fault.TrySetResult(new NotSupportedException("YouTube requested a page requiring additional interaction.")); }
            };
            core.ProcessFailed += (_, _) => fault.TrySetResult(new InvalidOperationException("The browser playback session stopped unexpectedly."));
            core.AddWebResourceRequestedFilter("*://*.googlevideo.com/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (closing || observing || operations.Count >= 128 || e.Request.Method != "POST" || e.Request.Content is null) return;
                var deferral = e.GetDeferral(); observing = true; operations.Add(ReadRequestAsync(e, deferral));
            };
            core.WebResourceResponseReceived += (_, e) =>
            {
                if (closing || operations.Count >= 128 || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
                if (uri.Host == "www.youtube.com" && uri.AbsolutePath == "/youtubei/v1/player") operations.Add(ReadPlayerAsync(e));
                else if (e.Request.Method == "POST" && uri.Scheme == "https" && uri.Host.EndsWith(".googlevideo.com", StringComparison.Ordinal) && uri.AbsolutePath == "/videoplayback") operations.Add(ReadMediaAsync(e, uri));
            };
            var watch="https://www.youtube.com/watch?v="+videoId+"&autoplay=1"+(request.Authentication.YouTubeAccountIndex is { } account?"&authuser="+account.ToString(System.Globalization.CultureInfo.InvariantCulture):"");
            if(string.IsNullOrWhiteSpace(request.Referer))core.Navigate(watch);
            else core.NavigateWithWebResourceRequest(environment.CreateWebResourceRequest(watch,"GET",null,"Referer: "+request.Referer+"\r\n"));
            var elapsed = Stopwatch.StartNew();
            while (result is null)
            {
                token.ThrowIfCancellationRequested();
                if (fault.Task.IsCompleted) throw await fault.Task;
                // Read only this host-owned public watch page; do not click consent/sign-in UI.
                if (IsWatchAddress(core.Source, videoId))
                {
                    var raw = await core.ExecuteScriptAsync("(()=>{let v=document.querySelector('video');if(v&&!v.ended){v.muted=true;v.play().catch(()=>{});}return JSON.stringify(window.ytInitialPlayerResponse??null);})()").WaitAsync(token);
                    if (raw.Length <= 4 * 1024 * 1024)
                    {
                        var json = JsonSerializer.Deserialize<string>(raw);
                        if (json is { Length: > 0 } && json != "null") buffer.AddPlayer(json);
                    }
                }
                result = buffer.TryRead(agent, token,requireResponse:true);
                if (result is null && buffer.CanRefresh(elapsed.Elapsed))
                {
                    var refreshed = await core.ExecuteScriptAsync("(()=>{let p=document.getElementById('movie_player');if(p?.getVideoData?.()?.video_id!==" + JsonSerializer.Serialize(videoId) +
                        "||p.classList.contains('ad-showing')||typeof p.loadVideoById!=='function')return false;p.loadVideoById(" + JsonSerializer.Serialize(videoId) + ",0);return true;})()").WaitAsync(token);
                    if (refreshed == "true") buffer.Refreshed();
                }
                if (result is null) await Task.Delay(250, token);
            }
            if (!string.IsNullOrWhiteSpace(request.CookieFile) || !string.IsNullOrWhiteSpace(request.BrowserCookies))
                result = result with { PlaybackCookies = await BrowserCookieSettings.ExportAsync(core, token) };

            async Task ReadRequestAsync(CoreWebView2WebResourceRequestedEventArgs args, CoreWebView2Deferral deferral)
            {
                try
                {
                    var stream = args.Request.Content; var originalPosition = stream.CanSeek ? stream.Position : 0;
                    byte[] body;
                    try { body = await ReadBoundedAsync(stream, 256 * 1024, token); }
                    catch
                    {
                        // Never forward a partially consumed, non-seekable request body.
                        if (!stream.CanSeek && !closing) args.Response = environment.CreateWebResourceResponse(Stream.Null, 400, "Request could not be captured", "");
                        throw;
                    }
                    finally { if (stream.CanSeek) stream.Position = originalPosition; }
                    if (!stream.CanSeek) args.Request.Content = new MemoryStream(body, writable: false);
                    buffer.AddRequest(new Uri(args.Request.Uri), body);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException) { }
                catch (Exception) { if (!closing) fault.TrySetResult(new InvalidOperationException("The browser playback request could not be read.")); }
                finally
                {
                    observing = false;
                    try { deferral.Complete(); deferral.Dispose(); }
                    catch (Exception ex) when (closing && ex is ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
                }
            }
            async Task ReadPlayerAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
            {
                try
                {
                    using var content = await args.Response.GetContentAsync();
                    if (content is null) return;
                    var data = await ReadBoundedAsync(content, 2 * 1024 * 1024, token);
                    buffer.AddPlayer(new UTF8Encoding(false, true).GetString(data));
                }
                catch (NotSupportedException ex) { if (!closing) fault.TrySetResult(ex); }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            async Task ReadMediaAsync(CoreWebView2WebResourceResponseReceivedEventArgs args, Uri url)
            {
                try
                {
                    if (args.Response.StatusCode != 200 || !args.Response.Headers.GetHeader("Content-Type").StartsWith("application/vnd.yt-ump", StringComparison.OrdinalIgnoreCase)) return;
                    using var content = await args.Response.GetContentAsync(); if (content is null) return;
                    var bytes = await ReadBoundedAsync(content, 8 * 1024 * 1024, token);
                    await buffer.AddResponseAsync(url, bytes, token);
                }
                catch (NotSupportedException ex) { if (!closing) fault.TrySetResult(ex); }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException or EndOfStreamException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
        }
        catch (WebView2RuntimeNotFoundException)
        { throw new NotSupportedException("YouTube playback requires the Microsoft Edge WebView2 runtime, which was not found."); }
        finally
        {
            closing = true; captureStop.Cancel();
            browser.Dispose(); window.Content = null; window.Close();
            try { await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            if (environment is not null) { try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { } }
            for (var attempt = 0; attempt < 8 && !clean; attempt++)
            {
                try { Directory.Delete(profile, recursive: true); clean = true; }
                catch (DirectoryNotFoundException) { clean = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (attempt < 7) await Task.Delay(250); }
            }
        }
        if (result is null) throw new InvalidOperationException("The browser session did not provide matching playback data.");
        return clean ? result : result with { Warnings = result.Warnings.Append("Temporary public browser session data could not be removed.").ToArray() };
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken ct)
    {
        using var memory = new MemoryStream(); var bytes = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(bytes, ct); if (count == 0) return memory.ToArray();
            if (memory.Length + count > maximum) throw new InvalidDataException("The browser playback response exceeds its limit.");
            memory.Write(bytes, 0, count);
        }
    }
}
