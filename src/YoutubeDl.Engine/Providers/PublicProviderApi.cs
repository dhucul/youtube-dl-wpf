using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers;

internal static class PublicProviderApi
{
    internal static async Task<JsonElement> Read(MediaHttp http, Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url); using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); var bytes = await http.ReadBoundedBytesAsync(response, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes); return json.RootElement.Clone();
    }
    internal static JsonElement[] Rows(JsonElement root, string? key = null, int maximum = 10000)
    {
        var value = key is null ? root : root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var child) ? child : default;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw new InvalidDataException("The public provider returned an incomplete or oversized inventory.");
        return value.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    internal static string Numeric(JsonElement root, string key = "id") => Long(root, key) is > 0 and var value ? value.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("The public provider omitted a positive resource identity.");
    internal static string Title(JsonElement root, string key = "title") => Text(root, key) is { Length: > 0 and <= 8192 } value ? WebUtility.HtmlDecode(value) : throw new InvalidDataException("The public provider omitted its selected title.");
    internal static Uri Secure(string? raw, params string[] hosts) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.AbsoluteUri.Length <= 16384 && hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase)
        ? url : throw new InvalidDataException("The public provider returned an undeclared resource origin.");
    internal static DateTimeOffset? Date(string? text)
    {
        if (text is null) return null;
        return text.Length <= 128 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : throw new InvalidDataException("The public provider returned an invalid date.");
    }
    internal static bool Present(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) &&
        !(value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(value.GetString())) && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) && !(value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any());
    internal static bool Drm(JsonElement value) => Present(value, "drm") || Present(value, "licenseUrl") || Present(value, "widevine") || Present(value, "playready");
    internal static string Key(string provider, NativeDownloadRequest request) => "public:" + provider + ":" + request.Url.AbsoluteUri + ":" + Download.AdaptiveFragmentCheckpoint.Hash(request.GeoVerification?.Proxy ?? "");
    internal static PageMetadata Inventory(string extractor, string id, string title, List<NativePlaylistEntry> entries, NativeDownloadRequest request) => new()
    { Extractor = extractor, Id = id, PlaylistId = id, Title = title, Entries = entries, IsPlaylist = true, RevalidateCollectionMembership = true, AnonymousPlayback = true, SourcePageUrl = request.Url };
    internal static void Limit(int count, NativeDownloadRequest request) { if (count > request.MaximumPlaylistEntries) throw new InvalidDataException("The complete public inventory exceeds the configured limit; it was not truncated."); }
    internal static Match Match(string input, string pattern) => Regex.Match(input, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
}
