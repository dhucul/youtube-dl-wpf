using System.Globalization;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Mixcloud;

public sealed partial class MixcloudProvider : IMediaProvider
{
    private const string Api = "https://api.mixcloud.com";
    public MediaProviderInfo Info { get; } = new("mixcloud", "Mixcloud public shows creator uploads playlists and widgets", 200);
    public bool CanHandle(Uri uri) => MixcloudAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = MixcloudAddress.Parse(request.Url); return address.Slug is not null && !address.Playlist ? new("mixcloud", address.Key.Trim('/')) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = MixcloudAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Slug is null || address.Playlist) return await CollectionAsync(address, request, anonymous, ct).ConfigureAwait(false);
        using var rest = await JsonAsync(new(Api + address.Key), anonymous, ct).ConfigureAwait(false); var metadata = rest.RootElement; ValidateShow(metadata, address);
        var query = "query NativeMixcloud($lookup:CloudcastLookup!){cloudcastLookup(lookup:$lookup){id slug name audioLength isExclusive isDisabledCopyright isExclusivePreviewOnly owner{username} streamInfo{url hlsUrl dashUrl}}}";
        var response = await anonymous.PostJsonDocumentAsync(new("https://www.mixcloud.com/graphql"), new { query, variables = new { lookup = new { username = address.User, slug = address.Slug } } }, 8 * 1024 * 1024, ct,
            new Dictionary<string, string> { ["User-Agent"] = "YoutubeDlModernNative/" + NativeEngine.Version }).ConfigureAwait(false);
        if (response.Url != new Uri("https://www.mixcloud.com/graphql")) throw new InvalidDataException("Mixcloud redirected its public playback metadata.");
        using var playback = Parse(response.Text); var root = playback.RootElement;
        if (Array(root, "errors").Length != 0) throw new NotSupportedException("Mixcloud did not provide an available public playback response.");
        var show = Object(Object(root, "data"), "cloudcastLookup"); if (show.ValueKind != JsonValueKind.Object || Text(show, "slug") != address.Slug || !string.Equals(Text(Object(show, "owner"), "username"), address.User, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mixcloud returned another requested playback identity.");
        if (Flag(show, "isExclusive") || Flag(show, "isDisabledCopyright") || Flag(show, "isExclusivePreviewOnly")) throw new NotSupportedException("This Mixcloud show requires additional exclusive preview or rights support.");
        var duration = Number(metadata, "audio_length"); if (duration is not > 0 || duration > 604800 || Number(show, "audioLength") != duration) throw new InvalidDataException("Mixcloud's complete playback durations disagree.");
        var stream = Object(show, "streamInfo"); var media = new List<MediaCandidate>();
        foreach (var field in new[] { "url", "hlsUrl", "dashUrl" })
        {
            if (Text(stream, field) is not { Length: > 0 } encoded) continue; var source = Decode(encoded); var kind = field == "url" ? MediaKind.Progressive : field == "hlsUrl" ? MediaKind.Hls : MediaKind.Dash;
            if (source.Host is not ("dl.mixcloud.stream" or "aod.mixcloud.stream") || !source.AbsolutePath.StartsWith("/secure/", StringComparison.Ordinal)) throw new NotSupportedException("This Mixcloud clear media origin needs additional native support.");
            if (field == "url" && !source.AbsolutePath.EndsWith(".m4a", StringComparison.Ordinal) || field == "hlsUrl" && !source.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal) || field == "dashUrl" && !source.AbsolutePath.EndsWith(".mpd", StringComparison.Ordinal)) throw new InvalidDataException("Mixcloud's offered clear media type conflicts with its source path.");
            media.Add(new() { Id = field == "url" ? "http-m4a" : field == "hlsUrl" ? "hls" : "dash", Url = source, Kind = kind, Source = "mixcloud", Extension = field == "hlsUrl" ? "ts" : "m4a", MimeType = field == "url" ? "audio/mp4" : field == "hlsUrl" ? "application/vnd.apple.mpegurl" : "application/dash+xml",
                HasAudio = true, HasVideo = false, AudioCodec = "aac", IsCompleteMedia = true, VerificationDemuxer = field == "hlsUrl" ? "mpegts" : "mov", VerificationDurationSeconds = duration });
        }
        if (media.Count == 0) throw new NotSupportedException("Mixcloud did not offer a complete clear stream; DRM-only playback needs separate support.");
        return new() { Id = address.Key.Trim('/'), Extractor = "mixcloud", ArchiveIdentity = new("mixcloud", address.Key.Trim('/')), Title = Text(metadata, "name") ?? address.Slug!, Description = Text(metadata, "description"), Uploader = Text(Object(metadata, "user"), "name") ?? address.User,
            DurationSeconds = duration, ThumbnailUrl = Picture(metadata), SourcePageUrl = address.Web, AnonymousPlayback = true,
            PlaybackHeaders = new Dictionary<string, string> { ["User-Agent"] = "YoutubeDlModernNative/" + NativeEngine.Version }, Media = media };
    }
    private static void ValidateShow(JsonElement metadata, MixcloudAddress address)
    {
        if (Text(metadata, "key") != address.Key || Text(metadata, "slug") != address.Slug || !string.Equals(Text(Object(metadata, "user"), "username"), address.User, StringComparison.OrdinalIgnoreCase) || MixcloudAddress.Parse(PublicUrl(Text(metadata, "url"))).Key != address.Key) throw new InvalidDataException("Mixcloud returned another requested show identity.");
    }
    private static bool Flag(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ValueKind == JsonValueKind.True : throw new InvalidDataException("Mixcloud omitted an explicit supported playback state.");
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Mixcloud returned an invalid public HTTPS resource.");
    private static Uri Decode(string encoded)
    {
        if (encoded.Length > 32768) throw new InvalidDataException("Mixcloud returned an excessive encoded stream URL.");
        byte[] bytes; try { bytes = Convert.FromBase64String(encoded); } catch (FormatException) { throw new InvalidDataException("Mixcloud returned a malformed encoded stream URL."); }
        var key = Encoding.ASCII.GetBytes("IFYOUWANTTHEARTISTSTOGETPAIDDONOTDOWNLOADFROMMIXCLOUD");
        for (var index = 0; index < bytes.Length; index++) bytes[index] ^= key[index % key.Length];
        string raw; try { raw = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { throw new InvalidDataException("Mixcloud returned invalid stream URL bytes."); }
        return PublicUrl(raw);
    }
    private static string? Picture(JsonElement root)
    { var picture = Object(root, "pictures"); var raw = Text(picture, "extra_large") ?? Text(picture, "large") ?? Text(picture, "medium"); if (raw is null) return null; var uri = PublicUrl(raw); if (uri.Host is not ("thumbnailer.mixcloud.com" or "images.mixcloud.com")) throw new NotSupportedException("Mixcloud returned an unsupported artwork origin."); return uri.AbsoluteUri; }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    { var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Mixcloud redirected its public resource identity."); return Parse(response.Text); }
    private static JsonDocument Parse(string raw)
    {
        JsonDocument document; try { document = JsonDocument.Parse(raw, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Mixcloud returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Mixcloud returned excessive metadata fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Mixcloud returned repeated metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
