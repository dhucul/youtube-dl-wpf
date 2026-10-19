using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Music;

internal static class MusicCatalog
{
    internal static string Id(JsonElement value, string key = "id") => Long(value, key) is > 0 and var number ? number.ToString(CultureInfo.InvariantCulture) :
        Text(value, key) is { } text && Regex.IsMatch(text, @"\A[A-Za-z0-9_-]{1,64}\z") ? text : throw new InvalidDataException("The music service returned an invalid identity.");
    internal static string Numeric(JsonElement value, string key = "id") => Long(value, key) is > 0 and var number ? number.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("The music service omitted a numeric identity.");
    internal static JsonElement[] Rows(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 10000) throw new InvalidDataException("The music service returned an incomplete or oversized inventory.");
        return rows.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    internal static string Title(JsonElement value, string key = "title") => WebUtility.HtmlDecode(Text(value, key) ?? Text(value, "name") ?? "Untitled");
    internal static Uri Query(string endpoint, params (string Key, string? Value)[] values) => new UriBuilder(endpoint)
    { Query = string.Join('&', values.Where(pair => pair.Value is not null).Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!))) }.Uri;
    internal static async Task<JsonElement> ReadAsync(MediaHttp http, Uri url, CancellationToken ct, bool allowArray = false)
    {
        var document = await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != url) throw new InvalidDataException("The music service changed the selected API resource.");
        using var json = JsonDocument.Parse(document.Text);
        if (json.RootElement.ValueKind != JsonValueKind.Object && !(allowArray && json.RootElement.ValueKind == JsonValueKind.Array)) throw new InvalidDataException("The music service returned an invalid API envelope.");
        return json.RootElement.Clone();
    }
    internal static bool Host(Uri url, params string[] hosts) => url.IsAbsoluteUri && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        hosts.Any(host => url.Host == host || url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    internal static Uri Resource(string? text, params string[] hosts) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && Host(uri, hosts) ? uri : throw new InvalidDataException("The music service returned a resource outside its supported origins.");
    internal static string? Image(string? text, params string[] hosts) => string.IsNullOrEmpty(text) ? null : Resource(text.StartsWith("//", StringComparison.Ordinal) ? "https:" + text : text, hosts).AbsoluteUri;
    internal static PageMetadata Inventory(string provider, string id, string title, List<NativePlaylistEntry> entries, Uri source) =>
        new() { Extractor = provider, Id = id, PlaylistId = id, Title = title, IsPlaylist = true, Entries = entries, RevalidateCollectionMembership = true, SourcePageUrl = source, AnonymousPlayback = true };
    internal static NativePlaylistEntry Entry(PageMetadata metadata, int position, Uri url, bool available = true) => new(position, metadata.Id, metadata.Title, url, metadata.DurationSeconds, metadata.ThumbnailUrl,
        available, ExpectedArchiveIdentity: metadata.ArchiveIdentity ?? new(metadata.Extractor, metadata.Id!), CollectionMemberId: metadata.Id);
    internal static MediaCandidate Audio(Uri url, string id, string extension, double duration, string provider, long? size = null, double? bandwidth = null) =>
        new() { Id = id, Url = url, Extension = extension, Kind = extension is "m3u8" ? MediaKind.Hls : extension is "mpd" ? MediaKind.Dash : MediaKind.Progressive, Source = provider,
            HasAudio = true, HasVideo = false, AudioCodec = extension switch { "mp3" => "mp3", "m4a" or "aac" => "aac", "flac" => "flac", "ogg" => "vorbis", _ => null }, FileSize = size, Bandwidth = bandwidth,
            VerificationDemuxer = extension switch { "mp3" => "mp3", "m4a" or "m3u8" or "mpd" => "mov", "aac" => "aac", "flac" => "flac", "ogg" => "ogg", _ => null },
            VerificationDurationSeconds = duration, VerificationMinimumDurationSeconds = duration, VerificationMinimumDurationToleranceSeconds = 1.1, VerificationFileSize = size };
    internal static string AudioExtension(Uri url, string? type = null)
    {
        var extension = PageMediaExtractor.ExtensionFrom(url, type);
        return extension is "mp3" or "m4a" or "aac" or "flac" or "ogg" or "m3u8" or "mpd" ? extension : throw new NotSupportedException("This music encoding is not supported yet.");
    }
    internal static void Limit(int count, NativeDownloadRequest request) { if (count > request.MaximumPlaylistEntries) throw new InvalidDataException("The complete music inventory exceeds the configured limit; it was not truncated."); }
    internal static bool Present(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) && !(field.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(field.GetString()));
    internal static Task<bool> RecordedAsync(NativeDownloadRequest request, PageMetadata page, CancellationToken ct) => request.SelectionState is not null && !string.IsNullOrWhiteSpace(request.DownloadArchiveFile)
        ? Download.DownloadArchive.ContainsAsync(request.DownloadArchiveFile, page.ArchiveIdentity ?? new(page.Extractor, page.Id!), ct) : Task.FromResult(false);
}
