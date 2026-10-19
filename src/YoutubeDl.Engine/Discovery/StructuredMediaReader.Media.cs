using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace YoutubeDl.Engine.Discovery;

internal sealed partial class StructuredMediaReader
{
    private PageMetadata ReadMedia(Node node)
    {
        var type = MediaType(node)!; var value = node.Value; var media = new List<MediaCandidate>(); var embeds = new List<Uri>(); var warnings = new List<string>();
        var sources = new List<Node>();
        foreach (var name in new[] { "encoding", "associatedMedia" })
            if (value.TryGetProperty(name, out var encodings))
                foreach (var encoding in Values(encodings))
                    if (Resolve(encoding, node) is { } source) sources.Add(source);
        sources.Add(node); // Encoding-specific facts take precedence for the same resource.
        if (sources.Count > 256) throw new InvalidDataException("The structured media object contains too many encodings.");
        var restricted = Restricted(value); var live = True(value, "isLiveBroadcast") || value.TryGetProperty("publication", out var publication) && Values(publication).Any(item => Resolve(item, node) is { } broadcast && True(broadcast.Value, "isLiveBroadcast"));
        var clipped = new[] { "startTime", "endTime", "startOffset", "endOffset" }.Any(name => value.TryGetProperty(name, out var range) && range.ValueKind != JsonValueKind.Null);
        if (restricted) warnings.Add("This structured media declaration requires an account or subscription workflow that has not been verified.");
        if (live) warnings.Add("This structured media declaration describes a live broadcast that requires native recording support.");
        if (clipped) warnings.Add("This structured media object declares a clip range that requires additional native support.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!restricted && !live && !clipped)
        {
            foreach (var source in sources)
            {
                if (Restricted(source.Value)) { warnings.Add("A restricted structured media encoding is unavailable."); continue; }
                var mime = Mime(Text(source.Value, "encodingFormat") ?? Text(source.Value, "fileFormat"), type);
                if (source.Value.TryGetProperty("contentUrl", out var content))
                    foreach (var raw in Values(content))
                    {
                        var url = Url(raw.ValueKind == JsonValueKind.String ? raw.GetString() : Text(raw, "@id"));
                        if (url is null || seen.Contains(url.AbsoluteUri)) continue;
                        if (url.Fragment.Length > 0) { warnings.Add("A structured content URL uses a media fragment that requires additional native support."); continue; }
                        var kind = PageMediaExtractor.Classify(url, mime); var extension = PageMediaExtractor.ExtensionFrom(url, mime);
                        if (kind == MediaKind.Unknown || extension is null) { warnings.Add("A structured content URL does not identify a supported media format."); continue; }
                        seen.Add(url.AbsoluteUri);
                        var audio = type == "audio" || mime?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
                        var width = Dimension(source.Value, "width"); var height = Dimension(source.Value, "height");
                        if ((width is null || height is null) && Text(source.Value, "videoFrameSize") is { } frame)
                        {
                            var match = Regex.Match(frame, @"\A(?<w>[0-9]{1,5})\s*[xX]\s*(?<h>[0-9]{1,5})\z", RegexOptions.CultureInvariant);
                            if (match.Success) { width ??= int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture); height ??= int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture); }
                        }
                        media.Add(new() { Id = "jsonld-" + (media.Count + 1), Url = url, Kind = kind, Extension = extension, MimeType = mime, Source = "json-ld",
                            HasVideo = !audio, HasAudio = audio ? true : null, IsCompleteMedia = kind == MediaKind.Progressive,
                            Width = !audio && width is > 0 ? width : null, Height = !audio && height is > 0 ? height : null, FileSize = ByteSize(Text(source.Value, "contentSize")) });
                    }
            }
            if (value.TryGetProperty("embedUrl", out var embed))
                foreach (var raw in Values(embed))
                    if (Url(raw.ValueKind == JsonValueKind.String ? raw.GetString() : Text(raw, "@id")) is { } url && !SamePage(url, _page)) embeds.Add(url);
        }
        var identity = Text(value, "@id") is { } identifier ? _page.GetLeftPart(UriPartial.Authority) + "\n" + Key(identifier, node.Block) : _page.AbsoluteUri + "\n" +
            (media.FirstOrDefault()?.Url.AbsoluteUri ?? embeds.FirstOrDefault()?.AbsoluteUri ?? value.GetRawText());
        var images = new List<ThumbnailCandidate>();
        foreach (var property in new[] { "thumbnailUrl", "thumbnail" })
            if (value.TryGetProperty(property, out var thumbnails))
                foreach (var item in Values(thumbnails))
                {
                    var image = item.ValueKind == JsonValueKind.String ? item.GetString() : Text(item, "contentUrl") ?? Text(item, "url");
                    if (Url(image) is { } url && images.All(prior => prior.Url != url)) images.Add(new() { Id = "jsonld-" + (images.Count + 1), Url = url });
                    if (images.Count > 256) throw new InvalidDataException("The structured media object has too many thumbnails.");
                }
        var uploader = new List<string>();
        foreach (var property in new[] { "author", "creator", "publisher" })
        {
            if (!value.TryGetProperty(property, out var authors)) continue;
            foreach (var author in Values(authors))
            {
                var name = Resolve(author, node) is { } linked ? Text(linked.Value, "name") : author.ValueKind == JsonValueKind.String &&
                    !author.GetString()!.StartsWith('#') && !author.GetString()!.Contains(":", StringComparison.Ordinal) ? author.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name)) uploader.Add(name);
            }
            if (uploader.Count > 0) break;
        }
        var allowPagePlayer = !restricted && !live && !clipped && media.Count == 0 && embeds.Count == 0 && warnings.Count == 0;
        if (allowPagePlayer) warnings.Add("The structured media object did not expose a supported content URL or player.");
        var subtitles = new List<SubtitleTrack>();
        if (!restricted && !live && !clipped && value.TryGetProperty("caption", out var captions))
            foreach (var reference in Values(captions))
            {
                if (Resolve(reference, node) is not { } caption || Url(Text(caption.Value, "contentUrl")) is not { } url) continue;
                var extension = Text(caption.Value, "encodingFormat")?.ToLowerInvariant() switch
                { "text/vtt" or "vtt" => "vtt", "application/x-subrip" or "srt" => "srt", "application/ttml+xml" or "ttml" => "ttml", _ => Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant() };
                if (extension is not ("vtt" or "srt" or "ttml" or "dfxp")) continue;
                var language = Text(caption.Value, "inLanguage") ?? "und";
                if (!Regex.IsMatch(language, @"\A[A-Za-z0-9-]{1,35}\z", RegexOptions.CultureInvariant)) language = "und";
                if (subtitles.All(track => track.Url != url || track.Language != language)) subtitles.Add(new() { Url = url, Extension = extension, Language = language });
                if (subtitles.Count > 256) throw new InvalidDataException("The structured media object exposes too many caption tracks.");
            }
        return new() { Id = Hash(identity), Extractor = "generic:jsonld", Title = Text(value, "name") ?? Text(value, "headline") ?? "",
            Description = Text(value, "description"), Uploader = uploader.Count == 0 ? null : string.Join("; ", uploader.Distinct()),
            DurationSeconds = Duration(Text(value, "duration")), ThumbnailUrl = images.FirstOrDefault()?.Url.AbsoluteUri, Thumbnails = images,
            UploadDate = MetadataFacts.Date(Text(value, "uploadDate") ?? Text(value, "datePublished")),
            ViewCount = InteractionCount(value, "WatchAction"),
            MetadataFields = new Dictionary<string, object?> { ["like_count"] = InteractionCount(value, "LikeAction"), ["comment_count"] = InteractionCount(value, "CommentAction") },
            Media = media, Subtitles = subtitles, EmbeddedPages = embeds.Distinct().ToArray(), Warnings = warnings.Distinct().ToArray(), IsLive = live, SourcePageUrl = _page,
            AllowPagePlayer = allowPagePlayer };
    }
    private static IEnumerable<JsonElement> Values(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
    private static long? InteractionCount(JsonElement value, string action)
    {
        if (!value.TryGetProperty("interactionStatistic", out var counters)) return null;
        var matches = new List<long>();
        foreach (var counter in Values(counters))
        {
            if (counter.ValueKind != JsonValueKind.Object || !counter.TryGetProperty("interactionType", out var type)) continue;
            var name = type.ValueKind == JsonValueKind.String ? type.GetString() : Text(type, "@type");
            if (name is null || name != action && !name.EndsWith("/" + action, StringComparison.Ordinal)) continue;
            if (Providers.ProviderJson.Long(counter, "userInteractionCount") is { } count) matches.Add(count);
        }
        return matches.Count == 1 ? matches[0] : null;
    }
    private static bool True(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;
    private static bool Restricted(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        (value.TryGetProperty("requiresSubscription", out var subscription) && subscription.ValueKind is not (JsonValueKind.False or JsonValueKind.Null) ||
         value.TryGetProperty("isAccessibleForFree", out var free) && free.ValueKind == JsonValueKind.False);
    private static int? Dimension(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var dimension)) return null;
        if (dimension.ValueKind == JsonValueKind.Object)
        {
            if (Text(dimension, "unitText") is { } unit && unit is not ("px" or "pixel" or "pixels")) return null;
            if (dimension.TryGetProperty("unitCode", out _)) return null;
            if (!dimension.TryGetProperty("value", out dimension)) return null;
        }
        var text = dimension.ValueKind == JsonValueKind.String ? dimension.GetString() : dimension.ValueKind == JsonValueKind.Number ? dimension.GetRawText() : null;
        if (dimension.ValueKind == JsonValueKind.Number && dimension.TryGetDecimal(out var numeric) && numeric == decimal.Truncate(numeric) && numeric is > 0 and <= 65536) return (int)numeric;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 65536 ? number : null;
    }
    private static double? Duration(string? text)
    {
        if (text is null || !Regex.IsMatch(text, @"\AP(?:[0-9]+(?:\.[0-9]+)?D)?(?:T(?:[0-9]+(?:\.[0-9]+)?H)?(?:[0-9]+(?:\.[0-9]+)?M)?(?:[0-9]+(?:\.[0-9]+)?S)?)?\z", RegexOptions.CultureInvariant)) return null;
        try { var duration = XmlConvert.ToTimeSpan(text).TotalSeconds; return duration > 0 && double.IsFinite(duration) ? duration : null; }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }
    private static long? ByteSize(string? text)
    {
        if (text is null) return null; var match = Regex.Match(text, @"\A([0-9]{1,19})(?:\s*(?:bytes|B))?\z", RegexOptions.CultureInvariant);
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) && value > 0 ? value : null;
    }
    private static string? Mime(string? text, string role)
    {
        if (text is null) return null; text = text.Trim();
        if (text.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("application/", StringComparison.OrdinalIgnoreCase)) return text.Split(';')[0].Trim();
        return text.ToLowerInvariant() switch { "mp4" => role == "audio" ? "audio/mp4" : "video/mp4", "webm" => role + "/webm", "mp3" => "audio/mpeg", "m3u8" => "application/vnd.apple.mpegurl", "mpd" => "application/dash+xml", _ => null };
    }
}
