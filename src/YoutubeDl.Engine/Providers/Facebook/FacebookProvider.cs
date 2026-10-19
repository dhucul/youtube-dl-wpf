using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Facebook;

internal sealed record FacebookAddress(string Id)
{
    internal Uri Page => new("https://www.facebook.com/reel/" + Id + "/");
    internal static bool Host(Uri url) => url.IsAbsoluteUri && url.Host is "www.facebook.com" or "facebook.com" or "m.facebook.com" or "web.facebook.com";
    internal static FacebookAddress Parse(Uri url)
    {
        if (!Host(url) || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a Facebook video or reel link.");
        if (url.AbsolutePath == "/plugins/video.php")
        {
            var href = Query(url, "href"); if (href is null || !Uri.TryCreate(href, UriKind.Absolute, out var nested) || nested.AbsolutePath == "/plugins/video.php") throw new ArgumentException("The Facebook player omitted its selected video link."); return Parse(nested);
        }
        var path = Match(url.AbsolutePath, @"\A/(?:reel/(?<id>[0-9]{5,24})|[A-Za-z0-9._-]+/videos/(?:[^/]{1,256}/)?(?<id>[0-9]{5,24}))/?\z");
        var id = path.Success ? path.Groups["id"].Value : url.AbsolutePath is "/watch/" or "/watch" or "/video.php" ? Query(url, "v") : null;
        return id is not null && Match(id, @"\A[0-9]{5,24}\z").Success ? new(id) : throw new NotSupportedException("Use a link to one Facebook video or reel.");
    }
    internal static string? Query(Uri url, string name)
    {
        var values = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(part => Uri.UnescapeDataString(part[0]) == name).ToArray();
        if (values.Length > 1) throw new ArgumentException("The Facebook link repeats its selected video parameter.");
        return values.Length == 1 && values[0].Length == 2 ? Uri.UnescapeDataString(values[0][1]) : null;
    }
}

public sealed class FacebookProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("facebook", "Facebook public videos and reels", 210);
    public bool CanHandle(Uri url) => FacebookAddress.Host(url) || url.IsAbsoluteUri && url.Host == "fb.watch";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => request.Url.Host == "fb.watch" ? null : new("facebook", FacebookAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = request.Url.Host == "fb.watch" ? await ResolveShortLink(request.Url, http, ct).ConfigureAwait(false) : FacebookAddress.Parse(request.Url);
        long? asset = null; double? duration = null;
        async Task<PageMetadata> Current(MediaHttp owner, CancellationToken token)
        {
            using var clean = owner.CreatePublicServiceSession();
            using var metadataClient = clean.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = "YoutubeDLModern/1.0" });
            var html = await metadataClient.GetStringAsync(address.Page, 5 * 1024 * 1024, token).ConfigureAwait(false); var metadata = PageMetadataReader.Read(html, address.Page);
            var page = new PageMetadata { Id = address.Id, Extractor = "facebook", ArchiveIdentity = new("facebook", address.Id), Title = metadata.Description ?? metadata.Title, Description = metadata.Description, SourcePageUrl = address.Page, AnonymousPlayback = true, PublicServicePlayback = true };
            if (await Music.MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var issuer = new Uri("https://www.facebook.com/plugins/video.php?href=" + Uri.EscapeDataString(address.Page.AbsoluteUri) + "&show_text=false");
            var embed = await clean.GetStringAsync(issuer, 4 * 1024 * 1024, token).ConfigureAwait(false); var configs = PlayerConfigs(embed, token);
            if (configs.Count == 0) return page with { Warnings = ["Facebook did not offer public playback for this video. It may be unavailable or require sign-in."] };
            if (configs.Count != 1) throw new InvalidDataException("Facebook returned more than one selected player.");
            var config = configs[0]; var videos = Rows(config, "videoData", 8);
            if (Text(config, "video_id") != address.Id || videos.Length != 1 || Text(videos[0], "video_id") != address.Id) throw new InvalidDataException("Facebook returned another selected video.");
            var video = videos[0]; var canonical = Text(video, "video_url"); if (canonical is null || FacebookAddress.Parse(new(canonical)).Id != address.Id) throw new InvalidDataException("Facebook changed the selected video's canonical link.");
            if (True(video, "is_live_stream") || True(video, "is_broadcast") || True(video, "is_hls") || Text(video, "stream_type") != "progressive") return page with { Warnings = ["This Facebook player needs an additional live or adaptive playback integration."] };
            if (Present(config, "videoLicenseUriMap") || Present(config, "graphApiVideoLicenseUri") || Drm(config) || Drm(video)) return page with { DrmRefused = true, Warnings = ["Facebook did not offer clear playback for this video."] };
            var formats = new List<MediaCandidate>(); var urls = new HashSet<string>(StringComparer.Ordinal); long? currentAsset = null; double? currentDuration = null;
            foreach (var quality in new[] { "hd", "sd" })
            {
                if (Text(video, quality + "_src") is not { Length: > 0 } raw) continue;
                var url = Resource(raw); if (!urls.Add(url.AbsoluteUri)) continue;
                if (FacebookAddress.Query(url, "efg") is { Length: > 0 and <= 8192 } facts)
                {
                    JsonElement offered;
                    try { using var json = JsonDocument.Parse(Convert.FromBase64String(facts), new() { MaxDepth = 8 }); offered = json.RootElement.Clone(); }
                    catch (Exception ex) when (ex is FormatException or JsonException) { throw new InvalidDataException("Facebook returned invalid rendition facts.", ex); }
                    var key = Long(offered, "xpv_asset_id"); var seconds = Number(offered, "duration_s");
                    if (key is not > 0 || seconds is not (> 0 and <= 604800) || currentAsset is not null && currentAsset != key || currentDuration is not null && currentDuration != seconds) throw new InvalidDataException("Facebook returned inconsistent full rendition identities.");
                    currentAsset = key; currentDuration = seconds;
                }
                var bitrate = FacebookAddress.Query(url, "bitrate");
                formats.Add(new() { Id = quality, Label = quality.ToUpperInvariant(), Url = url, Kind = MediaKind.Progressive, Extension = "mp4", MimeType = "video/mp4", HasVideo = true, HasAudio = true, IsCompleteMedia = true, Bandwidth = long.TryParse(bitrate, out var rate) && rate > 0 ? rate : null, ProviderQualityOrder = quality == "hd" ? 2 : 1, VerificationDemuxer = "mov", VerificationDurationSeconds = currentDuration, Source = "facebook" });
            }
            if (asset is not null && asset != currentAsset || duration is not null && duration != currentDuration) throw new InvalidDataException("Facebook changed the selected media asset during playback renewal."); asset ??= currentAsset; duration ??= currentDuration;
            return page with { DurationSeconds = currentDuration, Media = formats, Warnings = formats.Count == 0 ? ["Facebook offered no supported public video file."] : [] };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request with { Url = address.Page }, Info.Id, Current, reorderedFormats: false, publicSession: true)).ReadAsync(ct).ConfigureAwait(false);
    }
    internal static List<JsonElement> PlayerConfigs(string html, CancellationToken ct)
    {
        var result = new List<JsonElement>(); var blocks = 0;
        foreach (Match script in Regex.Matches(html, @"<script\b[^>]{0,8192}>(?<body>[\s\S]*?)</script\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var body = script.Groups["body"].Value; if (!body.Contains("new ServerJS", StringComparison.Ordinal)) continue;
            foreach (Match call in Regex.Matches(body, @"\b[A-Za-z_$][A-Za-z0-9_$]*\.handle\s*\(\s*(?<object>\{)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
            {
                ct.ThrowIfCancellationRequested(); if (++blocks > 16) throw new InvalidDataException("Facebook exceeded its public player bootstrap limit.");
                var literal = JavaScriptDataLiteral.ObjectArgument(body, call.Groups["object"].Index, out var end, ct); if (!body.AsSpan(end).TrimStart().StartsWith(")", StringComparison.Ordinal)) throw new InvalidDataException("Facebook's selected player data is computed rather than literal.");
                using var document = JsonDocument.Parse(literal.ToJsonString());
                if (!document.RootElement.TryGetProperty("instances", out _)) continue;
                foreach (var instance in Rows(document.RootElement, "instances", 256))
                {
                    if (instance.ValueKind != JsonValueKind.Array || instance.GetArrayLength() < 3) continue;
                    if (instance[1].ValueKind != JsonValueKind.Array) continue;
                    if (!Rows(instance[1], maximum: 64).Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "VideoConfig")) continue;
                    var args = Rows(instance[2], maximum: 8); if (args.Length != 1 || args[0].ValueKind != JsonValueKind.Object) throw new InvalidDataException("Facebook omitted its selected player configuration."); result.Add(args[0]);
                }
            }
        }
        return result;
    }
    private static async Task<FacebookAddress> ResolveShortLink(Uri url, MediaHttp http, CancellationToken ct)
    {
        if (url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || !Match(url.AbsolutePath, @"\A/[A-Za-z0-9_-]{3,128}/?\z").Success) throw new ArgumentException("Use a valid Facebook short video link.");
        using var clean = http.CreatePublicServiceSession();
        for (var hop = 0; hop < 5; hop++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url); using var response = await clean.SendAsync(message, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (>= 300 and < 400)) { response.EnsureSuccessStatusCode(); throw new NotSupportedException("The Facebook short link did not identify a video."); }
            var location = response.Headers.Location; var next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(url, location);
            if (next is null || next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo.Length != 0 || next.Fragment.Length != 0) throw new InvalidDataException("Facebook redirected outside its video link scope.");
            if (FacebookAddress.Host(next)) return FacebookAddress.Parse(next);
            if (next.Host != "fb.watch") throw new InvalidDataException("Facebook redirected outside its video link scope."); url = next;
        }
        throw new InvalidDataException("Facebook repeated its short-link redirects.");
    }
    internal static bool MediaHost(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Host.StartsWith("video.", StringComparison.Ordinal) && url.Host.EndsWith(".fbcdn.net", StringComparison.Ordinal);
    private static Uri Resource(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MediaHost(url) && url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? url : throw new InvalidDataException("Facebook returned an undeclared video-file origin.");
}
