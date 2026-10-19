using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.IHeart;

public sealed partial class IHeartProvider
{
    private static Uri Inventory(Uri api, string id, string? cursor = null) => new(api,
        "api/v3/podcast/podcasts/" + id + "/episodes?limit=24&newEnabled=true&sortBy=startDate-desc" +
        (cursor is null ? "" : "&pageKey=" + Uri.EscapeDataString(cursor)));

    private static async Task<PageMetadata> CollectionAsync(IHeartAddress address, NativeDownloadRequest request, Uri api, JsonElement podcast, MediaHttp http, CancellationToken ct)
    {
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null; string? firstFingerprint = null; long? lastDate = null; var dataCharacters = 0;
        for (var pageNumber = 0; ; pageNumber++)
        {
            ct.ThrowIfCancellationRequested(); if (pageNumber >= 500) throw new NotSupportedException("The complete iHeart inventory exceeds its pagination bound.");
            var endpoint = Inventory(api, address.PodcastId, cursor);
            var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (response.Url != endpoint) throw new InvalidDataException("iHeart redirected its public episode inventory.");
            dataCharacters = checked(dataCharacters + response.Text.Length);
            if (dataCharacters > 64 * 1024 * 1024) throw new NotSupportedException("The complete iHeart inventory exceeds its metadata bound.");
            using var document = IHeartHydration.Parse(response.Text, ct); var root = document.RootElement;
            var items = ReadPage(root); if (pageNumber == 0) firstFingerprint = Fingerprint(items);
            var next = Next(root);
            if (items.Length == 0 && next is not null || items.Length > 24) throw new InvalidDataException("iHeart returned an empty continuing or oversized episode page.");
            foreach (var item in items)
            {
                ValidateEpisode(item, address.PodcastId); var id = Id(item);
                if (!ids.Add(id)) throw new InvalidDataException("iHeart repeated a podcast episode during pagination.");
                var start = Long(item, "startDate");
                if (start is null || lastDate is not null && start > lastDate) throw new InvalidDataException("iHeart changed the declared episode order during pagination.");
                lastDate = start;
                if (entries.Count >= request.MaximumPlaylistEntries) throw new NotSupportedException("The complete iHeart podcast exceeds the configured inventory limit; it was not truncated.");
                var available = Published(item, false) && Types(item).Any(type => type.StartsWith("audio/", StringComparison.Ordinal));
                foreach (var state in new[] { "isPrivate", "isPremium", "requiresLogin", "subscriptionRequired", "isPreview", "isLive", "drmProtected" }) if (Flag(item, state)) available = false;
                entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? id, available ? IHeartAddress.Episode(address.PodcastId, id) : null,
                    Number(item, "duration"), Poster(Text(item, "imageUrl")), available, ExpectedArchiveIdentity: new("iheartradio", id)));
            }
            if (next is null) break;
            if (!cursors.Add(next)) throw new InvalidDataException("iHeart repeated its public inventory continuation.");
            cursor = next;
        }
        using var recheck = await JsonAsync(Inventory(api, address.PodcastId), http, ct).ConfigureAwait(false);
        if (Fingerprint(ReadPage(recheck.RootElement)) != firstFingerprint) throw new InvalidDataException("iHeart changed the first public inventory page during inspection.");
        using var owner = await JsonAsync(new(api, "api/v3/podcast/podcasts/" + address.PodcastId), http, ct).ConfigureAwait(false);
        ValidatePodcast(owner.RootElement, address.PodcastId);
        if (Text(owner.RootElement, "title") != Text(podcast, "title") || Text(owner.RootElement, "slug") != Text(podcast, "slug"))
            throw new InvalidDataException("iHeart changed its public podcast identity during inspection.");
        return new() { Id = address.PodcastId, PlaylistId = address.PodcastId, IsPlaylist = true, Extractor = "iheartradio:podcast", Title = Text(podcast, "title")!,
            Description = Text(podcast, "description"), Uploader = Text(podcast, "title"), ThumbnailUrl = Poster(Text(podcast, "imageUrl")),
            Entries = entries.AsReadOnly(), SourcePageUrl = address.Web, AnonymousPlayback = true };
    }
    private static JsonElement[] ReadPage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || Object(root, "links").ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("iHeart omitted a public episode page or its pagination state.");
        return data.EnumerateArray().ToArray();
    }
    private static string? Next(JsonElement root)
    {
        var links = Object(root, "links"); if (!links.TryGetProperty("next", out var next) || next.ValueKind == JsonValueKind.Null) return null;
        if (next.ValueKind != JsonValueKind.String || next.GetString() is not { Length: > 0 and <= 4096 } value || value.Any(char.IsControl))
            throw new InvalidDataException("iHeart returned an invalid opaque public inventory continuation.");
        return next.GetString();
    }
    private static string Fingerprint(IEnumerable<JsonElement> items) => JsonSerializer.Serialize(items.Select(item => new
    {
        Id = Id(item), Podcast = Id(item, "podcastId"), Start = Long(item, "startDate"), End = Long(item, "endDate"), Title = Text(item, "title"),
        Duration = Number(item, "duration"), Types = Types(item), Private = Flag(item, "isPrivate"), Premium = Flag(item, "isPremium"),
        Login = Flag(item, "requiresLogin"), Subscription = Flag(item, "subscriptionRequired"), Preview = Flag(item, "isPreview"), Live = Flag(item, "isLive"), Protected = Flag(item, "drmProtected")
    }));
}
