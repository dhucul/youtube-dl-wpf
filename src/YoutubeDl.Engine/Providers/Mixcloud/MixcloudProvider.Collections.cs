using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Mixcloud;

public sealed partial class MixcloudProvider
{
    private static async Task<PageMetadata> CollectionAsync(MixcloudAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        using var owner = await JsonAsync(new(Api + address.Key), http, ct).ConfigureAwait(false); var root = owner.RootElement;
        if (Text(root, "key") != address.Key || MixcloudAddress.Parse(PublicUrl(Text(root, "url"))).Key != address.Key) throw new InvalidDataException("Mixcloud returned another requested collection identity.");
        if (address.Playlist && !string.Equals(Text(Object(root, "owner"), "username"), address.User, StringComparison.OrdinalIgnoreCase) || !address.Playlist && !string.Equals(Text(root, "username"), address.User, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mixcloud changed requested collection ownership.");
        var count = Integer(root, "cloudcast_count") ?? throw new InvalidDataException("Mixcloud omitted its declared collection count."); if (count > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Mixcloud collection exceeds the configured inventory limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var seenPages = new HashSet<string>(StringComparer.Ordinal); var endpoint = new Uri(Api + address.Key + "cloudcasts/?limit=100&offset=0");
        while (true)
        {
            ct.ThrowIfCancellationRequested(); if (!seenPages.Add(endpoint.AbsoluteUri)) throw new InvalidDataException("Mixcloud repeated a collection continuation.");
            using var page = await JsonAsync(endpoint, http, ct).ConfigureAwait(false); var response = page.RootElement;
            if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 100 || entries.Count + data.GetArrayLength() > count) throw new InvalidDataException("Mixcloud omitted or oversized its collection page.");
            foreach (var item in data.EnumerateArray())
            {
                var key = Text(item, "key"); var child = MixcloudAddress.Parse(PublicUrl(Text(item, "url"))); if (child.Slug is null || child.Playlist || child.Key != key || !address.Playlist && (!string.Equals(child.User, address.User, StringComparison.OrdinalIgnoreCase) || !seen.Add(child.Key))) throw new InvalidDataException("Mixcloud repeated or replaced a requested collection member.");
                ValidateShow(item, child); entries.Add(new(entries.Count + 1, child.Key.Trim('/'), Text(item, "name") ?? child.Slug!, child.Web, Number(item, "audio_length"), Picture(item), true, ExpectedArchiveIdentity: new("mixcloud", child.Key.Trim('/'))));
            }
            var paging = Object(response, "paging"); var next = Text(paging, "next");
            if (entries.Count == count) { if (!string.IsNullOrWhiteSpace(next)) throw new InvalidDataException("Mixcloud supplied more pages than its complete declared count."); break; }
            if (data.GetArrayLength() == 0 || string.IsNullOrWhiteSpace(next)) throw new InvalidDataException("Mixcloud ended before its complete declared collection count.");
            endpoint = Continuation(next, address, entries.Count);
        }
        using var refreshed = await JsonAsync(new(Api + address.Key), http, ct).ConfigureAwait(false); if (Text(refreshed.RootElement, "key") != address.Key || Integer(refreshed.RootElement, "cloudcast_count") != count) throw new InvalidDataException("Mixcloud changed collection identity or count during pagination.");
        return new() { Id = address.Key.Trim('/'), PlaylistId = address.Key.Trim('/'), Extractor = address.Playlist ? "mixcloud:playlist" : "mixcloud:user", IsPlaylist = true, Title = Text(root, "name") ?? address.User, Uploader = address.User, Description = Text(root, "biog"), ThumbnailUrl = address.Playlist ? null : Picture(root), SourcePageUrl = address.Web, Entries = entries };
    }
    private static Uri Continuation(string raw, MixcloudAddress address, int offset)
    {
        var uri = PublicUrl(raw); if (uri.Host != "api.mixcloud.com" || uri.AbsolutePath != address.Key + "cloudcasts/") throw new InvalidDataException("Mixcloud continued another collection or API operation.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = item.Split('=', 2); if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) throw new InvalidDataException("Mixcloud repeated continuation query fields."); }
        if (query.Count != 2 || query.GetValueOrDefault("limit") != "100" || query.GetValueOrDefault("offset") != offset.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Mixcloud changed its public collection continuation scope."); return uri;
    }
}
