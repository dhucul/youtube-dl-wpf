using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.NetEase;

internal sealed record NetEaseAddress(string Kind, string Id)
{
    internal bool Collection => Kind is "album" or "artist" or "playlist" or "djradio";
    internal string Archive => Kind == "mv" ? "netease:mv" : Kind == "program" ? "netease:program" : "netease";
    internal Uri Url => new("https://music.163.com/" + Kind + "?id=" + Id);
    internal static NetEaseAddress Parse(Uri url)
    {
        if (!MusicCatalog.Host(url, "music.163.com") || url.Host is not ("music.163.com" or "www.music.163.com")) throw new ArgumentException("Use a NetEase song, album, artist, playlist, video, program or radio URL.");
        if (url.Fragment.Length != 0) throw new ArgumentException("Normalize the NetEase address to its direct page URL.");
        var kind = url.AbsolutePath.Trim('/'); var query = SoundCloud.SoundCloudAddress.Query(url);
        if (kind is not ("song" or "album" or "artist" or "playlist" or "mv" or "program" or "djradio") || query.Count != 1 || !query.TryGetValue("id", out var id) ||
            !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) || numeric <= 0 || id != numeric.ToString(CultureInfo.InvariantCulture)) throw new NotSupportedException("This NetEase address is not supported yet.");
        return new(kind, id);
    }
}
public sealed class NetEaseProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("netease", "NetEase public music, radio and video inventories", 200);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host is "music.163.com" or "www.music.163.com";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = Parse(request.Url); return address.Collection ? null : new(address.Archive, address.Id); }
    private static NetEaseAddress Parse(Uri url) => url.Fragment.StartsWith("#/", StringComparison.Ordinal) && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 && url.IsDefaultPort && url.Host is "music.163.com" or "www.music.163.com"
        ? NetEaseAddress.Parse(new Uri("https://music.163.com" + url.Fragment[1..])) : NetEaseAddress.Parse(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = Parse(request.Url);
        if (address.Collection) { using var anonymous = http.CreateAnonymousSession(); return await InventoryAsync(address, request, anonymous, ct).ConfigureAwait(false); }
        async Task<PageMetadata> Read(MediaHttp controlled, CancellationToken token)
        {
            using var anonymous = controlled.CreateAnonymousSession();
            if (address.Kind == "mv") return await VideoAsync(address, anonymous, token).ConfigureAwait(false);
            JsonElement track; PageMetadata metadata;
            if (address.Kind == "program")
            {
                var reply = await ApiAsync(anonymous, "/api/dj/program/detail", token, ("id", address.Id)).ConfigureAwait(false); var program = Object(reply, "program");
                if (MusicCatalog.Numeric(program) != address.Id) throw new InvalidDataException("NetEase returned another program identity.");
                track = Object(program, "mainSong"); metadata = Program(program);
            }
            else
            {
                var reply = await ApiAsync(anonymous, "/api/song/detail/", token, ("ids", "[" + address.Id + "]")).ConfigureAwait(false); var rows = MusicCatalog.Rows(reply, "songs");
                if (rows.Length != 1 || MusicCatalog.Numeric(rows[0]) != address.Id) throw new InvalidDataException("NetEase returned another song identity."); track = rows[0]; metadata = Song(track);
            }
            var songId = MusicCatalog.Numeric(track); var playback = await ApiAsync(anonymous, "/api/song/enhance/player/url", token, ("ids", "[" + songId + "]"), ("br", "320000")).ConfigureAwait(false);
            var streams = MusicCatalog.Rows(playback, "data"); if (streams.Length != 1 || MusicCatalog.Numeric(streams[0]) != songId) throw new InvalidDataException("NetEase changed the selected playback identity.");
            var stream = streams[0];
            if (Integer(stream, "code") != 200 || Text(stream, "url") is not { Length: > 0 } raw || MusicCatalog.Present(stream, "freeTrialInfo") || True(stream, "isPreview"))
                return metadata with { Warnings = ["NetEase did not grant complete playback to this public session."] };
            if (Text(stream, "type") is "mgg" or "ncm" || True(stream, "drm")) return metadata with { DrmRefused = true, RefusalReason = "This NetEase audio encoding is protected." };
            var url = MusicCatalog.Resource(raw, "126.net", "163.com", "netease.com"); var extension = MusicCatalog.AudioExtension(url, Text(stream, "type") is "mp3" ? "audio/mpeg" : null);
            return metadata with { Media = [MusicCatalog.Audio(url, "audio-" + songId, extension, metadata.DurationSeconds!.Value, "netease", Long(stream, "size") is > 0 and var size ? size : null, Number(stream, "br"))] };
        }
        return await http.SessionState("playback:netease:" + address.Kind + ":" + address.Id, () => new ProviderPlaybackSession(http, request, "netease", Read, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<JsonElement> ApiAsync(MediaHttp http, string path, CancellationToken ct, params (string Key, string? Value)[] query)
    {
        var root = await MusicCatalog.ReadAsync(http, MusicCatalog.Query("https://music.163.com" + path, query), ct).ConfigureAwait(false);
        if (Integer(root, "code") != 200) throw new NotSupportedException("NetEase did not make this public resource available."); return root;
    }
    private static PageMetadata Song(JsonElement row)
    {
        var id = MusicCatalog.Numeric(row); var duration = (Number(row, "duration") ?? Number(row, "dt")) / 1000;
        if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("NetEase omitted a complete track duration.");
        var artists = Array(row, "artists"); if (artists.Length == 0) artists = Array(row, "ar"); var album = Object(row, "album"); if (album.ValueKind == JsonValueKind.Undefined) album = Object(row, "al");
        return new() { Id = id, Extractor = "netease", ArchiveIdentity = new("netease", id), Title = MusicCatalog.Title(row, "name"), DurationSeconds = duration,
            Uploader = string.Join(", ", artists.Select(item => MusicCatalog.Title(item, "name"))), ThumbnailUrl = MusicCatalog.Image(Text(album, "picUrl"), "126.net", "163.com", "netease.com"), AnonymousPlayback = true };
    }
    private static PageMetadata Program(JsonElement row)
    {
        var id = MusicCatalog.Numeric(row); var song = Song(Object(row, "mainSong"));
        return song with { Id = id, Extractor = "netease:program", ArchiveIdentity = new("netease:program", id), Title = MusicCatalog.Title(row, "name"), Description = Text(row, "description") };
    }
    private static async Task<PageMetadata> VideoAsync(NetEaseAddress address, MediaHttp http, CancellationToken ct)
    {
        var reply = await ApiAsync(http, "/api/mv/detail", ct, ("id", address.Id), ("type", "mp4")).ConfigureAwait(false); var video = Object(reply, "data");
        if (MusicCatalog.Numeric(video) != address.Id) throw new InvalidDataException("NetEase returned another video identity.");
        var duration = Number(video, "duration") / 1000; var media = new List<MediaCandidate>(); var formats = Object(video, "brs");
        if (formats.ValueKind != JsonValueKind.Object || formats.EnumerateObject().Count() > 32) throw new InvalidDataException("NetEase returned an invalid video inventory.");
        foreach (var item in formats.EnumerateObject())
        {
            var url = MusicCatalog.Resource(item.Value.GetString(), "126.net", "163.com", "netease.com");
            if (duration is not (> 0 and <= 604800)) throw new InvalidDataException("NetEase omitted a complete video duration.");
            if (!int.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height is < 1 or > 4320) throw new InvalidDataException("NetEase returned invalid video format facts.");
            media.Add(new() { Id = item.Name, Url = url, Kind = MediaKind.Progressive, Extension = "mp4", Source = "netease", HasVideo = true, HasAudio = true, Height = height, VerificationDemuxer = "mov", VerificationMinimumDurationSeconds = duration, VerificationMinimumDurationToleranceSeconds = 1.1 });
        }
        return new() { Id = address.Id, Extractor = "netease:mv", ArchiveIdentity = new("netease:mv", address.Id), Title = MusicCatalog.Title(video, "name"), Uploader = Text(video, "artistName"), DurationSeconds = duration,
            ThumbnailUrl = MusicCatalog.Image(Text(video, "cover"), "126.net", "163.com", "netease.com"), Description = Text(video, "desc"), Media = media, AnonymousPlayback = true,
            Warnings = media.Count == 0 ? ["NetEase did not offer complete clear video playback."] : [] };
    }
    private static async Task<PageMetadata> InventoryAsync(NetEaseAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var entries = new List<NativePlaylistEntry>(); string title;
        if (address.Kind == "album")
        {
            var root = await ApiAsync(http, "/api/album/" + address.Id, ct).ConfigureAwait(false); var album = Object(root, "album"); if (MusicCatalog.Numeric(album) != address.Id) throw new InvalidDataException("NetEase changed its album identity.");
            var rows = MusicCatalog.Rows(album, "songs"); if (Integer(album, "size") != rows.Length) throw new InvalidDataException("NetEase returned an incomplete album."); foreach (var row in rows) AddSong(row); title = MusicCatalog.Title(album, "name");
        }
        else if (address.Kind == "playlist")
        {
            var root = await ApiAsync(http, "/api/v6/playlist/detail", ct, ("id", address.Id), ("n", "1000")).ConfigureAwait(false); var playlist = Object(root, "playlist"); if (MusicCatalog.Numeric(playlist) != address.Id) throw new InvalidDataException("NetEase changed its playlist identity.");
            var ids = MusicCatalog.Rows(playlist, "trackIds").Select(row => MusicCatalog.Numeric(row)).ToArray(); MusicCatalog.Limit(ids.Length, request);
            if (Integer(playlist, "trackCount") != ids.Length) throw new InvalidDataException("NetEase omitted complete playlist membership.");
            var tracks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var row in MusicCatalog.Rows(playlist, "tracks")) { var id = MusicCatalog.Numeric(row); if (!ids.Contains(id) || !tracks.TryAdd(id, row)) throw new InvalidDataException("NetEase returned conflicting playlist details."); }
            foreach (var batch in ids.Where(id => !tracks.ContainsKey(id)).Distinct().Chunk(100))
            {
                var data = await ApiAsync(http, "/api/song/detail/", ct, ("ids", "[" + string.Join(',', batch) + "]")).ConfigureAwait(false);
                foreach (var row in MusicCatalog.Rows(data, "songs")) { var id = MusicCatalog.Numeric(row); if (!batch.Contains(id) || !tracks.TryAdd(id, row)) throw new InvalidDataException("NetEase changed a playlist detail identity."); }
            }
            foreach (var id in ids) { if (tracks.TryGetValue(id, out var row)) AddSong(row); else entries.Add(new(entries.Count + 1, id, "Unavailable track " + id, null, null, null, false, ExpectedArchiveIdentity: new("netease", id), CollectionMemberId: id)); }
            title = MusicCatalog.Title(playlist, "name");
        }
        else
        {
            var radio = address.Kind == "djradio"; var seen = new HashSet<string>(StringComparer.Ordinal); int? total = null; title = "NetEase " + address.Kind + " " + address.Id;
            for (var page = 0; ; page++)
            {
                if (page >= 1000) throw new InvalidDataException("NetEase exceeded bounded collection paging.");
                var root = await ApiAsync(http, radio ? "/api/dj/program/byradio" : "/api/v1/artist/songs", ct, (radio ? "radioId" : "id", address.Id), ("offset", entries.Count.ToString(CultureInfo.InvariantCulture)), ("limit", "100"), (radio ? "asc" : "order", radio ? "false" : "hot")).ConfigureAwait(false);
                var count = Integer(root, radio ? "count" : "total") ?? throw new InvalidDataException("NetEase omitted a complete collection count."); MusicCatalog.Limit(count, request); if (total is not null && total != count) throw new InvalidDataException("NetEase changed its collection count."); total = count;
                var rows = MusicCatalog.Rows(root, radio ? "programs" : "songs"); if (rows.Length > 100 || rows.Length == 0 && entries.Count < count) throw new InvalidDataException("NetEase collection pagination made no progress.");
                foreach (var row in rows)
                {
                    if (!seen.Add(MusicCatalog.Numeric(row))) throw new InvalidDataException("NetEase repeated a collection member across pages.");
                    if (radio)
                    {
                        var parent = Object(row, "radio"); if (MusicCatalog.Numeric(parent) != address.Id) throw new InvalidDataException("NetEase returned another radio's program.");
                        title = MusicCatalog.Title(parent, "name"); entries.Add(MusicCatalog.Entry(Program(row), entries.Count + 1, new NetEaseAddress("program", MusicCatalog.Numeric(row)).Url));
                    }
                    else { var artists = Array(row, "ar"); if (artists.Length == 0) artists = Array(row, "artists"); if (!artists.Any(item => Long(item, "id")?.ToString(CultureInfo.InvariantCulture) == address.Id)) throw new InvalidDataException("NetEase returned another artist's track."); AddSong(row); }
                }
                if (!root.TryGetProperty("more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("NetEase omitted its paging continuation state.");
                if (more.ValueKind == JsonValueKind.False) { if (entries.Count != count) throw new InvalidDataException("NetEase ended an incomplete collection."); break; }
                if (entries.Count >= count) throw new InvalidDataException("NetEase continued beyond its declared count.");
            }
        }
        return MusicCatalog.Inventory("netease", address.Kind + "-" + address.Id, title, entries, address.Url);
        void AddSong(JsonElement row) { MusicCatalog.Limit(entries.Count + 1, request); var metadata = Song(row); entries.Add(MusicCatalog.Entry(metadata, entries.Count + 1, new NetEaseAddress("song", metadata.Id!).Url)); }
    }
}
