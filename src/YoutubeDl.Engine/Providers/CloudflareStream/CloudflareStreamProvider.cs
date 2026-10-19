using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.CloudflareStream;

public sealed class CloudflareStreamProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("cloudflarestream", "Cloudflare Stream players and supplied manifests", 200);
    public bool CanHandle(Uri url) => CloudflareStreamAddress.IsHost(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = CloudflareStreamAddress.Parse(request.Url);
        if (address.ManifestKind is { } kind)
            return new() { Id = address.Signed ? "token-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(address.Key))).ToLowerInvariant() : address.ClaimedId,
                Extractor = Info.Id, Title = address.FallbackId, SourcePageUrl = request.Url,
                Media = [new() { Id = kind == MediaKind.Hls ? "hls" : "dash", Url = request.Url, Kind = kind, Extension = kind == MediaKind.Hls ? "m3u8" : "mpd", Source = Info.Id }] };
        var document = await http.GetDocumentAsync(address.Url, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        var final = CloudflareStreamAddress.Parse(document.Url);
        if (final.Key != address.Key || final.ManifestKind is not null) throw new InvalidDataException("Cloudflare Stream redirected to a different media identity.");
        var matches = Regex.Matches(document.Text, @"<stream\b(?<attributes>[^>]{0,16384})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (matches.Count != 1) throw new NotSupportedException("Cloudflare Stream did not expose a supported player declaration.");
        var attributes = Attributes(matches[0].Groups["attributes"].Value);
        if (attributes.GetValueOrDefault("src") != address.Key) throw new InvalidDataException("The Cloudflare Stream player belongs to another video or token.");
        var prefix = attributes.GetValueOrDefault("customer-domain-prefix");
        var host = prefix is null ? final.Url.Host : prefix + ".cloudflarestream.com";
        if (!CloudflareStreamAddress.IsCustomer(host) || CloudflareStreamAddress.IsCustomer(final.Url.Host) && host != final.Url.Host)
            throw new InvalidDataException("The Cloudflare Stream player returned an inconsistent customer domain.");
        var lifecycleUrl = address.Asset(host, "lifecycle");
        var lifecycleDocument = await http.GetDocumentAsync(lifecycleUrl, 1024 * 1024, ct).ConfigureAwait(false);
        if (lifecycleDocument.Url != lifecycleUrl) throw new InvalidDataException("Cloudflare Stream redirected its lifecycle metadata.");
        using var lifecycleJson = JsonDocument.Parse(lifecycleDocument.Text);
        var lifecycle = lifecycleJson.RootElement; var id = Text(lifecycle, "videoUID");
        if (True(lifecycle, "isInput")) throw new NotSupportedException("Cloudflare live-input routing requires additional native support.");
        if (!CloudflareStreamAddress.IsId(id) || address.ClaimedId is not null && !id!.Equals(address.ClaimedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cloudflare Stream returned another video identity.");
        var live = True(lifecycle, "live");
        if (Text(lifecycle, "status") is not ("disconnected" or "ready" or "connected") || lifecycle.TryGetProperty("chunked", out var chunked) && chunked.ValueKind == JsonValueKind.False)
            throw new NotSupportedException("This Cloudflare Stream video is not in a supported playback state.");
        var metadataUrl = address.Asset(host, "metadata/playerEnhancementInfo.json");
        var metadataDocument = await http.GetDocumentAsync(metadataUrl, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (metadataDocument.Url != metadataUrl) throw new InvalidDataException("Cloudflare Stream redirected its player metadata.");
        using var metadataJson = JsonDocument.Parse(metadataDocument.StatusCode == HttpStatusCode.NoContent ? "{}" : metadataDocument.Text); var metadata = metadataJson.RootElement;
        if (metadata.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Cloudflare Stream player metadata is invalid.");
        if (True(metadata, "isPreview")) throw new NotSupportedException("Cloudflare Stream exposed a preview rather than the requested complete video.");
        if (Array(metadata, "tt").Length > 256 || Array(metadata, "audio").Length > 256) throw new InvalidDataException("Cloudflare Stream returned too many track descriptors.");
        var details = Object(metadata, "publicDetails");
        return new() { Id = id!.ToLowerInvariant(), Extractor = Info.Id, Title = Text(details, "title") is { Length: > 0 } title ? title : id!, Description = Text(details, "description"), IsLive = live,
            ThumbnailUrl = address.Asset(host, "thumbnails/thumbnail.jpg").AbsoluteUri, SourcePageUrl = document.Url,
            Media = live ? [] : [new() { Id = "hls", Url = address.Asset(host, "manifest/video.m3u8", request.Referer), Kind = MediaKind.Hls, Extension = "m3u8", Source = Info.Id },
                new() { Id = "dash", Url = address.Asset(host, "manifest/video.mpd", request.Referer), Kind = MediaKind.Dash, Extension = "mpd", Source = Info.Id }],
            Warnings = live ? ["Native Cloudflare live recording is not implemented yet."] : [],
            PlaybackHeaders = string.IsNullOrWhiteSpace(request.Referer) ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Referer"] = request.Referer } };
    }
    internal static Uri? ReadEmbed(string tag, IReadOnlyDictionary<string, string> attributes)
    {
        if (tag.StartsWith("<script", StringComparison.OrdinalIgnoreCase) && attributes.TryGetValue("src", out var script) && Uri.TryCreate(script, UriKind.Absolute, out var sdk) &&
            sdk.Host is "embed.cloudflarestream.com" or "embed.videodelivery.net" && sdk.Query.TrimStart('?').Split('&').Any(part => Uri.UnescapeDataString(part.Split('=')[0]) == "video"))
            return CloudflareStreamAddress.Parse(sdk).Url;
        if (!tag.StartsWith("<stream", StringComparison.OrdinalIgnoreCase) || !attributes.TryGetValue("src", out var key)) return null;
        if (key.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        var host = attributes.TryGetValue("customer-domain-prefix", out var prefix) ? prefix + ".cloudflarestream.com" : "iframe.videodelivery.net";
        if (host != "iframe.videodelivery.net" && !CloudflareStreamAddress.IsCustomer(host)) throw new InvalidDataException("The Stream embed has an invalid customer domain.");
        var url = new Uri("https://" + host + "/" + key + (host == "iframe.videodelivery.net" ? "" : "/iframe"));
        _ = CloudflareStreamAddress.Parse(url); return url;
    }
    private static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Regex.Matches(text, @"(?<name>[A-Za-z0-9_-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!result.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["double"].Success ? attribute.Groups["double"].Value : attribute.Groups["single"].Value)))
                throw new InvalidDataException("The Stream player repeats a declaration attribute.");
        return result;
    }
}
