using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.JioSaavn;

internal sealed record JioSaavnAddress(string Kind, string Token, Uri Url, int? Season = null)
{
    internal bool Collection => Kind is not ("song" or "episode");
    internal static JioSaavnAddress Parse(Uri url)
    {
        if (!MusicCatalog.Host(url, "jiosaavn.com") || url.Host is not ("www.jiosaavn.com" or "jiosaavn.com") || url.Query.Length != 0) throw new ArgumentException("Use a public JioSaavn song, episode, album, artist, playlist or show-season URL.");
        var parts = url.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is < 3 or > 4 || !Regex.IsMatch(parts[^1], @"\A[A-Za-z0-9_,=-]{1,128}\z") || parts[1].Length is < 1 or > 512) throw new NotSupportedException("This JioSaavn address is not supported yet.");
        var kind = parts[0] switch { "song" => "song", "album" => "album", "artist" => "artist", "featured" => "playlist", "shows" => parts.Length == 4 ? "show" : "episode", _ => throw new NotSupportedException("This JioSaavn page type is not supported yet.") };
        int? season = null;
        if (kind == "show") season = int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 10000 ? number : throw new ArgumentException("The JioSaavn show season is invalid.");
        else if (parts.Length != 3) throw new NotSupportedException("This JioSaavn address is not supported yet.");
        return new(kind, parts[^1], new UriBuilder(url) { Scheme = "https", Host = "www.jiosaavn.com" }.Uri, season);
    }
}
public sealed class JioSaavnProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("jiosaavn", "JioSaavn public music and podcast inventories", 200);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "jiosaavn.com" or "www.jiosaavn.com";
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = JioSaavnAddress.Parse(request.Url);
        if (address.Collection) { using var anonymous = http.CreateAnonymousSession(); return await InventoryAsync(address, request, anonymous, ct).ConfigureAwait(false); }
        async Task<PageMetadata> Read(MediaHttp controlled, CancellationToken token)
        {
            using var anonymous = controlled.CreateAnonymousSession(); var root = await WebAsync(address, anonymous, token).ConfigureAwait(false);
            var rows = MusicCatalog.Rows(root, address.Kind == "song" ? "songs" : "episodes");
            if (rows.Length != 1) throw new InvalidDataException("JioSaavn returned an ambiguous track.");
            var track = rows[0]; ValidatePermalink(track, address); var page = Metadata(track); var info = Object(track, "more_info"); var encrypted = Text(info, "encrypted_media_url");
            if (await MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            if (encrypted is null || encrypted.Length is < 1 or > 8192) return page with { Warnings = ["JioSaavn did not offer playback for this track."] };
            var reply = await ApiAsync(anonymous, "song.generateAuthToken", token, ("pid", page.Id), ("url", encrypted), ("bitrate", "128")).ConfigureAwait(false);
            if (Text(reply, "pid") is { } granted && granted != page.Id) throw new InvalidDataException("JioSaavn changed its selected playback identity.");
            if (Text(reply, "status") == "error" || Text(reply, "auth_url") is not { Length: > 0 } raw) return page with { Warnings = ["JioSaavn did not grant full playback to this public session."] };
            if (MusicCatalog.Present(reply, "jwe_token") || MusicCatalog.Present(reply, "drm") || True(reply, "is_preview")) return page with { DrmRefused = true, RefusalReason = "This JioSaavn playback needs protection or preview support that is not available here." };
            var url = address.Kind == "episode" && Uri.TryCreate(raw, UriKind.Absolute, out var podcast) && podcast.Scheme == "https" && podcast.Host == "api.spreaker.com" && podcast.IsDefaultPort && podcast.UserInfo.Length == 0 && podcast.Fragment.Length == 0 && Regex.IsMatch(podcast.AbsolutePath, @"\A/download/episode/[1-9][0-9]*/[^/]+\.mp3\z")
                ? podcast : MusicCatalog.Resource(raw, "saavncdn.com", "jiosaavn.com"); var extension = MusicCatalog.AudioExtension(url);
            return page with { Media = [MusicCatalog.Audio(url, "public", extension, page.DurationSeconds!.Value, "jiosaavn")] };
        }
        return await http.SessionState("playback:jiosaavn:" + address.Token, () => new ProviderPlaybackSession(http, request, "jiosaavn", Read, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static PageMetadata Metadata(JsonElement row, bool inventory = false)
    {
        var id = MusicCatalog.Id(row); var info = Object(row, "more_info"); var duration = Number(info, "duration");
        if (!inventory && duration is not (> 0 and <= 604800) || Text(row, "type") is not ("song" or "episode")) throw new InvalidDataException("JioSaavn did not declare a complete supported track.");
        if (duration is not (> 0 and <= 604800)) duration = null;
        return new() { Id = id, Extractor = "jiosaavn", ArchiveIdentity = new("jiosaavn", id), Title = MusicCatalog.Title(row), DurationSeconds = duration,
            Uploader = string.Join(", ", Array(Object(info, "artistMap"), "primary_artists").Select(artist => MusicCatalog.Title(artist, "name"))),
            ThumbnailUrl = MusicCatalog.Image(Text(row, "image"), "saavncdn.com"), Description = Text(info, "description"), AnonymousPlayback = true };
    }
    private static void ValidatePermalink(JsonElement row, JioSaavnAddress expected)
    {
        if (!Uri.TryCreate(Text(row, "perma_url"), UriKind.Absolute, out var url)) throw new InvalidDataException("JioSaavn omitted its canonical track or collection URL.");
        var address = JioSaavnAddress.Parse(url); if (address.Kind != expected.Kind || address.Token != expected.Token || address.Season != expected.Season) throw new InvalidDataException("JioSaavn changed its selected identity.");
    }
    private static Task<JsonElement> WebAsync(JioSaavnAddress address, MediaHttp http, CancellationToken ct, int? page = null) => ApiAsync(http, "webapi.get", ct,
        ("type", address.Kind), ("token", address.Token), ("p", page?.ToString(CultureInfo.InvariantCulture)), ("n", "50"), ("n_song", "50"), ("n_album", "0"),
        ("sub_type", address.Kind == "artist" ? "songs" : null), ("more", page > 1 ? "true" : null), ("season_number", address.Season?.ToString(CultureInfo.InvariantCulture)));
    private static Task<JsonElement> ApiAsync(MediaHttp http, string call, CancellationToken ct, params (string Key, string? Value)[] args) => MusicCatalog.ReadAsync(http,
        MusicCatalog.Query("https://www.jiosaavn.com/api.php", new[] { ("__call", (string?)call), ("api_version", "4"), ("_format", "json"), ("_marker", "0"), ("ctx", "web6dot0") }.Concat(args).ToArray()), ct, call == "show.getAllEpisodes");
    private static async Task<PageMetadata> InventoryAsync(JioSaavnAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var root = await WebAsync(address, http, ct, address.Kind == "artist" ? 1 : null).ConfigureAwait(false); var entries = new List<NativePlaylistEntry>();
        JsonElement parent = address.Kind == "show" ? Object(root, "show_details") : root;
        var id = MusicCatalog.Id(parent, address.Kind == "artist" ? "artistId" : "id");
        if (address.Kind == "artist")
        {
            var urls = Object(root, "urls"); var permalink = Text(urls, "songs"); if (permalink is null) throw new InvalidDataException("JioSaavn omitted the artist's songs address.");
            ValidatePermalink(JsonSerializer.SerializeToElement(new { perma_url = permalink }), address);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 1; ; page++)
            {
                if (page > 1000 || MusicCatalog.Id(root, "artistId") != id) throw new InvalidDataException("JioSaavn changed or exceeded artist pagination.");
                var rows = MusicCatalog.Rows(root, "topSongs"); if (rows.Length > 50) throw new InvalidDataException("JioSaavn exceeded its requested artist page size."); if (rows.Length == 0) break; var before = entries.Count;
                foreach (var row in rows) { if (!seen.Add(MusicCatalog.Id(row))) continue; Add(row); }
                if (entries.Count == before) throw new InvalidDataException("JioSaavn repeated an artist page without progress.");
                root = await WebAsync(address, http, ct, page + 1).ConfigureAwait(false);
            }
        }
        else if (address.Kind == "show")
        {
            ValidatePermalink(parent, address); var total = Integer(Object(parent, "more_info"), "total_episodes"); if (total is null) throw new InvalidDataException("JioSaavn omitted the show episode count."); MusicCatalog.Limit(total.Value, request);
            var rows = await Episodes(1).ConfigureAwait(false); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 1; ; page++)
            {
                if (page > 1000) throw new InvalidDataException("JioSaavn exceeded show pagination.");
                foreach (var row in rows) { if (!seen.Add(MusicCatalog.Id(row))) throw new InvalidDataException("JioSaavn repeated an episode across pages."); Add(row); }
                if (entries.Count == total) break; if (rows.Length == 0 || entries.Count > total) throw new InvalidDataException("JioSaavn returned an incomplete show inventory.");
                rows = await Episodes(page + 1).ConfigureAwait(false);
            }
            async Task<JsonElement[]> Episodes(int page)
            {
                var next = await ApiAsync(http, "show.getAllEpisodes", ct, ("show_id", id), ("season_number", address.Season!.Value.ToString(CultureInfo.InvariantCulture)), ("sort_order", Text(Object(parent, "more_info"), "sort_order") ?? "desc"), ("p", page.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
                return next.ValueKind == JsonValueKind.Array && next.GetArrayLength() <= 50 ? next.EnumerateArray().ToArray() : MusicCatalog.Rows(next, "episodes");
            }
        }
        else
        {
            ValidatePermalink(parent, address); var rows = MusicCatalog.Rows(root, "list");
            if (address.Kind == "playlist" && Text(Object(root, "more_info"), "contents") is { } contents)
            {
                var ids = contents.Length == 0 ? [] : contents.Split(','); MusicCatalog.Limit(ids.Length, request);
                if (ids.Any(value => !Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,64}\z"))) throw new InvalidDataException("JioSaavn returned invalid playlist membership.");
                var known = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var row in rows) { var track = MusicCatalog.Id(row); if (!ids.Contains(track) || !known.TryAdd(track, row)) throw new InvalidDataException("JioSaavn returned conflicting playlist details."); }
                foreach (var batch in ids.Where(value => !known.ContainsKey(value)).Distinct().Chunk(50))
                {
                    var details = await ApiAsync(http, "song.getDetails", ct, ("pids", string.Join(',', batch))).ConfigureAwait(false);
                    foreach (var row in MusicCatalog.Rows(details, "songs")) { var track = MusicCatalog.Id(row); if (!batch.Contains(track) || !known.TryAdd(track, row)) throw new InvalidDataException("JioSaavn returned another batch identity."); }
                }
                foreach (var track in ids) { if (known.TryGetValue(track, out var row)) Add(row); else entries.Add(new(entries.Count + 1, track, "Unavailable track " + track, null, null, null, false, ExpectedArchiveIdentity: new("jiosaavn", track), CollectionMemberId: track)); }
            }
            else { var count = Integer(root, "list_count") ?? throw new InvalidDataException("JioSaavn omitted its complete album count."); if (rows.Length != count) throw new InvalidDataException("JioSaavn returned an incomplete album."); foreach (var row in rows) Add(row); }
        }
        return MusicCatalog.Inventory("jiosaavn", address.Kind + "-" + id + (address.Season is { } season ? "-" + season : ""), MusicCatalog.Title(parent, address.Kind == "artist" ? "name" : "title"), entries, address.Url);
        void Add(JsonElement row)
        {
            var metadata = Metadata(row, inventory: true);
            if (metadata.DurationSeconds is not null && address.Kind == "artist" && !new[] { "primary_artists", "featured_artists", "artists", "all" }.SelectMany(key => Array(Object(Object(row, "more_info"), "artistMap"), key)).Any(artist => MusicCatalog.Id(artist) == id))
                throw new InvalidDataException("JioSaavn returned another artist's track.");
            MusicCatalog.Limit(entries.Count + 1, request); var link = JioSaavnAddress.Parse(new Uri(Text(row, "perma_url") ?? throw new InvalidDataException("JioSaavn omitted a member URL.")));
            if (link.Collection) throw new InvalidDataException("JioSaavn returned a collection instead of a track.");
            entries.Add(MusicCatalog.Entry(metadata, entries.Count + 1, link.Url, metadata.DurationSeconds is not null));
        }
    }
}
