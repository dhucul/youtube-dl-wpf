using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Streamable;

public sealed class StreamableProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("streamable", "Streamable public videos and embeds", 200);
    public bool CanHandle(Uri url)
    {
        try { _ = Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("streamable", Parse(request.Url));
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = Parse(request.Url);
        var endpoint = new Uri("https://api.streamable.com/videos/" + id);
        var document = await http.GetDocumentAsync(endpoint, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url.Scheme != "https" || document.Url.Host != endpoint.Host || Parse(document.Url) != id)
            throw new InvalidDataException("Streamable redirected metadata to another video or service.");
        using var json = JsonDocument.Parse(document.Text);
        var root = json.RootElement;
        if (Integer(root, "status") != 2) throw new NotSupportedException("Streamable has not made this video available for playback.");
        var canonical = Resource(Text(root, "url"), allowBareHost: true);
        if (Parse(canonical) != id || Text(root, "shortcode") is { } declared && declared != id)
            throw new InvalidDataException("Streamable returned another video identity.");
        if (True(root, "private") || True(root, "password_protected")) throw new NotSupportedException("Private or password-protected Streamable videos require additional native account support.");
        var files = Object(root, "files");
        if (files.ValueKind != JsonValueKind.Object || files.EnumerateObject().Count() > 64) throw new InvalidDataException("Streamable did not return a bounded file list.");
        var media = new List<MediaCandidate>(); var durations = new List<double>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>(); var protectedMedia = HasProtection(root); var protectedSeen = protectedMedia;
        foreach (var file in files.EnumerateObject())
        {
            if (file.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Streamable returned an invalid file descriptor.");
            if (Text(file.Value, "url") is not { } raw || string.IsNullOrWhiteSpace(raw)) continue;
            if (protectedMedia || HasProtection(file.Value)) { protectedSeen = true; warnings.Add("A protected Streamable rendition is unavailable to the native engine."); continue; }
            if (Integer(file.Value, "status") != 2) { warnings.Add("A Streamable rendition is not ready for playback."); continue; }
            var url = Resource(raw); var kind = PageMediaExtractor.Classify(url, null);
            var extension = PageMediaExtractor.ExtensionFrom(url, null);
            if (kind == MediaKind.Unknown || kind == MediaKind.Progressive && extension is not ("mp4" or "webm"))
            { warnings.Add("A Streamable rendition uses an unsupported media type."); continue; }
            if (!seen.Add(url.AbsoluteUri)) continue;
            if (!Regex.IsMatch(file.Name, @"\A[A-Za-z0-9_-]{1,64}\z")) throw new InvalidDataException("Streamable returned an invalid format identifier.");
            var channels = Integer(root, "audio_channels");
            media.Add(new() { Id = file.Name, Url = url, Kind = kind, Extension = extension, HasVideo = true, HasAudio = channels is null ? null : channels > 0,
                Width = PositiveInteger(file.Value, "width"), Height = PositiveInteger(file.Value, "height"), Bandwidth = PositiveNumber(file.Value, "bitrate"),
                FramesPerSecond = PositiveNumber(file.Value, "framerate"), FileSize = Long(file.Value, "size") is > 0 and var size ? size : null, Source = "streamable" });
            if (PositiveNumber(file.Value, "duration") is { } duration) durations.Add(duration);
        }
        if (media.Count == 0) warnings.Add("Streamable did not expose any ready, supported media URLs.");
        return new() { Id = id, Extractor = "streamable", Title = Text(root, "title") ?? id, DurationSeconds = durations.Count > 0 ? durations.Max() : null,
            ThumbnailUrl = Text(root, "thumbnail_url") is { Length: > 0 } thumbnail ? Resource(thumbnail).AbsoluteUri : null, Media = media,
            SourcePageUrl = new Uri("https://streamable.com/" + id), PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = "https://streamable.com/" },
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(), DrmRefused = protectedSeen && media.Count == 0,
            RefusalReason = protectedSeen && media.Count == 0 ? "Streamable requires an unsupported protected-media workflow." : null };
    }
    private static int? PositiveInteger(JsonElement value, string name) => Integer(value, name) is > 0 and var number ? number : null;
    private static double? PositiveNumber(JsonElement value, string name) => Number(value, name) is > 0 and var number ? number : null;
    private static bool HasProtection(JsonElement value) => new[] { "drm", "key_systems" }.Any(name => value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false, JsonValueKind.Object => field.EnumerateObject().Any(), JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(field.GetString()), _ => true
    });
    private static string Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Host is not ("streamable.com" or "www.streamable.com" or "api.streamable.com"))
            throw new ArgumentException("Use a Streamable video or embed URL.");
        var pattern = url.Host == "api.streamable.com" ? @"\A/videos/(?<id>[A-Za-z0-9]{1,32})/?\z" : @"\A/(?:[eo]/)?(?<id>[A-Za-z0-9]{1,32})/?\z";
        var match = Regex.Match(url.AbsolutePath, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || new[] { "blog", "login", "signup", "privacy", "terms", "videos", "upload", "features", "pricing" }.Contains(match.Groups["id"].Value, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Native support for this Streamable page type remains pending.");
        return match.Groups["id"].Value;
    }
    private static Uri Resource(string? value, bool allowBareHost = false)
    {
        if (value?.StartsWith("//", StringComparison.Ordinal) == true) value = "https:" + value;
        else if (allowBareHost && (value?.StartsWith("streamable.com/", StringComparison.OrdinalIgnoreCase) == true || value?.StartsWith("www.streamable.com/", StringComparison.OrdinalIgnoreCase) == true)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !(url.Host == "streamable.com" || url.Host.EndsWith(".streamable.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Streamable returned an invalid media or page address.");
        return url;
    }
}
