using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Providers.Feeds;

public sealed record ParsedMediaFeed(PageMetadata Metadata, Uri? Next, string? ExplicitId);

/// <summary>Independent RSS 2.0, Atom and Media RSS reader; external XML resources are never resolved.</summary>
public static class MediaFeedParser
{
    private const int MaximumCharacters = 5 * 1024 * 1024;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Podcast = "https://podcastindex.org/namespace/1.0";
    private static readonly XNamespace Chapters = "http://podlove.org/simple-chapters";

    public static bool IsFeed(string text, string? mimeType)
    {
        if (mimeType?.ToLowerInvariant() is "application/rss+xml" or "application/atom+xml") return true;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), Settings(ignoreDtd: true));
            reader.MoveToContent();
            return reader.LocalName == "rss" && reader.NamespaceURI == "" || reader.LocalName == "feed" && reader.NamespaceURI == Atom.NamespaceName;
        }
        catch (XmlException) { return false; }
    }

    public static ParsedMediaFeed Parse(string xml, Uri source, int maximumEntries = 1000, string? feedIdentity = null)
    {
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (xml.Length > MaximumCharacters) throw new InvalidDataException("The media feed exceeds the configured size limit.");
        XDocument document;
        try
        {
            using (var validation = XmlReader.Create(new StringReader(xml), Settings()))
                while (validation.Read()) if (validation.Depth > 64) throw new InvalidDataException("The media feed is nested too deeply.");
            using var reader = XmlReader.Create(new StringReader(xml), Settings());
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex) { throw new InvalidDataException("The media feed contains invalid or unsupported XML.", ex); }
        var root = document.Root ?? throw new InvalidDataException("The media feed has no root element.");
        var atom = root.Name == Atom + "feed";
        XElement channel;
        XName entryName;
        if (atom) { channel = root; entryName = Atom + "entry"; }
        else if (root.Name == "rss" && root.Elements("channel").Count() == 1) { channel = root.Element("channel")!; entryName = "item"; }
        else throw new NotSupportedException("This response is not a supported RSS 2.0 or Atom media feed.");
        var ns = atom ? Atom : XNamespace.None;
        var title = Text(channel.Element(ns + "title")) ?? source.Host;
        var explicitId = atom ? Text(channel.Element(Atom + "id")) : null;
        var self = Link(channel, "self", source);
        var identity = feedIdentity ?? explicitId ?? self?.AbsoluteUri ?? source.AbsoluteUri;
        var page = new PageMetadata { Id = identity, PlaylistId = identity, IsPlaylist = true, Extractor = "generic:feed", Title = title,
            Description = Text(channel.Element(ns + (atom ? "subtitle" : "description"))), Uploader = Author(channel, atom),
            ThumbnailUrl = Artwork(channel, source), SourcePageUrl = source };
        var elements = channel.Elements(entryName).ToArray();
        if (elements.Length > maximumEntries) throw new InvalidDataException("The media feed exceeds the configured item limit; it was not truncated.");
        var entries = elements.Select((entry, index) => ReadEntry(entry, index + 1, atom, source, identity, page)).ToArray();
        return new(page with { Entries = entries }, Link(channel, "next", source), explicitId);
    }

    private static NativePlaylistEntry ReadEntry(XElement entry, int position, bool atom, Uri source, string feedId, PageMetadata feed)
    {
        var ns = atom ? Atom : XNamespace.None;
        var title = Text(entry.Element(ns + "title")) ?? "Episode " + position;
        var pageUrl = atom ? Link(entry, "alternate", source) : Address(entry.Element("link"), Text(entry.Element("link")), source);
        var formats = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal); var reasons = new List<string>();
        var enclosures = atom ? entry.Elements(Atom + "link").Where(link => (string?)link.Attribute("rel") == "enclosure") : entry.Elements("enclosure");
        var contents = entry.Elements(Media + "content").Concat(entry.Elements(Media + "group").Elements(Media + "content")).ToArray();
        var all = enclosures.Concat(contents).ToArray();
        if (all.Length > 100) throw new InvalidDataException("A feed episode exposes too many media resources.");
        foreach (var item in all)
        {
            var raw = (string?)item.Attribute(atom && item.Name == Atom + "link" ? "href" : "url");
            var url = Address(item, raw, source) ?? throw new InvalidDataException("A feed enclosure has no media URL.");
            var mime = ((string?)item.Attribute("type"))?.Split(';')[0].Trim().ToLowerInvariant();
            var medium = (string?)item.Attribute("medium");
            if (mime?.StartsWith("image/", StringComparison.Ordinal) == true || medium is "image" or "document") continue;
            var kind = PageMediaExtractor.Classify(url, mime);
            if (kind == MediaKind.Unknown) { reasons.Add("The feed episode includes an unsupported enclosure type."); continue; }
            if ((string?)item.Attribute("expression") is { } expression && expression != "full")
            { reasons.Add("The feed exposes a sample or continuous stream rather than a complete episode."); continue; }
            if (!seen.Add(url.AbsoluteUri)) continue;
            var extension = PageMediaExtractor.ExtensionFrom(url, mime);
            var audio = medium == "audio" || medium != "video" && (mime?.StartsWith("audio/", StringComparison.Ordinal) == true && !mime.Contains("mpegurl", StringComparison.Ordinal) ||
                extension is "mp3" or "m4a" or "aac" or "ogg" or "opus" or "flac" or "wav");
            formats.Add(new() { Id = "enclosure-" + (formats.Count + 1), Url = url, Kind = kind, Source = "feed", Extension = extension, MimeType = mime,
                HasAudio = audio ? true : null, HasVideo = audio ? false : medium == "video" || mime?.StartsWith("video/", StringComparison.Ordinal) == true ? true : null,
                Width = PositiveInt(item, "width"), Height = PositiveInt(item, "height"), Bandwidth = Number(item, "bitrate") is { } bitrate ? bitrate * 1000 : null,
                AudioCodec = audio ? extension switch { "mp3" => "mp3", "aac" => "aac", "opus" => "opus", "flac" => "flac", _ => null } : null,
                FramesPerSecond = Number(item, "framerate"), FileSize = PositiveLong(item, item.Name == Media + "content" ? "fileSize" : "length") });
        }
        var id = Text(entry.Element(ns + (atom ? "id" : "guid"))) ?? pageUrl?.AbsoluteUri ?? formats.FirstOrDefault()?.Url.AbsoluteUri ?? source.AbsoluteUri + "#position-" + position;
        if (id.Length > 8192 || id.Any(char.IsControl)) throw new InvalidDataException("A feed episode has an invalid identity.");
        var duration = ProviderJson.Duration(Text(entry.Element(Itunes + "duration"))) ?? contents.Select(item => Number(item, "duration")).FirstOrDefault(value => value is not null);
        IReadOnlyList<ChapterMarker> chapters; string? chapterError = null;
        try { chapters = ReadChapters(entry, duration); }
        catch (InvalidDataException) { chapters = []; chapterError = "This feed episode has invalid chapter metadata; its media remains available, but chapter embedding cannot proceed."; reasons.Add(chapterError); }
        var media = new PageMetadata { Id = id, Extractor = "generic:feed", Title = title, Description = Text(entry.Element(ns + (atom ? "summary" : "description"))) ?? Text(entry.Element(Itunes + "summary")),
            Uploader = Author(entry, atom) ?? feed.Uploader, ThumbnailUrl = Artwork(entry, source) ?? feed.ThumbnailUrl, DurationSeconds = duration, Media = formats,
            UploadTimestamp = MetadataFacts.Timestamp(Text(entry.Element(ns + (atom ? "published" : "pubDate"))) ?? Text(entry.Element(ns + "updated"))),
            UploadDate = MetadataFacts.Day(MetadataFacts.Timestamp(Text(entry.Element(ns + (atom ? "published" : "pubDate"))) ?? Text(entry.Element(ns + "updated")))),
            SourcePageUrl = pageUrl ?? source, ArchiveIdentity = new("native:feed", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feedId + "\n" + id))).ToLowerInvariant()),
            Subtitles = Transcripts(entry, source), Chapters = chapters, ChapterRefusalReason = chapterError,
            Warnings = formats.Count == 0 ? reasons.Append("This feed entry has no supported media enclosure.").Distinct().ToArray() : reasons.Distinct().ToArray() };
        return new(position, id, title, pageUrl ?? formats.FirstOrDefault()?.Url ?? source, duration, media.ThumbnailUrl, formats.Count > 0, media);
    }

    private static IReadOnlyList<SubtitleTrack> Transcripts(XElement entry, Uri source)
    {
        var items = entry.Elements(Podcast + "transcript").ToArray();
        if (items.Length > 200) throw new InvalidDataException("A feed episode contains too many transcript references.");
        return items.Select(item =>
        {
            var extension = ((string?)item.Attribute("type"))?.ToLowerInvariant() switch { "text/vtt" => "vtt", "application/x-subrip" or "application/srt" => "srt", "application/ttml+xml" => "ttml", _ => null };
            if (extension is null) return null;
            var url = Address(item, (string?)item.Attribute("url"), source);
            return url is null ? null : new SubtitleTrack { Url = url, Extension = extension, Language = (string?)item.Attribute("language") ?? "und" };
        }).Where(track => track is not null).Select(track => track!).ToArray();
    }

    private static IReadOnlyList<ChapterMarker> ReadChapters(XElement entry, double? duration)
    {
        var items = entry.Element(Chapters + "chapters")?.Elements(Chapters + "chapter").ToArray() ?? [];
        if (items.Length == 0) return [];
        if (items.Length > 10000) throw new InvalidDataException("A feed episode contains too many chapter markers.");
        if (duration is null) return [];
        var starts = items.Select(item => ProviderJson.Duration((string?)item.Attribute("start")) ?? throw new InvalidDataException("A feed chapter has an invalid start time.")).ToArray();
        var chapters = items.Select((item, index) => new ChapterMarker { Title = (string?)item.Attribute("title") ?? "Chapter " + (index + 1), StartSeconds = starts[index], EndSeconds = index + 1 < starts.Length ? starts[index + 1] : duration.Value }).ToArray();
        _ = YoutubeDl.Engine.Media.ChapterMetadataWriter.Render(chapters);
        return chapters;
    }
    private static Uri? Link(XElement parent, string relation, Uri source)
    {
        var links = parent.Elements(Atom + "link").Where(link => ((string?)link.Attribute("rel") ?? "alternate") == relation).ToArray();
        if (relation is "next" or "self" && links.Length > 1) throw new InvalidDataException("The media feed contains ambiguous navigation links.");
        return links.Length == 0 ? null : Address(links[0], (string?)links[0].Attribute("href"), source);
    }
    private static string? Artwork(XElement element, Uri source)
    {
        var image = element.Element(Itunes + "image");
        var thumbnail = element.Element(Media + "thumbnail") ?? element.Element(Media + "group")?.Element(Media + "thumbnail");
        var rss = element.Element("image")?.Element("url");
        var atom = element.Element(Atom + "logo") ?? element.Element(Atom + "icon");
        return (Address(image, (string?)image?.Attribute("href"), source) ?? Address(thumbnail, (string?)thumbnail?.Attribute("url"), source) ??
            Address(rss, Text(rss), source) ?? Address(atom, Text(atom), source))?.AbsoluteUri;
    }
    private static string? Author(XElement element, bool atom) => Text(element.Element(Itunes + "author")) ?? Text(element.Element(Dc + "creator")) ??
        (atom ? Text(element.Element(Atom + "author")?.Element(Atom + "name")) : Text(element.Element("author")) ?? Text(element.Element("managingEditor")));
    private static Uri? Address(XElement? element, string? value, Uri source)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var basis = source;
        if (element is not null) foreach (var parent in element.AncestorsAndSelf().Reverse())
            if ((string?)parent.Attribute(XNamespace.Xml + "base") is { } xmlBase) basis = HttpUrl(basis, xmlBase);
        return HttpUrl(basis, value.Trim());
    }
    private static Uri HttpUrl(Uri basis, string value)
    {
        if (!Uri.TryCreate(basis, value, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0)
            throw new InvalidDataException("The media feed contains a non-HTTP or credential-bearing address.");
        return url;
    }
    private static string? Text(XElement? element) => string.IsNullOrWhiteSpace(element?.Value) ? null : WebUtility.HtmlDecode(element.Value).Trim();
    private static double? Number(XElement element, string attribute) => double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : null;
    private static long? PositiveLong(XElement element, string attribute) => long.TryParse((string?)element.Attribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    private static int? PositiveInt(XElement element, string attribute) => PositiveLong(element, attribute) is { } value && value <= int.MaxValue ? (int)value : null;
    private static XmlReaderSettings Settings(bool ignoreDtd = false) => new() { DtdProcessing = ignoreDtd ? DtdProcessing.Ignore : DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = MaximumCharacters, MaxCharactersFromEntities = MaximumCharacters, IgnoreComments = true, IgnoreProcessingInstructions = true };
}
