using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Odysee;

public sealed partial class OdyseeProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("lbry", "Odysee/LBRY public streams, channels and collections", 200);
    public bool CanHandle(Uri url) => OdyseeAddress.IsHost(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = OdyseeAddress.Parse(request.Url);
        var agent = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent;
        // Odysee's get gateway can initiate purchases in an authenticated wallet
        // context. This public provider must never forward that context.
        using var anonymous = http.CreateAnonymousSession();
        using var session = anonymous.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = agent, ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? address.Web.AbsoluteUri : request.Referer });
        JsonElement claim;
        if (address.CollectionId is { } collectionId)
        {
            var result = await OdyseeRpc.CallAsync("claim_search", new { claim_ids = new[] { collectionId }, page = 1, page_size = 1 }, session, ct).ConfigureAwait(false);
            OdyseeRpc.CheckSearch(result); var items = Array(result, "items");
            if (items.Length != 1 || Text(items[0], "claim_id") != collectionId || Text(items[0], "value_type") != "collection") throw new NotSupportedException("The public Odysee collection was not found.");
            claim = items[0];
        }
        else
        {
            var result = await OdyseeRpc.CallAsync("resolve", new { urls = new[] { address.Rpc } }, session, ct).ConfigureAwait(false);
            if (!result.TryGetProperty(address.Rpc, out claim) || claim.ValueKind != JsonValueKind.Object || claim.TryGetProperty("error", out var claimError) && claimError.ValueKind != JsonValueKind.Null)
                throw new NotSupportedException("Odysee could not resolve this public claim.");
        }
        ValidateClaim(claim, address.Stream ?? address.Channel);
        if (address.Stream is not null && address.Channel is { } channel)
        {
            var signing = Object(claim, "signing_channel"); ValidateClaim(signing, channel);
            if (claim.TryGetProperty("is_channel_signature_valid", out var signature) && signature.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("The Odysee stream is not signed by the requested channel.");
        }
        return Text(claim, "value_type") switch
        {
            "channel" when address.Stream is null && address.CollectionId is null => await ChannelAsync(claim, request, session, ct).ConfigureAwait(false),
            "collection" => await CollectionAsync(claim, request, session, ct).ConfigureAwait(false),
            "stream" => await StreamAsync(claim, session, agent, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException("This Odysee claim type requires additional native support.")
        };
    }
    private static async Task<PageMetadata> StreamAsync(JsonElement claim, MediaHttp http, string agent, CancellationToken ct)
    {
        var unavailable = Unavailable(claim);
        if (unavailable is not null) throw new NotSupportedException(unavailable);
        var id = Text(claim, "claim_id")!; var name = Text(claim, "name")!; var value = Object(claim, "value"); var source = Object(value, "source");
        var result = await OdyseeRpc.CallAsync("get", new { uri = "lbry://" + name + "#" + id, save_file = false }, http, ct).ConfigureAwait(false);
        if (Text(result, "claim_id") is { } returnedId && returnedId != id) throw new InvalidDataException("Odysee returned another playback identity.");
        var url = Playback(Text(result, "streaming_url"), id, name, Text(source, "sd_hash"));
        var kind = PageMediaExtractor.Classify(url);
        var extension = PageMediaExtractor.ExtensionFrom(url);
        if (kind is not (MediaKind.Hls or MediaKind.Dash))
        {
            // The CDN route may end in .mp4 for an original audio or other media
            // file. Its container comes from the resolved source, not that suffix.
            var declared = PageMediaExtractor.ExtensionFrom(new("https://odysee.com/"), Text(source, "media_type"));
            if (declared is null && Text(source, "name") is { Length: > 0 and < 4096 } sourceName)
                declared = PageMediaExtractor.ExtensionFrom(new("https://odysee.com/" + Uri.EscapeDataString(Path.GetFileName(sourceName))));
            if (declared is not null) { extension = declared; kind = declared switch { "m3u8" => MediaKind.Hls, "mpd" => MediaKind.Dash, _ => MediaKind.Progressive }; }
        }
        if (kind == MediaKind.Unknown || extension is null)
            throw new NotSupportedException("The Odysee playback response uses an unsupported resource format.");
        var audio = Text(value, "stream_type") == "audio";
        var video = Object(value, "video");
        var page = Metadata(claim);
        return page with { AnonymousPlayback = true, Media = [new() { Id = "stream", Url = url, Kind = kind, Extension = extension, Source = "lbry", HasVideo = !audio,
                HasAudio = audio ? true : null, IsCompleteMedia = kind == MediaKind.Progressive, MimeType = kind == MediaKind.Progressive ? Text(source, "media_type") : null,
                Width = kind == MediaKind.Progressive && !audio ? Positive(video, "width") : null, Height = kind == MediaKind.Progressive && !audio ? Positive(video, "height") : null,
                FileSize = kind == MediaKind.Progressive ? Long(source, "size") : null }],
            PlaybackHeaders = new Dictionary<string, string> { ["User-Agent"] = agent, ["Referer"] = page.SourcePageUrl!.AbsoluteUri } };
    }
    private static Uri Playback(string? value, string id, string name, string? hash)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.Host != "player.odycdn.com")
            throw new InvalidDataException("Odysee returned an unsupported playback origin.");
        var parts = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray(); string? suffix = null;
        if (parts.Length == 4 && parts[0] == "v6" && parts[1] == "streams" && parts[2] == id) suffix = parts[3];
        else if (parts.Length is 6 or 7 && parts[0] == "api" && parts[1] is "v3" or "v4" && parts[2] == "streams" && parts[3] == "free" && parts[4] == name && parts[5] == id)
        { if (parts.Length == 7) suffix = parts[6]; }
        else throw new InvalidDataException("The Odysee playback URL does not match the resolved claim.");
        if (suffix is not null)
        {
            var prefix = Path.GetFileNameWithoutExtension(suffix);
            if (prefix.Length < 6 || hash is null || !hash.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Odysee playback resource does not match the resolved stream descriptor.");
        }
        return url;
    }
    private static void ValidateClaim(JsonElement claim, ClaimSelector? expected = null)
    {
        var id = Text(claim, "claim_id"); var name = Text(claim, "name");
        if (!OdyseeAddress.IsId(id) || name is null) throw new InvalidDataException("Odysee returned an invalid claim identity.");
        var parsed = OdyseeAddress.Part(Uri.EscapeDataString(name), native: true);
        if (parsed.Name != name || parsed.Prefix is not null || name.StartsWith('@') != (Text(claim, "value_type") == "channel"))
            throw new InvalidDataException("Odysee returned an inconsistent claim name or type.");
        if (expected is not null && (!OdyseeAddress.SameName(expected.Name, name) || expected.Prefix is { } prefix && !id!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Odysee resolved a different claim than requested.");
    }
    private static string? Unavailable(JsonElement claim)
    {
        if (Text(claim, "value_type") != "stream") return "This collection entry is not an audio/video stream.";
        if (Object(claim, "signing_channel").ValueKind != JsonValueKind.Undefined && claim.TryGetProperty("is_channel_signature_valid", out var signature) && signature.ValueKind != JsonValueKind.True)
            return "The Odysee publisher signature could not be validated.";
        var value = Object(claim, "value"); var type = Text(value, "stream_type");
        if (type is not ("video" or "audio")) return "This Odysee claim is not a supported audio/video stream.";
        if (value.TryGetProperty("fee", out var fee) && fee.ValueKind != JsonValueKind.Null)
        {
            if (fee.ValueKind != JsonValueKind.Object || !fee.TryGetProperty("amount", out var amount)) return "Odysee did not establish that this stream is free.";
            var amountText = amount.ValueKind == JsonValueKind.String ? amount.GetString() : amount.ValueKind == JsonValueKind.Number ? amount.GetRawText() : null;
            if (amountText is null || amountText.Length > 64 || !Regex.IsMatch(amountText, @"\A\+?0+(?:\.0+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                return "Paid Odysee content requires an additional purchase/access workflow.";
        }
        foreach (var tag in Array(value, "tags").Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!))
            if (tag.StartsWith("purchase:", StringComparison.Ordinal) || tag.StartsWith("rental:", StringComparison.Ordinal) || tag is "c:purchase" or "c:rental" or "c:members-only" or "c:unlisted" ||
                tag is "c:scheduled:hide" or "c:scheduled:show" && Number(value, "release_time") > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return "This Odysee stream requires a restricted, scheduled or account-specific access workflow.";
        var hash = Text(Object(value, "source"), "sd_hash");
        if (hash is null || hash.Length != 96 || !hash.All(char.IsAsciiHexDigit)) return "This Odysee claim has no supported published stream; live or alternate storage requires additional support.";
        return null;
    }
    private static PageMetadata Metadata(JsonElement claim)
    {
        var id = Text(claim, "claim_id")!; var value = Object(claim, "value"); var signing = Object(claim, "signing_channel");
        if (claim.TryGetProperty("is_channel_signature_valid", out var signature) && signature.ValueKind != JsonValueKind.True) signing = default;
        var thumbnail = Text(Object(value, "thumbnail"), "url");
        if (!Uri.TryCreate(thumbnail, UriKind.Absolute, out var image) || image.Scheme is not ("http" or "https") || image.UserInfo.Length != 0 || image.Fragment.Length != 0) thumbnail = null;
        return new() { Id = id, Extractor = "lbry", Title = Text(value, "title") ?? Text(claim, "name")!, Description = Text(value, "description"), ThumbnailUrl = thumbnail,
            Uploader = Text(Object(signing, "value"), "title") ?? Text(signing, "name"), DurationSeconds = Number(Object(value, "video"), "duration") ?? Number(Object(value, "audio"), "duration"),
            SourcePageUrl = OdyseeAddress.ClaimUrl(Text(claim, "name")!, id) };
    }
    private static int? Positive(JsonElement value, string key) => Integer(value, key) is > 0 and var number ? number : null;
}
