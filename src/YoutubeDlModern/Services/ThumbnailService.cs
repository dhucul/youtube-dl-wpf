using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoutubeDlModern.Services;

internal static class ThumbnailService
{
    private const long MaximumDownloadBytes = 6L * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<ImageSource?> LoadAsync(string? rawUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return null;

        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        try
        {
            using var response = await SendWithValidatedRedirectsAsync(uri, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                return null;

            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = GC.AllocateUninitializedArray<byte>(81920);
            long total = 0;
            while (true)
            {
                var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0)
                    break;
                total = checked(total + count);
                if (total > MaximumDownloadBytes)
                    return null;
                await memory.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }

            if (memory.Length == 0)
                return null;
            memory.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 512;
            image.DecodePixelHeight = 512;
            image.StreamSource = memory;
            image.EndInit();
            if (image.PixelWidth is <= 0 or > 4096 || image.PixelHeight is <= 0 or > 4096)
                return null;
            image.Freeze();
            return image;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(Uri initialUri, CancellationToken ct)
    {
        var uri = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            ValidateRemoteUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is < 300 or >= 400)
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirect == MaximumRedirects)
                throw new HttpRequestException("Thumbnail redirect limit exceeded.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }

        throw new HttpRequestException("Thumbnail redirect limit exceeded.");
    }

    private static void ValidateRemoteUri(Uri uri)
    {
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Only HTTP or HTTPS thumbnail URLs are allowed.");
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
                if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocal))
                    throw new InvalidOperationException("Local and private-network thumbnail URLs are not allowed.");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return true;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return true;

        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 0) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 198 && bytes[1] is 18 or 19) ||
               bytes[0] >= 224;
    }
}
