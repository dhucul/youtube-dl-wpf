using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bandcamp;

public sealed class BandcampProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("bandcamp", "Bandcamp exposed public track and album streams", 200);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) &&
        url.Host.EndsWith(".bandcamp.com", StringComparison.OrdinalIgnoreCase) && (url.AbsolutePath.StartsWith("/track/", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/album/", StringComparison.Ordinal));
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (!CanHandle(request.Url)) throw new ArgumentException("Use a supported Bandcamp track or album URL.");
        var html = await http.GetStringAsync(request.Url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var match = Regex.Match(html, @"\bdata-tralbum\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new NotSupportedException("The Bandcamp page did not expose its track data. Additional access or page support may be required.");
        var json = WebUtility.HtmlDecode(match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value);
        return ParseTralbum(json, request.Url, request.MaximumPlaylistEntries);
    }
    public static PageMetadata ParseTralbum(string json, Uri pageUrl, int maximumEntries = 1000)
    {
        if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new InvalidDataException("The Bandcamp track data exceeds the extraction limit.");
        if (maximumEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = Object(root, "current");
        var id = Text(root, "id") ?? Long(root, "id")?.ToString(CultureInfo.InvariantCulture);
        if (id is null || !Regex.IsMatch(id, @"\A[0-9]{1,20}\z")) throw new InvalidDataException("Bandcamp did not return a valid item identifier.");
        var artist = Text(root, "artist");
        var metadata = new PageMetadata { Id = id, Extractor = "bandcamp", Title = Text(current, "title") ?? id, Uploader = artist,
            Description = Text(current, "about") };
        var tracks = Array(root, "trackinfo");
        if (tracks.Length > maximumEntries) throw new InvalidDataException("The Bandcamp album exceeds its configured entry limit; it was not truncated.");
        if (tracks.Length == 0) throw new InvalidOperationException("Bandcamp did not return accessible track metadata.");
        var album = pageUrl.AbsolutePath.StartsWith("/album/", StringComparison.Ordinal) || Text(root, "item_type") == "album";
        var resolved = tracks.Select((track, index) =>
        {
            var trackId = Text(track, "track_id") ?? Long(track, "track_id")?.ToString(CultureInfo.InvariantCulture) ?? throw new InvalidDataException("A Bandcamp track has no identity.");
            var formats = Object(track, "file");
            var media = new List<MediaCandidate>();
            if (formats.ValueKind == JsonValueKind.Object)
                foreach (var file in formats.EnumerateObject())
                {
                    if (file.Value.ValueKind != JsonValueKind.String || !Uri.TryCreate(file.Value.GetString(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo))
                        throw new InvalidDataException("Bandcamp returned an invalid stream URL.");
                    var codec = file.Name.Split('-', 2)[0];
                    if (codec is not ("mp3" or "aac" or "opus" or "flac" or "wav")) continue;
                    var extension = codec == "aac" ? "m4a" : codec;
                    var bitrateText = file.Name.Split('-', 2).ElementAtOrDefault(1);
                    media.Add(new() { Id = file.Name, Url = url, Kind = MediaKind.Progressive, Extension = extension, MimeType = "audio/" + codec,
                        HasAudio = true, HasVideo = false, AudioCodec = codec, Bandwidth = double.TryParse(bitrateText, NumberStyles.None, CultureInfo.InvariantCulture, out var bitrate) ? bitrate * 1000 : null, Source = "bandcamp" });
                }
            var title = Text(track, "title") ?? "Track " + (index + 1);
            var info = metadata with { Id = trackId, Title = title, DurationSeconds = Number(track, "duration"), Media = media,
                Warnings = media.Count == 0 ? ["This Bandcamp track did not expose a public stream for the supplied access context."] : [] };
            var position = Integer(track, "track_num") ?? index + 1;
            var trackPage = Text(track, "title_link") is { } link ? new Uri(pageUrl, link) : pageUrl;
            if (trackPage.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(trackPage.UserInfo)) throw new InvalidDataException("Bandcamp returned an invalid track page URL.");
            return new NativePlaylistEntry(position, trackId, title, trackPage, info.DurationSeconds, null, true, info);
        }).ToArray();
        if (!album)
        {
            if (resolved.Length != 1) throw new InvalidDataException("The Bandcamp track page returned multiple tracks.");
            return resolved[0].ResolvedMedia!;
        }
        if (resolved.Any(entry => entry.Position < 1) || resolved.Select(entry => entry.Position).Distinct().Count() != resolved.Length)
            throw new InvalidDataException("The Bandcamp album returned duplicate or invalid track positions.");
        return metadata with { IsPlaylist = true, PlaylistId = id, Entries = resolved };
    }
}
