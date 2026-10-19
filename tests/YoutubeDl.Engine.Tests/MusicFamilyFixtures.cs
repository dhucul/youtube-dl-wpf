using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.SoundCloud;

internal sealed class MusicFamilyFixture
{
    internal static readonly string[] Cases = ["jio-song", "jio-episode", "jio-album", "jio-playlist", "jio-artist", "jio-show", "qq-song", "qq-album", "qq-singer", "qq-playlist", "qq-toplist", "qq-mv", "net-song", "net-album", "net-artist", "net-playlist", "net-radio", "net-program", "net-mv"];
    internal static Uri Source(string mode) => new(mode switch
    {
        "jio-song" => "https://www.jiosaavn.com/song/one/oneToken", "jio-episode" => "https://www.jiosaavn.com/shows/one/epOne", "jio-album" => "https://www.jiosaavn.com/album/collection/albumToken", "jio-playlist" => "https://www.jiosaavn.com/featured/collection/listToken", "jio-artist" => "https://www.jiosaavn.com/artist/fixture-songs/artistToken", "jio-show" => "https://www.jiosaavn.com/shows/fixture/1/showToken",
        "qq-song" => "https://y.qq.com/n/ryqq/songDetail/trackOne", "qq-album" => "https://y.qq.com/n/ryqq/albumDetail/albumToken", "qq-singer" => "https://y.qq.com/n/ryqq/singer/singerToken", "qq-playlist" => "https://y.qq.com/n/ryqq/playlist/77", "qq-toplist" => "https://y.qq.com/n/ryqq/toplist/77", "qq-mv" => "https://y.qq.com/n/ryqq/mv/vidOne",
        _ => "https://music.163.com/" + (mode == "net-radio" ? "djradio" : mode[4..]) + "?id=" + (mode is "net-song" ? "101" : mode == "net-program" ? "201" : mode == "net-mv" ? "301" : "77")
    });
    internal readonly List<Uri> Calls = [];
    internal Func<string, JsonNode, JsonNode>? Edit;
    internal byte[]? Audio, Video;
    internal readonly Dictionary<string, byte[]> Resources = new(StringComparer.Ordinal);
    internal bool Reordered;
    internal int Grants, MediaReads, MetadataReads, Rejects;
    internal bool RejectOnce, RemoveSelected, ChangeSelected;
    internal bool ChangeOnReject;
    internal string? MediaShape;
    internal NativeEngine Engine => new(httpFactory: _ => AdaptiveAccountChecks.Http((message, ct) => Task.FromResult(Respond(message))));
    internal static bool Collection(string mode) => mode is not ("jio-song" or "jio-episode" or "qq-song" or "qq-mv" or "net-song" or "net-program" or "net-mv");
    internal static JsonNode Jio(int index = 1, bool episode = false) => new JsonObject { ["id"] = episode ? "episode" + index : "track" + index, ["title"] = "Track &amp; " + index, ["type"] = episode ? "episode" : "song",
        ["perma_url"] = "https://www.jiosaavn.com/" + (episode ? "shows/one/ep" : "song/one/") + (index == 1 ? episode ? "One" : "oneToken" : episode ? "Two" : "twoToken"), ["image"] = "https://c.saavncdn.com/image.jpg",
        ["more_info"] = new JsonObject { ["duration"] = "3", ["encrypted_media_url"] = "opaque+fixture/=", ["artistMap"] = new JsonObject { ["primary_artists"] = new JsonArray(new JsonObject { ["id"] = "77", ["name"] = "Fixture artist" }) } } };
    internal static JsonNode Qq(int index = 1) => new JsonObject { ["id"] = 100 + index, ["mid"] = index == 1 ? "trackOne" : "trackTwo", ["type"] = 0, ["title"] = "Track " + index, ["interval"] = 3,
        ["singer"] = new JsonArray(new JsonObject { ["mid"] = "singerToken", ["name"] = "Fixture artist" }), ["album"] = new JsonObject { ["mid"] = "albumToken" }, ["file"] = new JsonObject { ["media_mid"] = "media" + index } };
    internal static JsonNode Net(int index = 1) => new JsonObject { ["id"] = 100 + index, ["name"] = "Track " + index, ["duration"] = 3000, ["artists"] = new JsonArray(new JsonObject { ["id"] = 77, ["name"] = "Fixture artist" }), ["album"] = new JsonObject { ["id"] = 77, ["picUrl"] = "https://p1.music.126.net/image.jpg" } };
    internal static JsonNode Program(int index = 1) => new JsonObject { ["id"] = 200 + index, ["name"] = "Program " + index, ["duration"] = 3000, ["radio"] = new JsonObject { ["id"] = 77, ["name"] = "Fixture radio" }, ["mainSong"] = Net(index) };
    private JsonNode Changed(string operation, JsonNode value) => Edit?.Invoke(operation, value) ?? value;
    internal HttpResponseMessage Respond(HttpRequestMessage message)
    {
        var uri = message.RequestUri!; Calls.Add(uri); Check(message.Headers.Authorization is null && !message.Headers.Contains("Cookie"), "Public music requests carried account headers.");
        JsonNode value;
        if (uri.Host == "www.jiosaavn.com")
        {
            var query = SoundCloudAddress.Query(uri); var call = query["__call"];
            if (call == "song.generateAuthToken") { Grants++; value = Changed("jio-grant", new JsonObject { ["status"] = "success", ["pid"] = query["pid"], ["auth_url"] = "https://aac.saavncdn.com/audio." + (MediaShape ?? "mp3") + "?sign=" + Grants }); }
            else if (call == "song.getDetails") value = Changed("jio-batch", new JsonObject { ["songs"] = new JsonArray(query["pids"].Split(',').Where(id => id is "track1" or "track2").Select(id => Jio(id == "track1" ? 1 : 2)).ToArray()) });
            else if (call == "show.getAllEpisodes") value = Changed("jio-show-next", new JsonArray(Jio(query["p"] == "1" ? 1 : 2, true)));
            else
            {
                var kind = query["type"]; var second = query.GetValueOrDefault("token") is "twoToken" or "epTwo"; var page = query.GetValueOrDefault("p", "1");
                if (kind is "song" or "episode") { MetadataReads++; var track = Jio(second ? 2 : 1, kind == "episode"); if (ChangeSelected) track["id"] = "changed"; value = Changed("jio-track", new JsonObject { [kind == "song" ? "songs" : "episodes"] = new JsonArray(track) }); }
                else if (kind == "artist") value = Changed("jio-artist", new JsonObject { ["artistId"] = "77", ["name"] = "Fixture artist", ["urls"] = new JsonObject { ["songs"] = Source("jio-artist").AbsoluteUri }, ["topSongs"] = new JsonArray(page == "3" || RemoveSelected && page == "2" ? [] : [Jio(page == "1" ? Reordered ? 2 : 1 : Reordered ? 1 : 2)]) });
                else if (kind == "show") value = Changed("jio-show", new JsonObject { ["show_details"] = new JsonObject { ["id"] = "77", ["title"] = "Show", ["perma_url"] = Source("jio-show").AbsoluteUri, ["more_info"] = new JsonObject { ["total_episodes"] = 2, ["sort_order"] = "desc" } }, ["episodes"] = new JsonArray(Jio(1, true)) });
                else value = Changed("jio-" + kind, new JsonObject { ["id"] = "77", ["type"] = kind, ["title"] = "Collection", ["perma_url"] = Source("jio-" + kind).AbsoluteUri, ["list_count"] = RemoveSelected ? 1 : 2,
                    ["list"] = kind == "album" ? RemoveSelected ? new JsonArray(Jio()) : new JsonArray(Jio(Reordered ? 2 : 1), Jio(Reordered ? 1 : 2)) : new JsonArray(Jio(Reordered ? 2 : 1)), ["more_info"] = kind == "playlist" ? new JsonObject { ["contents"] = Reordered ? "track2,track1,missing" : "track1,track2,missing" } : new JsonObject() });
            }
        }
        else if (uri.Host == "u.y.qq.com")
        {
            var data = JsonNode.Parse(SoundCloudAddress.Query(uri)["data"])!; var call = data["req_0"]!; var method = call["method"]!.GetValue<string>(); var param = call["param"]!; JsonNode response;
            if (method == "CgiGetTrackInfo") { MetadataReads++; var second = param["mids"]?[0]?.GetValue<string>() == "trackTwo" || param["ids"]?[0]?.GetValue<int>() == 102; var track = Qq(second ? 2 : 1); if (ChangeSelected) track["id"] = 999; response = Changed("qq-track", new JsonObject { ["tracks"] = new JsonArray(track) }); }
            else if (method == "GetUrl") { Grants++; var mid = param["songmid"]![0]!.GetValue<string>(); var index = mid == "trackTwo" ? 2 : 1; response = Changed("qq-grant", new JsonObject { ["midurlinfo"] = new JsonArray(new JsonObject { ["songmid"] = mid, ["filename"] = "M500media" + index + ".mp3", ["purl"] = "M500media" + index + ".mp3?sign=" + Grants }), ["sip"] = new JsonArray("https://isure.stream.qqmusic.qq.com/") }); }
            else if (method is "GetAlbumSongList" or "GetSingerSongList") { var index = RemoveSelected ? 1 : param["begin"]!.GetValue<int>() == 0 ? Reordered ? 2 : 1 : Reordered ? 1 : 2; response = Changed(method == "GetAlbumSongList" ? "qq-album" : "qq-singer", new JsonObject { [method == "GetAlbumSongList" ? "albumMid" : "singerMid"] = method == "GetAlbumSongList" ? "albumToken" : "singerToken", ["totalNum"] = RemoveSelected ? 1 : 2, ["songList"] = new JsonArray(new JsonObject { ["songInfo"] = Qq(index) }) }); }
            else if (method == "uniform_get_Dissinfo") { var index = param["song_begin"]!.GetValue<int>() == 0 ? Reordered ? 2 : 1 : Reordered ? 1 : 2; response = Changed("qq-playlist", new JsonObject { ["code"] = 0, ["subcode"] = 0, ["dirinfo"] = new JsonObject { ["id"] = 77, ["title"] = "Playlist", ["songnum"] = 2 }, ["songlist"] = new JsonArray(Qq(index)) }); }
            else if (method == "GetDetail") { var index = param["offset"]!.GetValue<int>() == 0 ? 1 : 2; response = Changed("qq-toplist", new JsonObject { ["data"] = new JsonObject { ["topId"] = 77, ["title"] = "Chart", ["totalNum"] = 2, ["period"] = "2026-10-16", ["song"] = new JsonArray(new JsonObject { ["songId"] = 100 + index }) }, ["songInfoList"] = new JsonArray(Qq(index)) }); }
            else if (method == "get_video_info_batch") { MetadataReads++; response = Changed("qq-mv", new JsonObject { ["vidOne"] = new JsonObject { ["vid"] = "vidOne", ["name"] = "Video", ["duration"] = 3, ["code"] = 0, ["play_forbid_reason"] = 0 } }); }
            else if (method == "GetMvUrls") { Grants++; response = Changed("qq-mv-grant", new JsonObject { ["vidOne"] = new JsonObject { ["mp4"] = new JsonArray(new JsonObject { ["code"] = 0, ["filetype"] = 10, ["height"] = 72, ["freeflow_url"] = new JsonArray("https://mv.qq.com/video.mp4?sign=" + Grants) }) } }); }
            else throw new Exception("Unexpected QQ method: " + method);
            value = new JsonObject { ["code"] = 0, ["req_0"] = new JsonObject { ["code"] = 0, ["data"] = response } };
        }
        else if (uri.Host == "music.163.com")
        {
            var query = SoundCloudAddress.Query(uri); var path = uri.AbsolutePath; var second = query.GetValueOrDefault("ids", "").Contains("102", StringComparison.Ordinal);
            if (path == "/api/song/detail/") { MetadataReads++; var ids = JsonSerializer.Deserialize<long[]>(query["ids"])!; value = Changed("net-track", new JsonObject { ["code"] = 200, ["songs"] = new JsonArray(ids.Where(id => id is 101 or 102).Select(id => { var track = Net(id == 101 ? 1 : 2); if (ChangeSelected) track["id"] = 999; return track; }).ToArray()) }); }
            else if (path == "/api/song/enhance/player/url") { Grants++; value = Changed("net-grant", new JsonObject { ["code"] = 200, ["data"] = new JsonArray(new JsonObject { ["id"] = second ? 102 : 101, ["code"] = 200, ["url"] = "https://m1.music.126.net/audio.mp3?sign=" + Grants, ["br"] = 128000, ["size"] = Audio?.Length, ["type"] = "mp3" }) }); }
            else if (path == "/api/album/77") value = Changed("net-album", new JsonObject { ["code"] = 200, ["album"] = new JsonObject { ["id"] = 77, ["name"] = "Album", ["size"] = RemoveSelected ? 1 : 2, ["songs"] = RemoveSelected ? new JsonArray(Net()) : new JsonArray(Net(Reordered ? 2 : 1), Net(Reordered ? 1 : 2)) } });
            else if (path == "/api/v6/playlist/detail") value = Changed("net-playlist", new JsonObject { ["code"] = 200, ["playlist"] = new JsonObject { ["id"] = 77, ["name"] = "Playlist", ["trackCount"] = 3, ["trackIds"] = new JsonArray(new JsonObject { ["id"] = Reordered ? 102 : 101 }, new JsonObject { ["id"] = Reordered ? 101 : 102 }, new JsonObject { ["id"] = 999 }), ["tracks"] = new JsonArray(Net(Reordered ? 2 : 1)) } });
            else if (path == "/api/v1/artist/songs") { var index = query["offset"] == "0" ? Reordered ? 2 : 1 : Reordered ? 1 : 2; value = Changed("net-artist", new JsonObject { ["code"] = 200, ["total"] = 2, ["more"] = query["offset"] == "0", ["songs"] = new JsonArray(Net(index)) }); }
            else if (path == "/api/dj/program/byradio") { var index = query["offset"] == "0" ? 1 : 2; value = Changed("net-radio", new JsonObject { ["code"] = 200, ["count"] = 2, ["more"] = query["offset"] == "0", ["programs"] = new JsonArray(Program(index)) }); }
            else if (path == "/api/dj/program/detail") { MetadataReads++; value = Changed("net-program", new JsonObject { ["code"] = 200, ["program"] = Program(query["id"] == "202" ? 2 : 1) }); }
            else if (path == "/api/mv/detail") { MetadataReads++; value = Changed("net-mv", new JsonObject { ["code"] = 200, ["data"] = new JsonObject { ["id"] = 301, ["name"] = "Video", ["duration"] = 3000, ["brs"] = new JsonObject { ["72"] = "https://v1.vod.126.net/video.mp4?sign=1" } } }); }
            else throw new Exception("Unexpected NetEase endpoint: " + path);
        }
        else
        {
            MediaReads++; if (RejectOnce && Rejects++ == 0) { if (ChangeOnReject) ChangeSelected = true; return new(HttpStatusCode.Forbidden); }
            if (Resources.TryGetValue(uri.AbsolutePath, out var resource)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(resource) };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal) ? Video ?? [1, 2, 3] : Audio ?? [1, 2, 3]) };
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString()) };
    }
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
