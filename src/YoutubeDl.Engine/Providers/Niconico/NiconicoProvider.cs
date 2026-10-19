using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Niconico;

/// <summary>Public long-form watch pages and issuer-granted clear DOMAND playback.</summary>
public sealed class NiconicoProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("niconico", "Niconico public DOMAND video", 203);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "www.nicovideo.jp" or "nicovideo.jp" or "sp.nicovideo.jp" or "embed.nicovideo.jp";
    internal static string Id(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host is not ("www.nicovideo.jp" or "nicovideo.jp" or "sp.nicovideo.jp" or "embed.nicovideo.jp") || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0) throw new ArgumentException("Use a canonical Niconico video watch URL.");
        var match = Match(url.AbsolutePath, @"\A/watch/(?<id>(?:sm|nm|so)[1-9][0-9]{0,11})/?\z");
        return match.Success ? match.Groups["id"].Value : throw new NotSupportedException("This Niconico route is not supported yet.");
    }
    public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = Id(request.Url);
        return http.SessionState(Key(Info.Id, request), () => new Session(http, request, id)).ReadAsync(ct);
    }
    private sealed class Session : IPlaybackAuthorization, IDisposable
    {
        private readonly string _id;
        private readonly NativeDownloadRequest _request;
        private readonly MediaHttp _service;
        private readonly SessionLifetime _lifetime = new();
        private RenewableMediaAuthorization? _lease;
        private PageMetadata? _initial;
        private string? _cookie;
        internal Session(MediaHttp owner, NativeDownloadRequest request, string id) { _id = id; _request = request; _service = owner.CreateVerificationSession(null); }
        private Uri Watch => new("https://www.nicovideo.jp/watch/" + _id);
        internal async Task<PageMetadata> ReadAsync(CancellationToken ct)
        {
            using var operation = _lifetime.Begin(ct); await _lifetime.Gate.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                if (_initial is null)
                {
                    _initial = await Fresh(operation.Token).ConfigureAwait(false);
                    if (_initial.Media.Count > 0) _lease = new(_initial, Fresh, Delivery, ReadManifest);
                }
                return _initial with { PlaybackAuthorization = _lease is null ? null : this, AnonymousPlayback = true, PublicServicePlayback = true, SourcePageUrl = Watch, PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = Watch.AbsoluteUri } };
            }
            finally { _lifetime.Gate.Release(); }
        }
        private async Task<PageMetadata> Fresh(CancellationToken ct)
        {
            var envelope = await Read(_service, new(Watch.AbsoluteUri + "?responseType=json"), ct).ConfigureAwait(false);
            if (Integer(Object(envelope, "meta"), "status") != 200) throw new InvalidDataException("Niconico denied the selected watch page.");
            var response = Object(Object(envelope, "data"), "response"); var video = Object(response, "video"); var client = Object(response, "client"); var payment = Object(Object(response, "payment"), "video");
            if (Text(video, "id") != _id || Text(client, "watchId") != _id || Text(video, "contentType") != "long") throw new InvalidDataException("Niconico returned another video or unsupported media type.");
            var duration = Number(video, "duration"); if (duration is not (> 0 and <= 604800) || _initial is not null && duration != _initial.DurationSeconds) throw new InvalidDataException("Niconico changed or omitted the full video duration.");
            var timestamp = Date(Text(video, "registeredAt")); var count = Object(video, "count");
            var page = new PageMetadata { Id = _id, Extractor = "Niconico", ArchiveIdentity = new("Niconico", _id), Title = Title(video), Description = Text(video, "description"), DurationSeconds = duration, UploadTimestamp = timestamp, UploadDate = timestamp is null ? null : DateOnly.FromDateTime(timestamp.Value.UtcDateTime), Uploader = Text(Object(response, "owner"), "nickname"), ViewCount = Long(count, "view"), AgeLimit = True(Object(video, "rating"), "isAdult") ? 18 : 0, ThumbnailUrl = Text(Object(video, "thumbnail"), "url") is { } image ? Secure(image, "nicovideo.cdn.nimg.jp", "img.cdn.nimg.jp").AbsoluteUri : null, AnonymousPlayback = true, PublicServicePlayback = true, SourcePageUrl = Watch };
            if (True(video, "isPrivate") || True(video, "isDeleted") || True(video, "isAuthenticationRequired") || Text(video, "watchableUserTypeForPayment") != "all" || Text(payment, "billingType") != "free" || True(payment, "isPpv") || True(payment, "isAdmission") || True(payment, "isPremium") || True(payment, "isContinuationBenefit") || Drm(response) || Drm(Object(response, "media")))
                return page with { DrmRefused = Drm(response) || Drm(Object(response, "media")), Warnings = ["Niconico has not granted complete public playback for this video."] };
            if (await Music.MusicCatalog.RecordedAsync(_request, page, ct).ConfigureAwait(false)) return page;
            var domand = Object(Object(response, "media"), "domand");
            var videos = Formats(domand, "videos", "video"); var audios = Formats(domand, "audios", "audio");
            var outputs = videos.SelectMany(v => audios.Select(a => new[] { v, a })).ToArray();
            if (outputs.Length is 0 or > 64) throw new NotSupportedException("Niconico has no bounded complete clear DOMAND quality inventory.");
            var key = Text(domand, "accessRightKey"); var track = Text(client, "watchTrackId");
            if (key is not { Length: > 0 and <= 8192 } || key.Any(char.IsControl) || track is not { Length: > 0 and <= 256 } || !Match(track, @"\A[A-Za-z0-9_-]+\z").Success) throw new InvalidDataException("Niconico omitted its issued guest playback authorization.");
            using var grant = new HttpRequestMessage(HttpMethod.Post, new Uri("https://nvapi.nicovideo.jp/v1/watch/" + _id + "/access-rights/hls?actionTrackId=" + Uri.EscapeDataString(track))) { Content = new StringContent(JsonSerializer.Serialize(new { outputs }), Encoding.UTF8, "application/json") };
            grant.Headers.Add("X-Access-Right-Key", key); grant.Headers.Add("X-Frontend-Id", "6"); grant.Headers.Add("X-Frontend-Version", "0"); grant.Headers.Add("X-Request-With", "https://www.nicovideo.jp"); grant.Headers.Add("Origin", "https://www.nicovideo.jp"); grant.Headers.Referrer = new("https://www.nicovideo.jp/");
            using var result = await _service.SendAsync(grant, ct).ConfigureAwait(false); result.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await _service.ReadBoundedBytesAsync(result, 1024 * 1024, ct).ConfigureAwait(false)); var root = json.RootElement;
            if (Integer(Object(root, "meta"), "status") != 201) throw new InvalidDataException("Niconico did not grant the complete requested playback.");
            var data = Object(root, "data"); var url = Secure(Text(data, "contentUrl"), "delivery.domand.nicovideo.jp");
            if (!url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal)) throw new InvalidDataException("Niconico returned another playback protocol.");
            var expires = Date(Text(data, "expireTime")); var created = Date(Text(data, "createTime"));
            if (expires is null || created is null || expires <= DateTimeOffset.UtcNow.AddSeconds(5) || expires <= created || expires > created.Value.AddDays(2)) throw new InvalidDataException("Niconico returned an expired or invalid playback grant.");
            var jar = new CookieContainer();
            if (result.Headers.TryGetValues("Set-Cookie", out var cookies)) foreach (var value in cookies) { if (value.Length > 16384) throw new InvalidDataException("Niconico returned an oversized playback cookie."); jar.SetCookies(grant.RequestUri!, value); }
            var bid = jar.GetCookies(new Uri("https://delivery.domand.nicovideo.jp/")).Cast<Cookie>().Where(cookie => cookie.Name == "domand_bid" && cookie.Secure && cookie.Path == "/" && !cookie.Expired).ToArray();
            if (bid.Length != 1 || bid[0].Value.Length is 0 or > 8192 || bid[0].Value.Any(c => c <= 32 || c >= 127 || c is ';' or ',')) throw new InvalidDataException("Niconico omitted its scoped guest delivery cookie.");
            lock (_lifetime) _cookie = "domand_bid=" + bid[0].Value;
            return page with { Media = [new() { Id = "domand-hls", Url = url, Kind = MediaKind.Hls, Extension = "mp4", IsCompleteMedia = true, HasVideo = true, HasAudio = true, Source = "niconico", ResourceExpires = expires, VerificationDurationSeconds = duration }] };
        }
        private static string[] Formats(JsonElement domand, string key, string role)
        {
            var rows = Rows(domand, key, 16); var ids = new HashSet<string>(StringComparer.Ordinal); var available = new List<string>();
            foreach (var row in rows)
            {
                var id = Text(row, "id"); if (id is null || id.Length > 128 || !Match(id, @"\A" + role + @"-[a-z0-9-]+\z").Success || !ids.Add(id)) throw new InvalidDataException("Niconico returned ambiguous quality identities.");
                if (True(row, "isAvailable")) available.Add(id);
            }
            return available.ToArray();
        }
        private static bool Delivery(Uri u) => u.Scheme == "https" && u.IsDefaultPort && u.UserInfo.Length == 0 && u.Fragment.Length == 0 && u.Host == "delivery.domand.nicovideo.jp";
        private async Task<MediaDocument> ReadManifest(Uri url, CancellationToken ct)
        {
            if (!Delivery(url)) throw new InvalidDataException("Niconico renewed a manifest outside its delivery origin.");
            string cookie; lock (_lifetime) cookie = _cookie ?? throw new InvalidDataException("The guest delivery grant has ended.");
            using var delivery = _service.WithHeaders(new Dictionary<string, string> { ["Cookie"] = cookie });
            return await delivery.ReadPlaybackManifestAsync(url, Delivery, Watch.AbsoluteUri, ct).ConfigureAwait(false);
        }
        public void ApplyRequestHeaders(HttpRequestMessage request)
        {
            request.Headers.Remove("Cookie"); if (request.RequestUri is { } url && Delivery(url)) lock (_lifetime) { if (_cookie is null) throw new InvalidDataException("The guest delivery grant has ended."); request.Headers.Add("Cookie", _cookie); }
        }
        public bool AppliesTo(Uri url) => _lease!.AppliesTo(url);
        public Task<PlaybackLease> AuthorizeAsync(Uri url, CancellationToken ct) => _lease!.AuthorizeAsync(url, ct);
        public Task<PlaybackLease> RenewAsync(Uri url, long generation, CancellationToken ct) => _lease!.RenewAsync(url, generation, ct);
        public Uri Canonicalize(Uri url) => _lease!.Canonicalize(url);
        public string? RecoveryIdentity(Uri url) => _lease!.RecoveryIdentity(url);
        public bool TryRedirect(Uri source, Uri destination) => _lease!.TryRedirect(source, destination);
        public Task ObserveManifestAsync(MediaDocument document, CancellationToken ct) => _lease!.ObserveManifestAsync(document, ct);
        public void Dispose() { _lifetime.Dispose(() => { _lease?.Dispose(); _initial = null; lock (_lifetime) _cookie = null; }); _service.Dispose(); }
    }
}
