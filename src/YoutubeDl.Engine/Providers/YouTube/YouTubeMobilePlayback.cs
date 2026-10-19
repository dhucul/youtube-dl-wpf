using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols.Sabr;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeMobilePlayback
{
    // Observed in Apple's public YouTube app listing on 2026-10-08; no external player or extractor is invoked.
    internal const string ClientVersion = "21.40.5";
    internal const string UserAgent = "com.google.ios.youtube/21.40.5 (iPhone16,2; U; CPU iOS 18_1_0 like Mac OS X;)";
    public static async Task<PageMetadata> ReadAsync(string videoId, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var agent = string.IsNullOrWhiteSpace(request.UserAgent) ? UserAgent : request.UserAgent;
        var json = await http.PostJsonAsync(new Uri("https://www.youtube.com/youtubei/v1/player?prettyPrint=false"),
            new { videoId, context = new { client = new { clientName = "IOS", clientVersion = ClientVersion, deviceModel = "iPhone16,2", osName = "iPhone", osVersion = "18.1.0.22B83", hl = "en" } } },
            2 * 1024 * 1024, new Dictionary<string, string> { ["X-YouTube-Client-Name"] = "5", ["X-YouTube-Client-Version"] = ClientVersion, ["User-Agent"] = agent }, ct).ConfigureAwait(false);
        // Validate playback permission and identity before interpreting transport configuration.
        var page = YouTubePlayerParser.ParseResponse(json, videoId);
        if (page.IsLive) throw new NotSupportedException("Native YouTube live SABR recording remains pending.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var endpoint = Text(Object(root, "streamingData"), "serverAbrStreamingUrl");
        if (endpoint is null || page.Media.Count > 0) return page;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !url.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("YouTube returned an invalid SABR address.");
        if (YouTubeQuery.Parse(url.Query).GetValueOrDefault("n") is { Length: > 0 }) throw new NotSupportedException("This mobile SABR endpoint requires an additional player transformation.");
        var config = Text(Object(Object(Object(root, "playerConfig"), "mediaCommonConfig"), "mediaUstreamerRequestConfig"), "videoPlaybackUstreamerConfig");
        if (string.IsNullOrWhiteSpace(config) || config.Length > 65536) throw new InvalidDataException("YouTube did not return a bounded SABR configuration.");
        byte[] bytes;
        try { var normalized = config.Replace('-', '+').Replace('_', '/'); bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')); }
        catch (FormatException ex) { throw new InvalidDataException("The SABR configuration is invalid.", ex); }
        if (bytes.Length == 0) throw new InvalidDataException("The SABR configuration is empty.");
        // The iOS inventory also advertises WebM objects which its SABR endpoint
        // rejects with reload/no-track-selected responses. Keep those unavailable
        // in this session so best/worst do not choose an undeliverable rendition.
        // A browser session and genuinely exposed direct URLs retain WebM support.
        return YouTubePlayerParser.ParseResponse(json, videoId, ct: ct, sabrSession: new(videoId, url, 5, ClientVersion, bytes, agent) { SupportsWebM = false });
    }
}
