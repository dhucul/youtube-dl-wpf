using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Odysee;

public sealed partial class OdyseeProvider
{
    private static async Task<PageMetadata> ChannelAsync(JsonElement channel, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        ValidateLimit(request.MaximumPlaylistEntries);
        var id = Text(channel, "claim_id")!; var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        const int pageSize = 50; int? total = null;
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await OdyseeRpc.CallAsync("claim_search", new { channel_ids = new[] { id }, claim_type = new[] { "stream" }, stream_types = new[] { "video", "audio" },
                has_source = true, order_by = new[] { "release_time" }, page, page_size = pageSize }, http, ct).ConfigureAwait(false);
            OdyseeRpc.CheckSearch(result);
            var count = Integer(result, "total_items") ?? throw new InvalidDataException("The Odysee channel count is missing.");
            if (count > request.MaximumPlaylistEntries) throw new InvalidDataException("The Odysee channel exceeds the configured item limit; it was not truncated silently.");
            if (total is not null && total != count || Integer(result, "page") != page || Integer(result, "page_size") != pageSize)
                throw new InvalidDataException("The Odysee channel changed or returned an unexpected page.");
            total = count;
            var expectedPages = (count + pageSize - 1) / pageSize;
            if (Integer(result, "total_pages") is not { } pages || (count == 0 ? pages is not (0 or 1) : pages != expectedPages))
                throw new InvalidDataException("The Odysee channel pagination is inconsistent.");
            var items = Array(result, "items");
            if (items.Length > pageSize || items.Length != Math.Min(pageSize, count - entries.Count)) throw new InvalidDataException("The Odysee channel page is incomplete or excessive.");
            foreach (var item in items)
            {
                ValidateClaim(item);
                if (!seen.Add(Text(item, "claim_id")!) || Text(Object(item, "signing_channel"), "claim_id") != id)
                    throw new InvalidDataException("The Odysee channel repeated an item or returned a different publisher.");
                entries.Add(Entry(item, entries.Count + 1));
            }
            if (entries.Count == total) break;
        }
        var metadata = Metadata(channel);
        return metadata with { Extractor = "lbry:channel", IsPlaylist = true, PlaylistId = id, Entries = entries, Uploader = metadata.Title };
    }

    private static async Task<PageMetadata> CollectionAsync(JsonElement collection, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        ValidateLimit(request.MaximumPlaylistEntries);
        var values = Object(collection, "value");
        if (!values.TryGetProperty("claims", out var references) || references.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The Odysee collection has no claim list.");
        if (references.GetArrayLength() > request.MaximumPlaylistEntries) throw new InvalidDataException("The Odysee collection exceeds the configured item limit.");
        var ids = references.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        if (ids.Any(id => !OdyseeAddress.IsId(id))) throw new InvalidDataException("The Odysee collection contains an invalid claim reference.");
        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var batch in ids.Select(id => id!).Distinct(StringComparer.Ordinal).Chunk(50))
        {
            ct.ThrowIfCancellationRequested();
            var result = await OdyseeRpc.CallAsync("claim_search", new { claim_ids = batch, page = 1, page_size = 50 }, http, ct).ConfigureAwait(false);
            OdyseeRpc.CheckSearch(result);
            var items = Array(result, "items");
            if (items.Length > batch.Length || Integer(result, "page") != 1 || Integer(result, "page_size") != 50 || Integer(result, "total_items") != items.Length ||
                Integer(result, "total_pages") is not (0 or 1)) throw new InvalidDataException("Odysee returned incomplete or excessive collection claim records.");
            foreach (var item in items)
            {
                ValidateClaim(item); var id = Text(item, "claim_id")!;
                if (!batch.Contains(id, StringComparer.Ordinal) || !found.TryAdd(id, item)) throw new InvalidDataException("Odysee returned an unexpected or repeated collection claim.");
            }
        }
        var entries = ids.Select((id, index) => found.TryGetValue(id!, out var item) ? Entry(item, index + 1) :
            new NativePlaylistEntry(index + 1, id, "Unavailable Odysee item", null, null, null, false)).ToArray();
        return Metadata(collection) with { Extractor = "lbry:playlist", IsPlaylist = true, PlaylistId = Text(collection, "claim_id"), Entries = entries };
    }
    private static NativePlaylistEntry Entry(JsonElement claim, int position)
    {
        var metadata = Metadata(claim);
        return new(position, metadata.Id, metadata.Title, metadata.SourcePageUrl, metadata.DurationSeconds, metadata.ThumbnailUrl, Unavailable(claim) is null);
    }
    private static void ValidateLimit(int limit) { if (limit is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(limit)); }
}
