using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers;

/// <summary>Reissues public player links without replaying a page's old CDN signatures.</summary>
internal sealed class PornhubPlaybackSession : IPlaybackAuthorization, IDisposable
{
    private readonly MediaHttp _issuer;
    private readonly NativeDownloadRequest _request;
    private readonly Func<NativeDownloadRequest, MediaHttp, MediaDocument, CancellationToken, Task<PageMetadata>> _parse;
    private readonly SessionLifetime _lifetime = new();
    private RenewableMediaAuthorization? _lease;
    private PageMetadata? _initial;
    private int _refreshes;

    internal PornhubPlaybackSession(MediaHttp owner, NativeDownloadRequest request,
        Func<NativeDownloadRequest, MediaHttp, MediaDocument, CancellationToken, Task<PageMetadata>> parse)
    { _issuer = owner.CreatePublicServiceSession(); _request = request; _parse = parse; }

    private static bool Secure(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0;
    internal static bool CanHandle(Uri url) => Secure(url) && (url.Host == "pornhub.com" || url.Host.EndsWith(".pornhub.com", StringComparison.OrdinalIgnoreCase)) &&
        url.AbsolutePath == "/view_video.php" && ViewKey(url) is not null;
    private static string? ViewKey(Uri url)
    {
        var keys = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair[0] == "viewkey").ToArray();
        return keys.Length == 1 && keys[0].Length == 2 && keys[0][1].Length is > 0 and <= 128 && keys[0][1].All(c => char.IsAsciiLetterOrDigit(c)) ? keys[0][1] : null;
    }
    internal static bool MediaHost(Uri url) => Secure(url) && url.Host.EndsWith(".phncdn.com", StringComparison.OrdinalIgnoreCase);
    private string Referer => string.IsNullOrWhiteSpace(_request.Referer) ? _request.Url.AbsoluteUri : _request.Referer;

    internal async Task<PageMetadata> ReadAsync(CancellationToken ct)
    {
        using var operation = _lifetime.Begin(ct); ct = operation.Token;
        await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initial is null)
            {
                var page = await ReadFreshCoreAsync(ct).ConfigureAwait(false);
                // Other generic/embedded page shapes keep their existing discovery behavior.
                if (page.Extractor != "generic:player" || page.DrmRefused || page.IsPlaylist || page.IsLive || page.Media.Count == 0 ||
                    page.Media.Any(media => !MediaHost(media.Url)) || page.Subtitles.Count != 0) return page;
                _lifetime.Commit(ct, () =>
                {
                    _initial = page;
                    _lease = new(page, FreshAsync, MediaHost, ReadManifestAsync, maximumRenewals: 12);
                });
            }
            return _initial! with { PlaybackAuthorization = this, AnonymousPlayback = true, PublicServicePlayback = true,
                PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = Referer } };
        }
        finally { _lifetime.Gate.Release(); }
    }

    private async Task<PageMetadata> ReadFreshCoreAsync(CancellationToken ct, bool renewal = false)
    {
        // A rejected anonymous generation can leave sticky issuer routing cookies.
        // Renew from a fresh public navigation context; no user/account jar is read.
        using var fresh = renewal ? _issuer.CreatePublicServiceSession() : null;
        using var navigation = (fresh ?? _issuer).WithHeaders(new Dictionary<string, string>
        {
            ["Cache-Control"] = "no-cache, no-store, max-age=0", ["Pragma"] = "no-cache",
            ["Referer"] = string.IsNullOrWhiteSpace(_request.Referer) ? "" : _request.Referer
        });
        // The cache key must change as well as the cache directives: some public
        // page caches replay stale signatures despite no-cache. Never alter CDN tokens.
        var address = _request.Url;
        if (renewal)
        {
            var builder = new UriBuilder(address);
            builder.Query = address.Query.TrimStart('?') + "&_native_refresh=" + Guid.NewGuid().ToString("N");
            address = builder.Uri;
        }
        var document = await navigation.ReadPlaybackManifestAsync(address,
            url => CanHandle(url) && ViewKey(url) == ViewKey(_request.Url), null, ct).ConfigureAwait(false);
        // The approved page can canonicalize its host, but its video key cannot change.
        if (!CanHandle(document.Url) || ViewKey(document.Url) != ViewKey(_request.Url)) throw new InvalidDataException("The refreshed player changed its video address.");
        return await _parse(_request, _issuer, document with { Url = _request.Url }, ct).ConfigureAwait(false);
    }

    private async Task<PageMetadata> FreshAsync(CancellationToken ct)
    {
        using var operation = _lifetime.Begin(ct); ct = operation.Token;
        await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Give the issuer a short, bounded interval to replace a rejected
            // generation instead of immediately replaying its cached response.
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(_refreshes++, 2))), ct).ConfigureAwait(false);
            PageMetadata fresh;
            using (NativeExtractionDelay.Extracting()) fresh = await ReadFreshCoreAsync(ct, renewal: true).ConfigureAwait(false);
            var initial = _initial!;
            if (fresh.Id != initial.Id || fresh.Extractor != initial.Extractor || fresh.DurationSeconds != initial.DurationSeconds ||
                fresh.DrmRefused || fresh.IsLive || fresh.IsPlaylist || fresh.Subtitles.Count != 0 || fresh.Media.Any(media => !MediaHost(media.Url)))
                throw new InvalidDataException("The refreshed player changed the selected video or denied playback.");
            var matched = new HashSet<MediaCandidate>(ReferenceEqualityComparer.Instance);
            var formats = new List<MediaCandidate>();
            foreach (var old in initial.Media)
            {
                // CDN host and signing query may change; asset path and rendition facts may not.
                var offered = fresh.Media.Where(media => Fingerprint(media) == Fingerprint(old)).ToArray();
                if (offered.Length == 1 && matched.Add(offered[0])) formats.Add(offered[0].WithIdentity(old.Id!));
                else if (_lease!.IsActive("format:" + old.Id)) throw new InvalidDataException("The selected playback rendition disappeared or became ambiguous.");
            }
            return fresh with { Media = formats };
        }
        finally { _lifetime.Gate.Release(); }
    }

    private static string Fingerprint(MediaCandidate media) => JsonSerializer.Serialize(new object?[] { media.Url.AbsolutePath, media.Kind, media.Extension,
        media.Height, media.Width, media.Bandwidth, media.FramesPerSecond, media.FileSize, media.VideoCodec, media.AudioCodec, media.HasAudio, media.HasVideo,
        media.AudioLanguage, media.IsDefaultAudio, media.Label, media.IsCompleteMedia, media.IsFallback });
    private Task<MediaDocument> ReadManifestAsync(Uri url, CancellationToken ct)
    {
        if (!MediaHost(url)) throw new InvalidDataException("The renewed manifest left the publisher's CDN.");
        return _issuer.ReadPlaybackManifestAsync(url, MediaHost, Referer, ct);
    }

    public int MaximumRenewalAttempts => 6;
    public bool ShouldRenew(HttpStatusCode status) => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Gone or HttpStatusCode.PreconditionFailed;
    public bool AppliesTo(Uri url)
    {
        if (_lease?.AppliesTo(url) != true) return false;
        if (!MediaHost(url)) throw new InvalidDataException("The declared playback resource left the publisher's CDN.");
        return true;
    }
    public Task<PlaybackLease> AuthorizeAsync(Uri url, CancellationToken ct) => _lease!.AuthorizeAsync(url, ct);
    public async Task<PlaybackLease> RenewAsync(Uri url, long rejectedGeneration, CancellationToken ct)
    {
        // A newly issued generation can itself contain stale child manifests.
        // Retire none of the accepted mappings until a complete refresh validates.
        for (var attempt = 0; ; attempt++)
        {
            try { return await _lease!.RenewAsync(url, rejectedGeneration, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (attempt < 2 && ex.StatusCode is { } status && ShouldRenew(status)) { ct.ThrowIfCancellationRequested(); }
        }
    }
    public Uri Canonicalize(Uri url) => _lease!.Canonicalize(url);
    public string? RecoveryIdentity(Uri url) => _lease!.RecoveryIdentity(url);
    public bool TryRedirect(Uri source, Uri destination) => _lease!.TryRedirect(source, destination);
    public Task ObserveManifestAsync(MediaDocument document, CancellationToken ct) => _lease!.ObserveManifestAsync(document, ct);
    public void Dispose() { _lifetime.Dispose(() => { _lease?.Dispose(); }); _issuer.Dispose(); }
}
