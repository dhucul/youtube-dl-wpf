using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.ReverbNation;

public sealed partial class ReverbNationProvider
{
    private static async Task<PageMetadata> ArtistAsync(JsonElement bootstrap, ReverbNationAddress address, string owner, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var initial = Object(bootstrap, "SONGS_WITH_PAGINATION"); var pagination = Object(initial, "pagination"); var count = Long(pagination, "result_count"); var perPage = Long(pagination, "per_page"); var pageCount = Long(pagination, "page_count");
        if (count is null or < 0 || perPage is null or < 1 or > 1000 || pageCount is null or < 1 or > 1000 || Long(pagination, "page") != 1 || pageCount != Math.Max(1, (count.Value + perPage.Value - 1) / perPage.Value))
            throw new InvalidDataException("ReverbNation did not expose a complete counted artist song inventory.");
        if (count > request.MaximumPlaylistEntries) throw new NotSupportedException("ReverbNation's complete artist inventory exceeds the configured limit.");
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); var first = Array(initial, "results").Select(Signature).ToArray();
        for (var page = 1; page <= pageCount; page++)
        {
            ct.ThrowIfCancellationRequested(); var data = page == 1 ? initial : await ApiAsync(PageUrl(owner, page, perPage!.Value), http, ct).ConfigureAwait(false); var meta = Object(data, "pagination");
            if (Long(meta, "page") != page || Long(meta, "page_count") != pageCount || Long(meta, "per_page") != perPage || Long(meta, "result_count") != count) throw new InvalidDataException("ReverbNation changed declared artist pagination.");
            var records = Array(data, "results").ToArray(); if (records.LongLength != Math.Min(perPage!.Value, count!.Value - entries.Count)) throw new InvalidDataException("ReverbNation truncated a declared artist page.");
            foreach (var song in records)
            {
                var id = Id(song); if (!seen.Add(id) || Id(Object(song, "artist")) != owner) throw new InvalidDataException("ReverbNation repeated or changed an artist recording identity.");
                var songAddress = new ReverbNationAddress(address.Artist, id); var available = Flag(song, "public", true) && !Flag(song, "streaming_restricted", true) && StreamingAccess(song);
                if (available) ValidateSong(song, songAddress, owner, requireDelivery: false);
                entries.Add(new(entries.Count + 1, id, Text(song, "name")?.Trim() ?? id, available ? songAddress.Web : null, Number(song, "duration"), Text(song, "thumbnail"), available, ExpectedArchiveIdentity: new("reverbnation", id)));
            }
        }
        if (entries.Count != count) throw new InvalidDataException("ReverbNation's artist pages did not match their declared count.");
        var final = await PageAsync(address, http, ct).ConfigureAwait(false); var finalInventory = Object(final, "SONGS_WITH_PAGINATION"); var finalPagination = Object(finalInventory, "pagination");
        if (ValidateArtist(Object(final, "ARTIST"), address) != owner || Long(finalPagination, "result_count") != count || Long(finalPagination, "page_count") != pageCount || !Array(finalInventory, "results").Select(Signature).SequenceEqual(first))
            throw new InvalidDataException("ReverbNation changed its artist inventory during complete discovery.");
        var artist = Object(bootstrap, "ARTIST"); return new() { Id = owner, Extractor = "reverbnation:artist", Title = Text(artist, "name") ?? address.Artist!, Uploader = Text(artist, "name"), SourcePageUrl = address.Web, IsPlaylist = true, PlaylistId = owner, Entries = entries, AnonymousPlayback = true };
    }
    private static Uri PageUrl(string owner, int page, long size) => new("https://legacy.reverbnation.com/api/artist/" + owner + "/songs?page=" + page + "&per_page=" + size + "&extra_fields=stats");
    private static string Signature(JsonElement song) => string.Join('\n', Id(song), Id(Object(song, "artist")), Text(song, "homepage_url"), Flag(song, "public", true).ToString(), Flag(song, "streaming_restricted", true).ToString(), Text(song, "access"), Number(song, "duration")?.ToString(System.Globalization.CultureInfo.InvariantCulture), Uri.TryCreate(Text(song, "url"), UriKind.Absolute, out var url) ? url.GetLeftPart(UriPartial.Path) : Text(song, "url"));
}
