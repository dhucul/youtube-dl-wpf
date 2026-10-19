using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.MicrosoftLearn;

public sealed partial class MicrosoftLearnProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("microsoftlearn", "Microsoft Learn public episodes sessions and shows", 200);
    public bool CanHandle(Uri url) => MicrosoftLearnAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => MicrosoftLearnAddress.Parse(request.Url).EntryId is { } id ? new("microsoftlearn", id) : null;
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = MicrosoftLearnAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (MicrosoftLearnAddress.Query(request.Url).Keys.Any(key => key is "terms" or "search" or "$filter" or "$orderby" or "$skip" or "$top" or "branch"))
            throw new NotSupportedException("Filtered, paged or preview Microsoft Learn listings need additional native support.");
        PageMetadata? metadata = null; Dictionary<string, string>? tags = null; var id = address.EntryId;
        if (id is null)
        {
            var document = await anonymous.GetDocumentAsync(address.Page, 4 * 1024 * 1024, ct).ConfigureAwait(false);
            var returned = MicrosoftLearnAddress.Parse(document.Url);
            if (returned.Kind != address.Kind || returned.Collection != address.Collection || returned.Slug != address.Slug)
                throw new InvalidDataException("Microsoft Learn redirected to another video or collection.");
            tags = Metadata(document.Text); metadata = PageMetadataReader.Read(document.Text, document.Url);
            if (tags.GetValueOrDefault("locale") != returned.Locale || tags.GetValueOrDefault("og:url") is not { } canonical || MicrosoftLearnAddress.Parse(new(canonical)) != returned)
                throw new InvalidDataException("Microsoft Learn page metadata does not confirm its canonical identity.");
            if (address.IsCollection)
            {
                if (tags.GetValueOrDefault("schema") != "Show" || tags.GetValueOrDefault("uid") != address.Collection) throw new InvalidDataException("Microsoft Learn returned another show identity.");
                return await CollectionAsync(address, tags.GetValueOrDefault("type") == "series", metadata, request.MaximumPlaylistEntries, anonymous, ct).ConfigureAwait(false);
            }
            var uid = tags.GetValueOrDefault("uid");
            if (uid is null || !AuthoringUid(uid) || address.Kind == "shows" && tags.GetValueOrDefault("showId") != address.Collection ||
                tags.GetValueOrDefault("schema") is not ("Episode" or "Session")) throw new InvalidDataException("Microsoft Learn returned another episode/session identity.");
            if (tags.GetValueOrDefault("entryId") is { Length: > 0 } entry) id = EntryId(entry);
            else if (tags.GetValueOrDefault("externalVideoUrl") is { Length: > 0 } external)
            {
                var delegated = PublicUrl(external);
                return metadata with { Id = uid, Extractor = "microsoftlearn:external", Media = [], SourcePageUrl = address.Page, EmbeddedPages = [delegated], AnonymousPlayback = true };
            }
            else throw new NotSupportedException("This Microsoft Learn page does not expose a supported public video entry.");
        }
        var endpoint = new Uri("https://learn.microsoft.com/api/video/public/v1/entries/" + id + "?isAMS=false");
        using var response = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false);
        var root = response.RootElement;
        if (Text(root, "id") is not { } returnedId || EntryId(returnedId) != id) throw new InvalidDataException("Microsoft Learn returned another video entry.");
        var video = Object(root, "publicVideo");
        if (video.ValueKind != JsonValueKind.Object) throw new NotSupportedException("Microsoft Learn did not expose public playback for this entry.");
        var duration = Number(video, "durationInMillisecond") is { } milliseconds ? milliseconds / 1000 : (double?)null;
        if (duration is <= 0 or > 604800) throw new InvalidDataException("Microsoft Learn returned an invalid video duration.");
        var audioPath = Text(video, "audioUrl") is { Length: > 0 } audioRaw ? PublicUrl(audioRaw).AbsolutePath : "";
        var media = new List<MediaCandidate>();
        Add("high", "highQualityVideoUrl", MediaKind.Progressive, "mp4");
        Add("medium", "mediumQualityVideoUrl", MediaKind.Progressive, "mp4");
        Add("low", "lowQualityVideoUrl", MediaKind.Progressive, "mp4");
        Add("hls", "adaptiveVideoHLSUrl", MediaKind.Hls, "ts");
        Add("dash", "adaptiveVideoDashUrl", MediaKind.Dash, "mp4");
        Add("audio", "audioUrl", MediaKind.Progressive, audioPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || audioPath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ? "m4a" : "mp3");
        if (Text(video, "adaptiveVideoUrl") is { Length: > 0 } && media.All(item => item.Kind is not (MediaKind.Hls or MediaKind.Dash)))
            throw new NotSupportedException("This legacy Microsoft Learn adaptive playback layout requires additional support.");
        var captions = new List<SubtitleTrack>(); var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var caption in Array(video, "captions"))
        {
            if (captions.Count >= 128 || Text(caption, "language") is not { } language || !Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || !languages.Add(language))
                throw new InvalidDataException("Microsoft Learn returned invalid or repeated caption languages.");
            var resource = Resource(Text(caption, "url"), id);
            if (!resource.AbsolutePath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("This Microsoft Learn caption format needs additional support.");
            captions.Add(new() { Url = resource, Language = language, Extension = "vtt" });
        }
        var thumbnails = new List<ThumbnailCandidate>(); var sizes = Object(video, "thumbnailOtherSizes");
        if (sizes.ValueKind == JsonValueKind.Object) foreach (var size in sizes.EnumerateObject())
        {
            if (size.Value.ValueKind != JsonValueKind.String || size.Value.GetString() is not { Length: > 0 } raw) continue;
            var match = Regex.Match(size.Name, @"\Aw(?<width>[1-9][0-9]{1,4})Url\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || thumbnails.Count >= 32) throw new InvalidDataException("Microsoft Learn returned unsupported poster metadata.");
            thumbnails.Add(new() { Id = size.Name, Url = Resource(raw, id), Width = int.Parse(match.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture) });
        }
        var poster = thumbnails.OrderByDescending(item => item.Width).FirstOrDefault();
        return new() { Id = id, Extractor = Info.Id, ArchiveIdentity = new("microsoftlearn", id), Title = Text(root, "title") ?? metadata?.Title ?? id,
            Description = metadata?.Description, Uploader = "Microsoft Learn", SourcePageUrl = address.Page, DurationSeconds = duration, Media = media,
            AnonymousPlayback = true, Subtitles = captions, Thumbnails = thumbnails, ThumbnailUrl = poster?.Url.AbsoluteUri,
            Warnings = media.Count == 0 ? ["This Microsoft Learn entry has no ready public media resources."] : [] };

        void Add(string format, string field, MediaKind kind, string extension)
        {
            if (Text(video, field) is not { Length: > 0 } raw) return;
            var resource = Resource(raw, id);
            if (!resource.AbsolutePath.EndsWith(kind == MediaKind.Hls ? ".m3u8" : kind == MediaKind.Dash ? ".mpd" : "." + extension, StringComparison.OrdinalIgnoreCase) &&
                !(format == "audio" && extension == "m4a" && resource.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Microsoft Learn's media container does not match its declared resource role.");
            var encoded = Regex.Match(Path.GetFileName(resource.AbsolutePath), @"\A(?<stem>.+)__(?<width>[1-9][0-9]{1,4})x(?<height>[1-9][0-9]{1,4})_AACAudio_[0-9]+_[0-9]+\.mp4\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            // Current One Stream encodings offer the original video and audio
            // tracks separately despite the AACAudio marker in video filenames.
            var separate = format != "audio" && kind == MediaKind.Progressive && encoded.Success &&
                Path.GetFileName(audioPath).StartsWith(encoded.Groups["stem"].Value + "__AACAudio_", StringComparison.Ordinal) &&
                (audioPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || audioPath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));
            media.Add(new() { Id = format, Label = format, Url = resource, Kind = kind, Extension = extension, HasVideo = format != "audio", HasAudio = format == "audio" ? true : separate ? false : null,
                Width = separate ? int.Parse(encoded.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                Height = separate ? int.Parse(encoded.Groups["height"].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                IsCompleteMedia = !separate, TimelineId = separate || format == "audio" ? id : null, Source = "microsoftlearn",
                VerificationDemuxer = kind == MediaKind.Hls ? "mpegts" : extension == "mp3" ? "mp3" : "mov", VerificationDurationSeconds = duration });
        }
    }
    private static string EntryId(string raw) { try { return MicrosoftLearnAddress.GuidId(raw); } catch (ArgumentException) { throw new InvalidDataException("Microsoft Learn returned an invalid video entry ID."); } }
    private static bool AuthoringUid(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,399}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url :
        throw new InvalidDataException("Microsoft Learn returned an unsupported public resource address.");
    private static Uri Resource(string? raw, string id)
    {
        var url = PublicUrl(raw);
        if (!url.Host.EndsWith(".azurefd.net", StringComparison.Ordinal) || !url.Host.StartsWith("videoencodingpublic-", StringComparison.Ordinal) ||
            !url.AbsolutePath.StartsWith("/public-" + id + "/", StringComparison.Ordinal)) throw new NotSupportedException("Microsoft Learn returned an unverified public encoding origin or video asset.");
        return url;
    }
    private static Dictionary<string, string> Metadata(string html)
    {
        var head = Regex.Match(html, @"<head\b[^>]*>(?<head>.*?)</head\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (!head.Success) throw new InvalidDataException("Microsoft Learn returned an incomplete page head.");
        var clean = Regex.Replace(head.Groups["head"].Value, @"<!--.*?-->|<script\b[^>]*>.*?</script\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(clean, @"<meta\b(?<attributes>[^>]{0,8192})>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)))
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Regex.Matches(tag.Groups["attributes"].Value, @"(?<name>[A-Za-z0-9_:-]+)\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                if (!attrs.TryAdd(attr.Groups["name"].Value, WebUtility.HtmlDecode(attr.Groups["d"].Success ? attr.Groups["d"].Value : attr.Groups["s"].Value))) throw new InvalidDataException("Microsoft Learn returned repeated page metadata attributes.");
            var key = attrs.GetValueOrDefault("name") ?? attrs.GetValueOrDefault("property");
            if (key is null || !attrs.TryGetValue("content", out var value)) continue;
            if (!result.TryAdd(key, value) && result[key] != value) throw new InvalidDataException("Microsoft Learn returned conflicting page metadata identities.");
        }
        return result;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("Microsoft Learn redirected its public metadata API.");
        JsonDocument json;
        try { json = JsonDocument.Parse(response.Text, new() { MaxDepth = 48 }); } catch (JsonException) { throw new InvalidDataException("Microsoft Learn returned malformed public metadata."); }
        try
        {
            var nodes = new Stack<JsonElement>(); nodes.Push(json.RootElement); var count = 0;
            while (nodes.TryPop(out var node))
            {
                if (++count > 100000) throw new InvalidDataException("Microsoft Learn returned excessive metadata fields.");
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in node.EnumerateObject())
                    { if (!names.Add(property.Name)) throw new InvalidDataException("Microsoft Learn returned repeated metadata fields."); nodes.Push(property.Value); }
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) nodes.Push(child);
            }
            return json;
        }
        catch { json.Dispose(); throw; }
    }
}
