using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.QqMusic;

internal sealed record QqMusicAddress(string Kind, string Id)
{
    internal bool Collection => Kind is "album" or "singer" or "playlist" or "toplist";
    internal string Archive => Kind == "mv" ? "qqmusic:mv" : "qqmusic";
    internal Uri Url => new("https://y.qq.com/n/ryqq/" + (Kind == "song" ? "songDetail" : Kind == "album" ? "albumDetail" : Kind) + "/" + Id);
    internal static QqMusicAddress Parse(Uri url)
    {
        if (!MusicCatalog.Host(url, "y.qq.com") || url.Host != "y.qq.com" || url.Query.Length != 0) throw new ArgumentException("Use a QQ Music song, album, singer, playlist, chart or video URL.");
        var match = Regex.Match(url.AbsolutePath, @"\A/n/(?:ryqq(?:_v2)?/(?<kind>songDetail|albumDetail|singer|playlist|toplist|mv)/(?<id>[A-Za-z0-9]{1,32})/?|yqq/(?<kind>song|album|singer|mv/v)/(?<id>[A-Za-z0-9]{1,32})\.html)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NotSupportedException("This QQ Music URL is not supported yet.");
        var kind = match.Groups["kind"].Value switch { "songDetail" => "song", "albumDetail" => "album", "mv/v" => "mv", var value => value }; var id = match.Groups["id"].Value;
        if (kind is "playlist" or "toplist" && (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)) throw new ArgumentException("QQ Music requires a positive collection ID.");
        return new(kind, id);
    }
}
public sealed class QqMusicProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("qqmusic", "QQ Music public tracks, charts and collections", 200);
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host == "y.qq.com";
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var source = QqMusicAddress.Parse(request.Url); return source.Kind == "mv" || source.Kind == "song" && long.TryParse(source.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? new(source.Archive, source.Id) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = QqMusicAddress.Parse(request.Url);
        if (address.Collection) { using var anonymous = http.CreateAnonymousSession(); return await InventoryAsync(address, request, anonymous, ct).ConfigureAwait(false); }
        var guid = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        async Task<PageMetadata> Read(MediaHttp controlled, CancellationToken token)
        {
            using var anonymous = controlled.CreateAnonymousSession(); if (address.Kind == "mv") return await VideoAsync(address, anonymous, token).ConfigureAwait(false);
            var numeric = long.TryParse(address.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
            var detail = await CallAsync(anonymous, "music.trackInfo.UniformRuleCtrl", "CgiGetTrackInfo", numeric ? new { ids = new[] { id }, types = new[] { 0 } } : (object)new { mids = new[] { address.Id }, types = new[] { 0 } }, token).ConfigureAwait(false);
            var tracks = MusicCatalog.Rows(detail, "tracks");
            if (tracks.Length != 1 || (numeric ? MusicCatalog.Numeric(tracks[0]) != address.Id : Text(tracks[0], "mid") != address.Id)) throw new InvalidDataException("QQ Music returned another track identity.");
            var track = tracks[0]; var page = Metadata(track); var mid = Mid(track); if (Integer(track, "type") is not (null or 0)) throw new NotSupportedException("This QQ Music track type is not supported yet.");
            if (await MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            JsonElement grant;
            try { grant = await CallAsync(anonymous, "music.vkey.GetEVkey", "GetUrl", new { guid, songmid = new[] { mid }, songtype = new[] { 0 }, uin = "0", loginflag = 0, platform = "20", xcdn = 1 }, token).ConfigureAwait(false); }
            catch (NotSupportedException) { return page with { Warnings = ["QQ Music did not grant full playback to this public session."] }; }
            var rows = MusicCatalog.Rows(grant, "midurlinfo"); if (rows.Length != 1 || Text(rows[0], "songmid") != mid) throw new InvalidDataException("QQ Music changed the playback track identity.");
            var row = rows[0]; var purl = Text(row, "purl"); var filename = Text(row, "filename") ?? "";
            if (string.IsNullOrEmpty(purl) || filename.StartsWith("RS", StringComparison.Ordinal) || True(row, "is_preview")) return page with { Warnings = ["QQ Music offered no complete clear track."] };
            var fileMid = Text(Object(track, "file"), "media_mid");
            if (fileMid is null || !Regex.IsMatch(filename, @"\A(?:M[0-9]{3}|C[0-9]{3}|F[0-9]{3}|A[0-9]{3}|O[0-9]{3})" + Regex.Escape(fileMid) + @"\.(?:mp3|m4a|flac|ape|ogg)\z")) throw new InvalidDataException("QQ Music's audio grant does not belong to the declared track file.");
            var origins = MusicCatalog.Rows(grant, "sip"); Uri? url = null;
            foreach (var origin in origins)
            {
                var baseUrl = MusicCatalog.Resource(origin.GetString(), "qqmusic.qq.com"); if (baseUrl.Scheme != "https") continue;
                if (!Uri.TryCreate(baseUrl, purl, out var candidate) || !MusicCatalog.Host(candidate, "qqmusic.qq.com") || candidate.Scheme != "https" || Path.GetFileName(candidate.AbsolutePath) != filename) throw new InvalidDataException("QQ Music returned a mismatched audio resource.");
                url = candidate; break;
            }
            if (url is null) return page with { Warnings = ["QQ Music did not offer a supported secure media origin."] };
            var extension = MusicCatalog.AudioExtension(url);
            return page with { Media = [MusicCatalog.Audio(url, "public", extension, page.DurationSeconds!.Value, "qqmusic")] };
        }
        return await http.SessionState("playback:qqmusic:" + address.Kind + ":" + address.Id, () => new ProviderPlaybackSession(http, request, "qqmusic", Read, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static async Task<JsonElement> CallAsync(MediaHttp http, string module, string method, object param, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { comm = new { ct = 24, cv = 0, format = "json", uin = 0 }, req_0 = new { module, method, param } });
        var root = await MusicCatalog.ReadAsync(http, MusicCatalog.Query("https://u.y.qq.com/cgi-bin/musicu.fcg", ("data", body)), ct).ConfigureAwait(false);
        var reply = Object(root, "req_0"); if (Integer(root, "code") != 0 || Integer(reply, "code") != 0) throw new NotSupportedException("QQ Music did not make this public resource available.");
        var data = Object(reply, "data"); if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("QQ Music omitted its response data."); return data;
    }
    private static string Mid(JsonElement track) => Text(track, "mid") is { } mid && Regex.IsMatch(mid, @"\A[A-Za-z0-9]{1,32}\z") ? mid : throw new InvalidDataException("QQ Music omitted its canonical track MID.");
    private static PageMetadata Metadata(JsonElement track, bool inventory = false)
    {
        var id = MusicCatalog.Numeric(track); _ = Mid(track); var duration = Number(track, "interval"); if (!inventory && duration is not (> 0 and <= 604800)) throw new InvalidDataException("QQ Music omitted a complete track duration.");
        if (duration is not (> 0 and <= 604800)) duration = null;
        return new() { Id = id, ArchiveIdentity = new("qqmusic", id), Extractor = "qqmusic", Title = MusicCatalog.Title(track), DurationSeconds = duration,
            Uploader = string.Join(", ", Array(track, "singer").Select(singer => MusicCatalog.Title(singer, "name"))), AnonymousPlayback = true };
    }
    private static async Task<PageMetadata> InventoryAsync(QqMusicAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal); int? total = null; string? period = null; var title = "QQ Music " + address.Kind + " " + address.Id;
        for (var page = 0; ; page++)
        {
            if (page >= 1000) throw new InvalidDataException("QQ Music exceeded bounded collection pagination.");
            JsonElement root; JsonElement[] songs; int count;
            if (address.Kind is "album" or "singer")
            {
                root = await CallAsync(http, address.Kind == "album" ? "music.musichallAlbum.AlbumSongList" : "musichall.song_list_server", address.Kind == "album" ? "GetAlbumSongList" : "GetSingerSongList",
                    address.Kind == "album" ? new { albumMid = address.Id, begin = entries.Count, num = 100, order = 2 } : (object)new { singerMid = address.Id, begin = entries.Count, num = 100, order = 1 }, ct).ConfigureAwait(false);
                if (Text(root, address.Kind == "album" ? "albumMid" : "singerMid") != address.Id) throw new InvalidDataException("QQ Music changed its album or singer identity.");
                count = Integer(root, "totalNum") ?? throw new InvalidDataException("QQ Music omitted its collection count."); songs = MusicCatalog.Rows(root, "songList").Select(row => Object(row, "songInfo")).ToArray();
            }
            else if (address.Kind == "playlist")
            {
                root = await CallAsync(http, "music.srfDissInfo.aiDissInfo", "uniform_get_Dissinfo", new { disstid = long.Parse(address.Id, CultureInfo.InvariantCulture), userinfo = 1, tag = 1, orderlist = 1, song_begin = entries.Count, song_num = 100, onlysonglist = 0, enc_host_uin = "" }, ct).ConfigureAwait(false);
                var parent = Object(root, "dirinfo"); if (MusicCatalog.Numeric(parent) != address.Id || Integer(root, "code") != 0 || Integer(root, "subcode") != 0) throw new InvalidDataException("QQ Music changed or denied its playlist identity.");
                title = MusicCatalog.Title(parent); count = Integer(parent, "songnum") ?? throw new InvalidDataException("QQ Music omitted complete playlist membership."); songs = MusicCatalog.Rows(root, "songlist");
            }
            else
            {
                root = await CallAsync(http, "musicToplist.ToplistInfoServer", "GetDetail", new { topid = long.Parse(address.Id, CultureInfo.InvariantCulture), offset = entries.Count, num = 100, period = period ?? "" }, ct).ConfigureAwait(false);
                var parent = Object(root, "data"); if (MusicCatalog.Numeric(parent, "topId") != address.Id || Text(parent, "period") is not { Length: > 0 and <= 64 } current || current.Any(char.IsControl) || period is not null && current != period) throw new InvalidDataException("QQ Music changed its chart identity or period.");
                period = current; title = MusicCatalog.Title(parent); count = Integer(parent, "totalNum") ?? throw new InvalidDataException("QQ Music omitted the chart count."); songs = MusicCatalog.Rows(root, "songInfoList");
                var ranks = MusicCatalog.Rows(parent, "song"); if (ranks.Length != songs.Length || !ranks.Select(row => MusicCatalog.Numeric(row, "songId")).SequenceEqual(songs.Select(row => MusicCatalog.Numeric(row)))) throw new InvalidDataException("QQ Music's chart ranks do not match its track details.");
            }
            MusicCatalog.Limit(count, request); if (total is not null && count != total) throw new InvalidDataException("QQ Music changed its complete collection count."); total = count;
            if (songs.Length > 100 || songs.Length == 0 && entries.Count < count) throw new InvalidDataException("QQ Music collection pagination made no progress.");
            foreach (var song in songs)
            {
                var metadata = Metadata(song, inventory: true); var mid = Mid(song);
                if (address.Kind != "playlist" && !seen.Add(metadata.Id!)) throw new InvalidDataException("QQ Music repeated a collection member across pages.");
                if (address.Kind == "album" && Text(Object(song, "album"), "mid") != address.Id || address.Kind == "singer" && !Array(song, "singer").Any(artist => Text(artist, "mid") == address.Id)) throw new InvalidDataException("QQ Music returned another album or singer's track.");
                entries.Add(MusicCatalog.Entry(metadata, entries.Count + 1, new QqMusicAddress("song", mid).Url, metadata.DurationSeconds is not null));
            }
            if (entries.Count == count) break; if (entries.Count > count) throw new InvalidDataException("QQ Music exceeded its declared collection count.");
        }
        return MusicCatalog.Inventory("qqmusic", address.Kind + "-" + address.Id + (period is null ? "" : "-" + period), title, entries, address.Url);
    }
    private static async Task<PageMetadata> VideoAsync(QqMusicAddress address, MediaHttp http, CancellationToken ct)
    {
        var metadata = await CallAsync(http, "music.video.VideoData", "get_video_info_batch", new { vidlist = new[] { address.Id }, required = new[] { "vid", "name", "desc", "duration", "singers", "code", "video_pay", "play_forbid_reason" } }, ct).ConfigureAwait(false);
        var item = Object(metadata, address.Id); if (Text(item, "vid") != address.Id) throw new InvalidDataException("QQ Music returned another video identity.");
        var page = new PageMetadata { Id = address.Id, Extractor = "qqmusic:mv", ArchiveIdentity = new("qqmusic:mv", address.Id), Title = MusicCatalog.Title(item, "name"), DurationSeconds = Number(item, "duration"), Description = Text(item, "desc"), AnonymousPlayback = true };
        if (Integer(item, "code") is > 0 || True(item, "video_pay") || Integer(item, "play_forbid_reason") is > 0 || Text(item, "play_forbid_reason") is { Length: > 0 }) return page with { Warnings = ["QQ Music did not grant complete public video playback."] };
        var reply = await CallAsync(http, "music.stream.MvUrlProxy", "GetMvUrls", new { vids = new[] { address.Id }, request_type = 10003, addrtype = 3, format = 264, maxFiletype = 60 }, ct).ConfigureAwait(false);
        var offered = Object(reply, address.Id); var media = new List<MediaCandidate>();
        foreach (var format in MusicCatalog.Rows(offered, "mp4"))
        {
            if (True(format, "is_preview") || True(format, "drm") || Integer(format, "code") is > 0) continue;
            if (page.DurationSeconds is not (> 0 and <= 604800)) throw new InvalidDataException("QQ Music omitted a complete video duration.");
            var urls = MusicCatalog.Rows(format, "freeflow_url"); if (urls.Length == 0 && format.TryGetProperty("url", out _)) urls = MusicCatalog.Rows(format, "url");
            if (urls.Length == 0) continue; if (urls.Length > 16) throw new InvalidDataException("QQ Music returned an invalid video URL inventory.");
            var url = MusicCatalog.Resource(urls[0].GetString(), "qq.com", "qqmusic.qq.com");
            media.Add(new() { Id = (Integer(format, "filetype") ?? throw new InvalidDataException("QQ Music omitted a video format ID.")).ToString(CultureInfo.InvariantCulture), Url = url, Extension = "mp4", Kind = MediaKind.Progressive, Source = "qqmusic", HasAudio = true, HasVideo = true,
                Height = Integer(format, "height"), VerificationDemuxer = "mov", VerificationMinimumDurationSeconds = page.DurationSeconds, VerificationMinimumDurationToleranceSeconds = 1.1 });
        }
        return page with { Media = media, Warnings = media.Count == 0 ? ["QQ Music did not offer complete clear video resources."] : [] };
    }
}
