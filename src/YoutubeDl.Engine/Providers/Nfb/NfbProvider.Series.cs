using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Nfb;

public sealed partial class NfbProvider
{
    private async Task<PageMetadata> ReadSeriesAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = NfbSeriesAddress.Parse(request.Url);
        using var session = http.WithHeaders(new Dictionary<string, string> { ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent,
            ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? request.Url.AbsoluteUri : request.Referer });
        var document = await session.GetDocumentAsync(request.Url, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        var final = NfbSeriesAddress.Parse(document.Url);
        if (final.Slug != address.Slug || final.Season != address.Season || final.Episode != address.Episode) throw new InvalidDataException("NFB redirected to another series selection.");
        var seriesId = DeclaredNumber(document.Text, "SERIES_ID");
        if (seriesId is not > 0 || DeclaredText(document.Text, "SERIES_SLUG") != address.Slug || DeclaredText(document.Text, "SERIES_TYPE") != "series" ||
            DeclaredText(document.Text, "PUBLIC_API_ENDPOINT") != "https://publicapi.nfb.ca")
            throw new NotSupportedException("NFB did not expose a supported public series declaration.");
        if (address.Season is { } requestedSeason && DeclaredNumber(document.Text, "SEASON_NUMBER") != requestedSeason)
            throw new InvalidDataException("NFB returned another selected season.");
        var endpoint = new Uri("https://publicapi.nfb.ca/api/v5/series/" + Uri.EscapeDataString(address.Slug));
        var response = await session.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("NFB redirected the public series response.");
        using var json = JsonDocument.Parse(response.Text); var item = Object(json.RootElement, "item");
        if (Long(item, "id") != seriesId || Text(item, "slug") != address.Slug || Text(item, "category") != "series") throw new InvalidDataException("NFB returned another series identity.");
        var availability = Object(item, "availability");
        if (!True(availability, "is_public")) throw new NotSupportedException("This NFB series requires additional native account support.");
        if (Text(availability, "resource_url") is not { } canonical || !Uri.TryCreate(canonical, UriKind.Absolute, out var canonicalUrl) ||
            NfbSeriesAddress.Parse(canonicalUrl) is not { Season: null, Episode: null } canonicalAddress || canonicalAddress.Slug != address.Slug)
            throw new InvalidDataException("NFB returned an inconsistent series resource address.");
        var seasons = Array(item, "seasons");
        if (seasons.Length > 1000 || Integer(item, "seasons_count") != seasons.Length || Integer(item, "episodes_count") is not >= 0 and <= 10000)
            throw new InvalidDataException("NFB returned an incomplete or excessive season inventory.");
        var seenSeasons = new HashSet<int>(); var seenIds = new HashSet<long>(); var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        var inventory = new List<(int Season, int Number, JsonElement Value, NativePlaylistEntry Entry)>();
        var imageTemplate = Text(Object(Object(json.RootElement, "meta"), "media"), "original_url");
        string? seasonTitle = null;
        foreach (var season in seasons)
        {
            var number = Integer(season, "number");
            if (number is not > 0 || !seenSeasons.Add(number.Value) || !season.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("NFB returned a repeated or malformed season.");
            if (address.Season == number) seasonTitle = Text(season, "title");
            var seenNumbers = new HashSet<int>();
            foreach (var episode in episodes.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (inventory.Count >= 10000) throw new InvalidDataException("The NFB series exceeds the absolute entry limit.");
                var id = Long(episode, "id"); var episodeNumber = Integer(episode, "number"); var slug = Text(episode, "slug");
                if (id is not > 0 || episodeNumber is not > 0 || slug is null || !Regex.IsMatch(slug, @"\A[A-Za-z0-9_-]{1,200}\z", RegexOptions.CultureInvariant) ||
                    !seenIds.Add(id.Value) || !seenSlugs.Add(slug) || !seenNumbers.Add(episodeNumber.Value)) throw new InvalidDataException("NFB omitted or repeated an episode identity.");
                if (!Array(episode, "part_of").Any(parent => Text(parent, "category") == "series" && Long(parent, "registry_id") == seriesId && Text(parent, "slug") == address.Slug))
                    throw new InvalidDataException("An NFB episode does not belong to the requested series.");
                var access = Object(episode, "availability"); var available = Text(episode, "category") == "film" && True(access, "is_public") && !Active(episode, "geoblocked") && !Active(episode, "coming_soon");
                Uri? url = null;
                if (Text(access, "resource_url") is { Length: > 0 } raw)
                {
                    if (!Uri.TryCreate(raw, UriKind.Absolute, out url)) throw new InvalidDataException("NFB returned an invalid episode address.");
                    if (Text(episode, "category") == "film")
                    { var film = NfbAddress.Parse(url); if (film.Embed || film.Slug != slug) throw new InvalidDataException("An NFB episode URL belongs to another film."); }
                    else url = null;
                }
                else available = false;
                var entry = new NativePlaylistEntry(inventory.Count + 1, id.Value.ToString(CultureInfo.InvariantCulture), WebUtility.HtmlDecode(Text(episode, "title") ?? slug), url,
                    Number(episode, "duration"), SeriesImage(Text(episode, "thumbnail"), imageTemplate), available,
                    ExpectedArchiveIdentity: new("nfb:registry", id.Value.ToString(CultureInfo.InvariantCulture)));
                inventory.Add((number.Value, episodeNumber.Value, episode, entry));
            }
        }
        if (inventory.Count != Integer(item, "episodes_count")) throw new InvalidDataException("The NFB episode count does not match its complete series inventory.");
        if (address.Season is { } seasonNumber && !seenSeasons.Contains(seasonNumber)) throw new InvalidDataException("The requested NFB season does not exist.");
        var selected = inventory.Where(episode => address.Season is null || episode.Season == address.Season).ToArray();
        if (address.Episode is { } token)
        {
            var numeric = Regex.Match(token, @"\Aepisode(?<number>[0-9]{1,6})\z", RegexOptions.CultureInvariant);
            var matches = selected.Where(episode => numeric.Success ? episode.Number == int.Parse(numeric.Groups["number"].Value, CultureInfo.InvariantCulture) : Text(episode.Value, "slug") == token).ToArray();
            if (matches.Length != 1 || DeclaredNumber(document.Text, "DISPLAYED_WORK_ID") != long.Parse(matches[0].Entry.Id!, CultureInfo.InvariantCulture) ||
                DeclaredNumber(document.Text, "EPISODE_INDEX") != matches[0].Number)
                throw new InvalidDataException("NFB returned another episode selection.");
            var selectedEntry = matches[0].Entry;
            if (!selectedEntry.Available || selectedEntry.Url is null) throw new NotSupportedException("This NFB episode is unavailable through the supported public workflow.");
            var page = await ExtractAsync(request with { Url = selectedEntry.Url, DownloadPlaylist = false, ExpandPlaylist = false }, http, ct).ConfigureAwait(false);
            if (page.ArchiveIdentity != selectedEntry.ExpectedArchiveIdentity) throw new InvalidDataException("The selected NFB film does not match its episode registry identity.");
            return page;
        }
        if (selected.Length > request.MaximumPlaylistEntries) throw new InvalidDataException("The requested NFB collection exceeds the configured limit; it was not truncated.");
        var idText = seriesId.Value.ToString(CultureInfo.InvariantCulture) + (address.Season is { } seasonId ? ":season:" + seasonId.ToString(CultureInfo.InvariantCulture) : "");
        return new() { Id = idText, PlaylistId = idText, Extractor = "nfb:series", IsPlaylist = true, Title = WebUtility.HtmlDecode(Text(item, "title") ?? address.Slug) +
            (address.Season is not null ? " - " + (seasonTitle ?? "Season " + address.Season) : ""), Description = Text(item, "description"),
            ThumbnailUrl = SeriesImage(Text(item, "thumbnail"), imageTemplate), SourcePageUrl = document.Url,
            Entries = selected.Select((episode, index) => episode.Entry with { Position = index + 1 }).ToArray() };
    }
    private static string? DeclaredText(string html, string name)
    {
        var values = Regex.Matches(html, @"\bwindow\." + Regex.Escape(name) + @"\s*=\s*['""](?<value>[^'""\r\n]*)['""]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups["value"].Value).Distinct().ToArray();
        if (values.Length > 1) throw new InvalidDataException("NFB repeated a conflicting series declaration.");
        return values.SingleOrDefault();
    }
    private static long? DeclaredNumber(string html, string name)
    {
        var values = Regex.Matches(html, @"\bwindow\." + Regex.Escape(name) + @"\s*=\s*['""]?(?<value>[0-9]+)['""]?\s*(?:;|\r?\n|</script)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => long.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1).Distinct().ToArray();
        if (values.Length > 1 || values.Length == 1 && values[0] < 0) throw new InvalidDataException("NFB returned conflicting or excessive numeric declarations.");
        return values.Length == 1 ? values[0] : null;
    }
    private static string? SeriesImage(string? path, string? template)
    {
        if (string.IsNullOrWhiteSpace(path) || template is null) return null;
        if (template != "https://assets.nfbcdn.ca/medias/norman/{path}" || path.StartsWith('/') || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("NFB returned an unsupported series image template or path.");
        return Image(template.Replace("{path}", string.Join('/', path.Split('/').Select(Uri.EscapeDataString)), StringComparison.Ordinal)).AbsoluteUri;
    }
}
