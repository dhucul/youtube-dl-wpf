using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Imgur;

/// <summary>Reads public declarations without executing Imgur's JavaScript.</summary>
public sealed class ImgurProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("imgur", "Imgur public videos and album videos", 200);
    public bool CanHandle(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { var address = Parse(request.Url); return address.Collection ? null : new("imgur", address.Id); }

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = Parse(request.Url);
        // gifv denotes a player; the page must supply the actual downloadable resource.
        var pageUrl = address.Collection ? request.Url : new Uri("https://imgur.com/" + address.Id);
        var document = await http.GetDocumentAsync(pageUrl, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (Parse(document.Url) != address) throw new InvalidDataException("Imgur redirected to another media identity.");
        var html = Regex.Replace(document.Text, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var declarations = Regex.Matches(html, @"<script\s*>\s*window\.postDataJSON\s*=\s*(?<json>""(?:\\.|[^""\\])*"")\s*;?\s*</script\s*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (declarations.Count == 0)
        {
            if (address.Collection) throw new NotSupportedException("Imgur did not expose a complete public album inventory.");
            return Single(html, address.Id, pageUrl);
        }
        if (declarations.Count != 1) throw new InvalidDataException("Imgur returned conflicting album declarations.");
        using var json = ReadDeclaration(declarations[0].Groups["json"].Value);
        var root = json.RootElement;
        if (Text(root, "id") != address.Id) throw new InvalidDataException("Imgur returned another post identity.");
        if (True(root, "is_pending") || True(root, "is_ad")) throw new NotSupportedException("The Imgur post is not ready public media.");
        var items = Array(root, "media");
        if (items.Length == 0 || items.Length > 10000 || Integer(root, "image_count") != items.Length)
            throw new InvalidDataException("Imgur returned an incomplete or excessive media inventory.");
        if (items.Length > request.MaximumPlaylistEntries) throw new InvalidDataException("The Imgur album exceeds the configured entry limit; it was not truncated.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var title = Text(root, "title") ?? address.Id; var uploader = Text(Object(root, "account"), "username");
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested(); var id = Text(item, "id");
            if (!ValidId(id) || !seen.Add(id!)) throw new InvalidDataException("Imgur returned a missing or repeated media identity.");
            var metadata = Object(item, "metadata");
            var itemTitle = Text(metadata, "title") is { Length: > 0 } named ? named : title;
            var source = new Uri("https://imgur.com/" + id); PageMetadata? resolved = null;
            if (Text(item, "type") == "video" && Text(item, "mime_type") is "video/mp4" or "video/webm")
            {
                var extension = Text(item, "mime_type") == "video/mp4" ? "mp4" : "webm";
                var url = Resource(Text(item, "url"), id!, extension);
                if (!metadata.TryGetProperty("has_sound", out var sound) || sound.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Imgur omitted the video's audio state.");
                var duration = Number(metadata, "duration");
                if (duration is not > 0) throw new InvalidDataException("Imgur omitted the complete video's duration.");
                resolved = new() { Id = id, Extractor = "imgur", ArchiveIdentity = new("imgur", id!), Title = itemTitle,
                    Description = Text(metadata, "description") ?? Text(root, "description"), Uploader = uploader, DurationSeconds = duration, SourcePageUrl = source,
                    Media = [new() { Id = extension, Url = url, Kind = MediaKind.Progressive, Extension = extension, MimeType = Text(item, "mime_type"),
                        HasVideo = true, HasAudio = sound.GetBoolean(), IsCompleteMedia = true, Width = Integer(item, "width"), Height = Integer(item, "height"),
                        FileSize = Long(item, "size") is > 0 and var size ? size : null, Source = "imgur", VerificationDemuxer = extension == "mp4" ? "mov" : "matroska",
                        VerificationDurationSeconds = duration }] };
            }
            entries.Add(new(entries.Count + 1, id, itemTitle, source, Number(metadata, "duration"), null, resolved is not null, resolved, new("imgur", id!)));
        }
        if (!address.Collection && !True(root, "is_album") && entries.Count == 1 && entries[0].Id == address.Id)
            return entries[0].ResolvedMedia ?? throw new NotSupportedException("This Imgur item does not expose a supported video.");
        return new() { Id = address.Id, PlaylistId = address.Id, Extractor = "imgur:album", Title = title, Description = Text(root, "description"),
            Uploader = uploader, IsPlaylist = true, Entries = entries, SourcePageUrl = pageUrl,
            Warnings = entries.Any(entry => !entry.Available) ? ["Still images and unsupported album items retain their positions but are not downloaded as videos."] : [] };
    }

    private static PageMetadata Single(string html, string id, Uri page)
    {
        // Discard script bodies so JavaScript strings cannot masquerade as meta elements.
        html = Regex.Replace(html, @"<script\b[^>]*>.*?</script\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var tags = Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in tags)
        {
            var attrs = Regex.Matches(tag.Value, @"(?<name>[\w:-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in attrs) values[attribute.Groups["name"].Value] = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            if (!values.TryGetValue("property", out var key) || !values.TryGetValue("content", out var value)) continue;
            if (!key.StartsWith("og:", StringComparison.OrdinalIgnoreCase)) continue;
            if (meta.TryGetValue(key, out var old) && old != value) throw new InvalidDataException("Imgur returned conflicting video metadata.");
            meta[key] = value;
        }
        var urls = new[] { "og:video", "og:video:url", "og:video:secure_url" }.Select(key => meta.GetValueOrDefault(key)).Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length != 1 || meta.GetValueOrDefault("og:video:type") is not ("video/mp4" or "video/webm"))
            throw new NotSupportedException("Imgur did not expose one supported public video.");
        var extension = meta["og:video:type"] == "video/mp4" ? "mp4" : "webm";
        var resource = Resource(urls[0], id, extension);
        return new() { Id = id, Extractor = "imgur", ArchiveIdentity = new("imgur", id), Title = meta.GetValueOrDefault("og:title") is { Length: > 0 } title && title != "imgur.com" ? title : id,
            Description = meta.GetValueOrDefault("og:description"), SourcePageUrl = page,
            Media = [new() { Id = extension, Url = resource, Kind = MediaKind.Progressive, Extension = extension, MimeType = meta["og:video:type"],
                HasVideo = true, IsCompleteMedia = true, Width = Dimension("og:video:width"), Height = Dimension("og:video:height"), Source = "imgur",
                VerificationDemuxer = extension == "mp4" ? "mov" : "matroska" }] };
        int? Dimension(string key) => int.TryParse(meta.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }
    private static JsonDocument ReadDeclaration(string literal)
    {
        // The site's double-quoted JavaScript literal additionally escapes apostrophes.
        // Normalize that one observed escape while retaining paired backslashes verbatim.
        var normalized = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] == '\\' && i + 1 < literal.Length)
            { var next = literal[++i]; if (next != '\'') normalized.Append('\\'); normalized.Append(next); }
            else normalized.Append(literal[i]);
        }
        try { return JsonDocument.Parse(JsonSerializer.Deserialize<string>(normalized.ToString())!, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw new InvalidDataException("Imgur returned an invalid public post declaration."); }
    }
    private static Uri Resource(string? raw, string id, string extension)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "i.imgur.com" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.AbsolutePath != "/" + id + "." + extension)
            throw new InvalidDataException("Imgur's offered video URL does not match the selected media identity.");
        return url;
    }
    private static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"\A(?:[A-Za-z0-9]{5}|[A-Za-z0-9]{7})\z", RegexOptions.CultureInvariant);
    private static (string Id, bool Collection) Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.Host is not ("imgur.com" or "www.imgur.com" or "m.imgur.com" or "i.imgur.com"))
            throw new ArgumentException("Unsupported Imgur address.");
        var path = url.AbsolutePath.Trim('/').Split('/');
        if (path.Length == 2 && url.Host != "i.imgur.com" && path[0] is "a" or "gallery")
        { var id = path[1].Split('-')[^1]; if (ValidId(id)) return (id, true); }
        if (path.Length == 1)
        { var id = path[0]; if (id.EndsWith(".gifv", StringComparison.Ordinal)) id = id[..^5]; else if (url.Host == "i.imgur.com") throw new ArgumentException("Not an Imgur player.");
            if (ValidId(id)) return (id, false); }
        throw new ArgumentException("Unsupported Imgur page type.");
    }
}
