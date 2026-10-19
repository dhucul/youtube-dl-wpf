using System.Net;
using System.Net.Http.Headers;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Protocols.Sabr;

/// <summary>Executes bounded playback requests against a caller-supplied, authorized SABR session.</summary>
public sealed class SabrHttpSession(MediaHttp http, string? userAgent = null)
{
    public string BufferSize { get; init; } = "";
    public bool NoResizeBuffer { get; init; }
    internal Func<CancellationToken,Task<Uri>>? Renew { get; init; }
    internal Func<SabrControlState,SabrSegmentAssembler,CancellationToken,Task>? RestorePrefix {get;init;}
    public async Task DownloadAsync(Uri endpoint, string videoId, SabrTrackSink sink,
        Func<SabrControlState, int, SabrRequest> createRequest, CancellationToken ct, int maximumRequests = 10000, bool allowStartupPlayback = false,
        int retries = 0, Download.ByteRateLimiter? limiter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequests);
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        _ = Download.NativeReadBuffer.ParseSize(BufferSize);
        if (http.AllowAutoRedirect) throw new ArgumentException("SABR sessions require automatic HTTP redirects to be disabled.", nameof(http));
        ValidateEndpoint(endpoint);
        var control = new SabrControlState(videoId, allowStartupPlayback);
        using var assembler = new SabrSegmentAssembler(videoId);
        var redirects = 0;
        var stalled = 0;
        var failedAttempts = 0;
        var renewed=0;
        for (var number = 0; number < maximumRequests; number++)
        {
            ct.ThrowIfCancellationRequested();
            if(Http.RenewableMediaAuthorization.Expiry(endpoint) is { } expires&&expires<=DateTimeOffset.UtcNow.AddSeconds(5))
            {
                if(Renew is null||++renewed>2)throw new NotSupportedException("The signed SABR session expired and has no remaining renewal.");
                endpoint=await Renew(ct).ConfigureAwait(false);ValidateEndpoint(endpoint);assembler.DiscardPending();control.ResetSession();
                if(Http.RenewableMediaAuthorization.Expiry(endpoint) is { } refreshed&&refreshed<=DateTimeOffset.UtcNow)throw new InvalidDataException("The SABR issuer returned an expired session.");
            }
            var before = sink.TotalBytes;
            try
            {
                var playback = createRequest(control, number);
                if (!playback.AudioFormats.Concat(playback.VideoFormats).ToHashSet().SetEquals(sink.SelectedFormats))
                    throw new ArgumentException("The SABR request must select exactly the tracks supplied to the download sink.", nameof(createRequest));
                var requestBody = (playback with { InitializedFormats = sink.InitializedFormats, BufferedRanges = sink.BufferedRanges }).Encode();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Content = new ByteArrayContent(requestBody);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
                if (!string.IsNullOrWhiteSpace(userAgent)) request.Headers.UserAgent.ParseAdd(userAgent);
                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location ?? throw new InvalidDataException("The SABR redirect is missing its destination.");
                    endpoint = location.IsAbsoluteUri ? location : new Uri(endpoint, location);
                    ValidateEndpoint(endpoint);
                    if (++redirects > 5) throw new InvalidDataException("The SABR session redirected too many times.");
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if(Renew is null||++renewed>2)throw new NotSupportedException("YouTube did not authorize the native playback session after its bounded renewal.");
                    response.Dispose();endpoint=await Renew(ct).ConfigureAwait(false);ValidateEndpoint(endpoint);assembler.DiscardPending();control.ResetSession();continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType != "application/vnd.yt-ump")
                    throw new InvalidDataException("The SABR server returned an unexpected response type.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var buffered = new Download.NativeBufferedReadStream(stream, http, BufferSize, NoResizeBuffer);
                await SabrResponseProcessor.ReadAsync(buffered, control, assembler, sink, ct, limiter).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && !sink.WriteFailed && failedAttempts < retries && ex is IOException or HttpRequestException or TimeoutException)
            {
                assembler.DiscardPending();
                control.ResetSession();
                failedAttempts++;
                number--;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, failedAttempts)), ct).ConfigureAwait(false);
                continue;
            }
            failedAttempts = 0;
            if (control.RequiresPlayerReload)
            {
                if(Renew is null||++renewed>2)throw new NotSupportedException("The YouTube playback session exceeded its bounded reload count.");
                endpoint=await Renew(ct).ConfigureAwait(false);ValidateEndpoint(endpoint);assembler.DiscardPending();control.ResetSession();continue;
            }
            if (control.Redirect is { } redirect)
            {
                ValidateEndpoint(redirect);
                endpoint = redirect;
                if (++redirects > 5) throw new InvalidDataException("The SABR session redirected too many times.");
            }
            if(RestorePrefix is not null)await RestorePrefix(control,assembler,ct).ConfigureAwait(false);
            if (sink.IsComplete(control)) return;
            stalled = before == sink.TotalBytes ? stalled + 1 : 0;
            if (stalled >= 3) throw new EndOfStreamException("The SABR session stopped providing the selected media tracks.");
            if (control.BackoffMilliseconds > 0) await Task.Delay(control.BackoffMilliseconds, ct).ConfigureAwait(false);
        }
        throw new EndOfStreamException("The SABR session exceeded its request limit before all tracks completed.");
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || !endpoint.IsDefaultPort || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.Fragment.Length != 0 ||
            !endpoint.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Supply an HTTPS GoogleVideo playback endpoint without credentials or a custom port.", nameof(endpoint));
    }
}
