using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine.BrowserRuntime;

internal static class BrowserNetworkSettings
{
    internal static string Arguments(NativeDownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Proxy)) return "";
        if (request.Proxy.Any(char.IsControl)) throw new ArgumentException("The proxy address contains invalid characters.");
        if (!Uri.TryCreate(request.Proxy, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https") ||
            proxy.Host.Length == 0 || proxy.Port <= 0 || proxy.AbsolutePath != "/" || proxy.Query.Length != 0 || proxy.Fragment.Length != 0)
            throw new NotSupportedException("Browser playback requires an HTTP or HTTPS proxy server address without a path or query.");
        if (proxy.UserInfo.Length != 0) throw new NotSupportedException("Authenticated browser proxies require additional support.");
        // Serialize only parsed authority components, never raw command-line input.
        var arguments = "--proxy-server=\"" + proxy.GetLeftPart(UriPartial.Authority) + "\" --proxy-bypass-list=\"<-loopback>\"";
        // Proxy TLS failures do not reach the page's certificate event. This fixed
        // switch applies only to the explicitly opted-in, isolated browser process.
        if (proxy.Scheme == "https" && request.NoCheckCertificate) arguments += " --ignore-certificate-errors";
        return arguments;
    }
    internal static string Conditions(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        return JsonSerializer.Serialize(new { offline = false, matchedNetworkConditions = new[] { new { urlPattern = "", latency = 0, downloadThroughput = bytesPerSecond, uploadThroughput = -1 } } });
    }
    internal static async Task ApplyAsync(CoreWebView2 core, NativeDownloadRequest request, CancellationToken ct)
    {
        if (request.NoCheckCertificate)
            core.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        if (ByteRateLimiter.Parse(request.RateLimit) is not { } rate) return;
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}").WaitAsync(ct);
            await core.CallDevToolsProtocolMethodAsync("Network.emulateNetworkConditionsByRule", Conditions(rate)).WaitAsync(ct);
        }
        catch (COMException)
        { throw new NotSupportedException("This WebView2 runtime could not apply the requested download-rate limit."); }
    }
}
