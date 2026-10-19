using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.InternetArchive;

public sealed class InternetArchiveProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("archive.org", "Internet Archive public media items", 200);
    public bool CanHandle(Uri url) => IsArchiveHost(url) && (url.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal) ||
        url.AbsolutePath.StartsWith("/download/", StringComparison.Ordinal) && url.AbsolutePath.TrimEnd('/').Split('/').Length == 3);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var id = ParseIdentifier(request.Url);
        var json = await http.GetStringAsync(new Uri("https://archive.org/metadata/" + Uri.EscapeDataString(id)), 8 * 1024 * 1024, ct).ConfigureAwait(false);
        return ParseMetadata(json, id, request.ExpandPlaylist, request.MaximumPlaylistEntries);
    }
    public static string ParseIdentifier(Uri url)
    {
        if (!IsArchiveHost(url)) throw new ArgumentException("This is not an Internet Archive item URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/(?:details|download)/(?<id>[A-Za-z0-9_][A-Za-z0-9_.-]{0,149})/?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("Native Internet Archive collection/search pages and nested item paths remain pending. Use an item or direct media address.");
        return match.Groups["id"].Value;
    }
    public static PageMetadata ParseMetadata(string json, string expectedId, bool expandPlaylist = false, int maximumEntries = 1000)
    {
        if (!Regex.IsMatch(expectedId, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,149}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("The expected archive item identifier is invalid.", nameof(expectedId));
        if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("The archive metadata exceeds the extraction limit.");
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "error") is not null || True(root, "is_dark"))
            throw new InvalidOperationException("This Internet Archive item is missing or unavailable.");
        var metadata = Object(root, "metadata");
        var id = Text(metadata, "identifier");
        if (id != expectedId) throw new InvalidDataException("The archive response belongs to a different item.");
        if (Text(metadata, "mediatype") == "collection") throw new NotSupportedException("Native Internet Archive collection enumeration is not implemented yet.");
        var allFiles = Array(root, "files");
        if (allFiles.Length > 10000) throw new InvalidDataException("The archive item contains too many files.");
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var file in allFiles)
        {
            if (Text(file, "name") is not { } name) continue;
            if (!index.TryAdd(name, file)) throw new InvalidDataException("The archive response has duplicate file names.");
        }
        var files = new List<(string Original, JsonElement File, MediaCandidate Candidate)>();
        foreach (var (name, file) in index)
        {
            if (True(file, "private")) continue;
            var url = FileUrl(id, name);
            var extension = PageMediaExtractor.ExtensionFrom(url);
            if (extension is null || PageMediaExtractor.Classify(url) != MediaKind.Progressive) continue;
            var original = Original(name, index);
            var audio = Text(file, "format")?.Contains("Video", StringComparison.OrdinalIgnoreCase) == true ? false :
                extension is "m4a" or "mp3" or "aac" or "opus" or "ogg" or "flac" or "wav";
            files.Add((original, file, new MediaCandidate
            {
                Id = name, Url = url, Extension = extension, Kind = MediaKind.Progressive, Source = "archive.org", Label = Text(file, "format"),
                Height = Integer(file, "height"), Width = Integer(file, "width"), Bandwidth = Number(file, "bitrate"),
                HasVideo = !audio, HasAudio = audio ? true : null, MimeType = audio ? "audio/" + extension : "video/" + extension
            }));
        }
        var groups = files.GroupBy(file => file.Original, StringComparer.Ordinal).ToArray();
        var page = new PageMetadata
        {
            Id = id, Extractor = "archive.org", Title = TextList(metadata, "title") ?? id,
            Description = TextList(metadata, "description"), Uploader = TextList(metadata, "creator"),
            DurationSeconds = Duration(Text(metadata, "runtime")), ThumbnailUrl = "https://archive.org/services/img/" + Uri.EscapeDataString(id)
        };
        if (groups.Length > 1)
        {
            if (!expandPlaylist) throw new InvalidOperationException("This archive item contains multiple media entries. Enable playlist expansion or provide a direct media file URL.");
            if (groups.Length > maximumEntries) throw new InvalidDataException("The archive item exceeds the configured playlist limit; it was not truncated silently.");
            var entries = groups.Select((group, position) =>
            {
                var selected = group.OrderByDescending(file => Text(file.File, "source") == "original").ThenByDescending(file => file.Candidate.Rank).First();
                var title = Path.GetFileNameWithoutExtension(group.Key) ?? group.Key;
                var groupFiles = group.OrderByDescending(file => Text(file.File, "source") == "original").ToArray();
                var snapshot = page with { Id = id + "/" + group.Key, Title = title, Media = groupFiles.Select(file => file.Candidate).ToArray(),
                    DurationSeconds = Duration(Text(selected.File, "length")), ReportedFormats = groupFiles.Select(Format).ToArray() };
                return new NativePlaylistEntry(position + 1, selected.Candidate.Id, title, selected.Candidate.Url,
                    snapshot.DurationSeconds, page.ThumbnailUrl, true, snapshot);
            }).ToArray();
            return page with { IsPlaylist = true, PlaylistId = id, Entries = entries };
        }
        var ordered = files.OrderByDescending(file => Text(file.File, "source") == "original").ToArray();
        return page with
        {
            Media = ordered.Select(file => file.Candidate).ToArray(),
            DurationSeconds = page.DurationSeconds ?? ordered.Select(file => Duration(Text(file.File, "length"))).FirstOrDefault(value => value is not null),
            ReportedFormats = ordered.Select(Format).ToArray(),
            Warnings = files.Count == 0 ? ["This archive item did not expose public media files supported by the native engine."] : []
        };

        static NativeFormat Format((string Original, JsonElement File, MediaCandidate Candidate) file) => new()
            {
                Id = file.Candidate.Id!, Url = file.Candidate.Url, Extension = file.Candidate.Extension, MimeType = file.Candidate.MimeType,
                Width = file.Candidate.Width, Height = file.Candidate.Height, Bandwidth = file.Candidate.Bandwidth,
                AudioOnly = file.Candidate.IsAudioOnly, HasAudio = file.Candidate.HasAudio, HasVideo = file.Candidate.HasVideo,
                FileSize = Long(file.File, "size"), Note = file.Candidate.Label, Protocol = "https"
            };
    }
    private static string Original(string name, IReadOnlyDictionary<string, JsonElement> files)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; depth < 16; depth++)
        {
            if (!visited.Add(name)) throw new InvalidDataException("Archive derivative references contain a cycle.");
            if (!files.TryGetValue(name, out var file) || Text(file, "original") is not { } original || string.IsNullOrEmpty(original)) return name;
            name = original;
        }
        throw new InvalidDataException("The archive derivative chain exceeds the supported depth.");
    }
    private static Uri FileUrl(string id, string name)
    {
        var parts = name.Split('/');
        if (name.Length > 2048 || parts.Any(part => part is "" or "." or ".." || part.Contains('\\') || part.Any(char.IsControl)))
            throw new InvalidDataException("The archive returned an invalid file path.");
        return new Uri("https://archive.org/download/" + Uri.EscapeDataString(id) + "/" + string.Join("/", parts.Select(Uri.EscapeDataString)));
    }
    private static bool IsArchiveHost(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo) && url.IsDefaultPort &&
        (url.Host.Equals("archive.org", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("www.archive.org", StringComparison.OrdinalIgnoreCase));
}
