using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.SoundCloud;

internal static class SoundCloudChecks
{
    internal static readonly Uri TrackUrl = new("https://soundcloud.com/artist/one");
    internal static readonly Uri PlaylistUrl = new("https://soundcloud.com/artist/sets/collection");
    internal const string Client = "0123456789abcdefghijklmnopqrstuv";
    // The fixtures are independently authored; no live playback addresses or tokens are stored here.
    internal static JsonNode Track(int id = 111, string slug = "one") => JsonNode.Parse($$"""
    {"id":{{id}},"kind":"track","title":"Track {{id}}","description":"Literal ]; and escaped quotes \"here\".","duration":12500,"full_duration":12500,
     "permalink_url":"https://soundcloud.com/artist/{{slug}}","streamable":true,"policy":"MONETIZE","track_authorization":"supplied+token/=",
     "artwork_url":"https://i1.sndcdn.com/artworks-fixture-large.jpg","user":{"username":"Artist"},"media":{"transcodings":[
       {"preset":"mp3_0_0","format":{"protocol":"progressive","mime_type":"audio/mpeg"},"url":"https://api-v2.soundcloud.com/media/{{id}}/direct","duration":12500,"snipped":false,"quality":"sq"},
       {"preset":"aac_160k","format":{"protocol":"hls","mime_type":"audio/mp4; codecs=\"mp4a.40.2\""},"url":"https://api-v2.soundcloud.com/media/{{id}}/hls","duration":12500,"snipped":false,"quality":"sq"}
     ] } }
    """)!;
    internal static string Page(JsonNode? item) => "<script>window.__sc_hydration=" + new JsonArray(
        new JsonObject { ["hydratable"] = "apiClient", ["data"] = new JsonObject { ["id"] = Client } },
        item is null ? null : new JsonObject { ["hydratable"] = item["kind"]!.GetValue<string>() == "playlist" ? "playlist" : "sound", ["data"] = item.DeepClone() }).ToJsonString() + ";</script>";

    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("SoundCloud native metadata retains audio formats and supplied playback parameters", Metadata),
        ("SoundCloud native MP3 transfer records its track identity and skips numeric aliases", Download),
        ("SoundCloud playlist detail batches retain positions, duplicates and unavailable tracks", Playlist),
        ("SoundCloud playlist selection fetches streams only for the selected track", PlaylistDownload),
        ("SoundCloud embedded API players and share redirects resolve native track identities", Aliases),
        ("SoundCloud previews and restricted tracks never become full downloadable media", Restricted),
        ("SoundCloud format denial retains other accessible formats", PartialFormats),
        ("SoundCloud malformed identities, page clients and endpoints fail explicitly", Invalid),
        ("SoundCloud playlist limits and changed batch identities cannot silently truncate", PlaylistLimits),
        ("SoundCloud canceled stream lookup does not publish media", Cancellation),
        ("SoundCloud packed MP3 HLS retains its audio extension with video constraints", Mp3Hls)
    ];

    private static async Task Metadata()
    {
        var requests = new List<Uri>();
        var info = await Engine(Track(), requests).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == "111" && info.Title == "Track 111" && info.DurationSeconds == 12.5 && info.Uploader == "Artist" && info.Description!.Contains("];"), "Track metadata was lost.");
        Assert(info.Formats.Count == 2 && info.Formats.All(format => format.AudioOnly && format.HasVideo == false && format.Availability == FormatAvailability.Ready) &&
            info.Formats.Single(format => format.Id == "aac_160k-hls").Bandwidth == 160000, "Audio facts or format availability changed.");
        Assert(requests.Count == 3 && requests.Skip(1).All(uri => Query(uri, "client_id") == Client && Query(uri, "track_authorization") == "supplied+token/="), "Playback values were changed or script execution was requested.");
        Assert(info.Formats[0].Url!.Query == "?signed=%2B", "The signed media URL was changed.");
        var repeated = Track(); var sources = repeated["media"]!["transcodings"]!.AsArray(); var preview = sources[0]!.DeepClone(); preview["snipped"] = true; sources.Insert(0, preview);
        var duplicates = await Engine(repeated, []).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(duplicates.Formats.Select(format => format.Id).Distinct().Count() == 3 && duplicates.Formats.Single(format => format.Id == "mp3_0_0-progressive-2").Availability == FormatAvailability.Ready,
            "A preview and full transcoding collided under one format identity.");
    }

    private static async Task Download()
    {
        using var files = new Files(); var archive = Path.Combine(files.Directory, "archive.txt"); var requests = new List<Uri>();
        var result = await Engine(Track(), requests).DownloadAsync(Request(files.Directory) with { FormatSelector = "mp3_0_0-progressive", DownloadArchiveFile = archive }, null, null, default);
        Assert(result.Succeeded && result.OutputPath!.EndsWith(".mp3") && File.ReadAllText(result.OutputPath) == "audio bytes" && File.ReadAllText(archive) == "soundcloud 111\n", "MP3 publication or archive identity failed.");
        var count = requests.Count;
        var skipped = await Engine(Track(), requests).DownloadAsync(Request(files.Directory) with { Url = new("https://api.soundcloud.com/tracks/111"), DownloadArchiveFile = archive }, null, null, default);
        Assert(skipped.Skipped && requests.Count == count, "An archived numeric track alias made HTTP requests.");
    }
    private static JsonNode Collection() => new JsonObject
    {
        ["id"] = 999, ["kind"] = "playlist", ["title"] = "Collection", ["permalink_url"] = PlaylistUrl.AbsoluteUri, ["track_count"] = 4,
        ["tracks"] = new JsonArray(Track(), new JsonObject { ["id"] = 222 }, new JsonObject { ["id"] = 333 }, new JsonObject { ["id"] = 111 })
    };
    private static async Task Playlist()
    {
        var requests = new List<Uri>();
        var info = await PlaylistEngine(Collection(), requests).InspectAsync(Request(Path.GetTempPath()) with { Url = PlaylistUrl }, default);
        Assert(info.IsPlaylist && info.Entries.Count == 4 && info.Entries.Select(item => item.Id).SequenceEqual(new[] { "111", "222", "333", "111" }) &&
            info.Entries.Select(item => item.Position).SequenceEqual(new[] { 1, 2, 3, 4 }) && !info.Entries[2].Available, "Playlist order, duplicate entries or unavailable positions changed.");
        Assert(requests.Count == 2 && Query(requests[1], "ids") == "222,333", "Playlist discovery resolved streams or fetched already available metadata.");
    }
    private static async Task PlaylistDownload()
    {
        using var files = new Files(); var requests = new List<Uri>();
        var result = await PlaylistEngine(Collection(), requests).DownloadAsync(Request(files.Directory) with { Url = PlaylistUrl, PlaylistItems = "2", FormatSelector = "mp3_0_0-progressive" }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && File.ReadAllText(result.OutputPath!) == "audio bytes" &&
            requests.Count(uri => uri.AbsolutePath == "/artist/two") == 1 && requests.All(uri => uri.AbsolutePath != "/artist/one" && !uri.AbsolutePath.StartsWith("/media/111/")), "Playlist selection downloaded or resolved unselected tracks.");
    }
    private static async Task Aliases()
    {
        foreach (var url in new[] { "https://m.soundcloud.com/artist/one", "https://api.soundcloud.com/tracks/111", "https://w.soundcloud.com/player/?url=https%3A%2F%2Fapi.soundcloud.com%2Ftracks%2F111", "https://on.soundcloud.com/fixture" })
        {
            var requests = new List<Uri>(); var engine = Engine(Track(), requests);
            var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default);
            Assert(info.Id == "111" && info.Extractor == "soundcloud", "An alias lost its native media identity.");
        }
        var embedded = new NativeEngine(httpFactory: _ => Http(request => request.RequestUri!.Host == "article.example"
            ? Response("<iframe src=\"https://w.soundcloud.com/player/?url=https%3A%2F%2Fapi.soundcloud.com%2Ftracks%2F111\"></iframe>") : Respond(request, Track())));
        Assert((await embedded.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://article.example/post") }, default)).Id == "111", "Generic HTML did not dispatch the SoundCloud embed.");
    }
    private static async Task Restricted()
    {
        foreach (var edit in new Action<JsonNode>[] { node => node["policy"] = "BLOCK", node => node["policy"] = "SNIP", node => node["streamable"] = false,
            node => node["duration"] = 1000, node => node["access"] = "preview" })
        {
            var track = Track(); edit(track); var requests = new List<Uri>();
            var info = await Engine(track, requests).InspectAsync(Request(Path.GetTempPath()), default);
            Assert(info.Formats.Count == 0 && info.Warnings.Count > 0 && requests.Count == 1, "Restricted media was resolved as a full track.");
        }
        var snippet = Track(); foreach (var source in snippet["media"]!["transcodings"]!.AsArray()) source!["snipped"] = true;
        var queried = new List<Uri>(); var preview = await Engine(snippet, queried).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(preview.Formats.All(format => format.Availability != FormatAvailability.Ready) && queried.Count == 1, "Preview transcodings were presented as complete formats.");
    }
    private static async Task PartialFormats()
    {
        var requests = new List<Uri>();
        var engine = Engine(Track(), requests, request => request.RequestUri!.AbsolutePath.EndsWith("/direct") ? new(HttpStatusCode.Forbidden) : null);
        var info = await engine.InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Formats.Single(format => format.Id == "mp3_0_0-progressive").Availability != FormatAvailability.Ready &&
            info.Formats.Single(format => format.Id == "aac_160k-hls").Availability == FormatAvailability.Ready, "One denied format removed usable alternatives.");
    }
    private static async Task Invalid()
    {
        foreach (var edit in new Action<JsonNode>[] { node => node["permalink_url"] = "https://soundcloud.com/another/track", node => node["id"] = 0,
            node => node["kind"] = "playlist", node => node["media"]!["transcodings"]![0]!["url"] = "https://other.example/media/111/direct" })
        {
            var track = Track(); edit(track);
            await Expect<InvalidDataException>(() => Engine(track, []).InspectAsync(Request(Path.GetTempPath()), default));
        }
        var badPage = new NativeEngine(httpFactory: _ => Http(_ => Response(Page(Track()).Replace(Client, "invalid"))));
        await Expect<InvalidDataException>(() => badPage.InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine(Track(), [], request => request.RequestUri!.AbsolutePath.StartsWith("/media/") ? Response("{\"url\":\"https://evil.example/media.mp3\"}") : null).InspectAsync(Request(Path.GetTempPath()), default));
        await Expect<InvalidDataException>(() => Engine(Track(222, "two"), []).InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://api.soundcloud.com/tracks/111") }, default));
        foreach (var url in new[] { "https://user@soundcloud.com/artist/one", "https://soundcloud.com:8443/artist/one", "https://w.soundcloud.com/player/?url=https%3A%2F%2Fevil.example%2Fmedia", "https://w.soundcloud.com/player/?url=a&url=b" })
            await Expect<ArgumentException>(() => Engine(Track(), []).InspectAsync(Request(Path.GetTempPath()) with { Url = new(url) }, default));
        Assert(!new SoundCloudProvider().CanHandle(new("https://soundcloud.com.evil.example/artist/one")), "A spoofed SoundCloud host was accepted.");
    }
    private static async Task PlaylistLimits()
    {
        await Expect<InvalidDataException>(() => PlaylistEngine(Collection(), []).InspectAsync(Request(Path.GetTempPath()) with { Url = PlaylistUrl, MaximumPlaylistEntries = 3 }, default));
        var changed = Collection(); changed["track_count"] = 5;
        await Expect<InvalidDataException>(() => PlaylistEngine(changed, []).InspectAsync(Request(Path.GetTempPath()) with { Url = PlaylistUrl }, default));
        await Expect<InvalidDataException>(() => PlaylistEngine(Collection(), [], new JsonArray(Track(444, "wrong"))).InspectAsync(Request(Path.GetTempPath()) with { Url = PlaylistUrl }, default));
    }
    private static async Task Cancellation()
    {
        using var files = new Files(); using var cancellation = new CancellationTokenSource();
        var engine = Engine(Track(), [], request => { if (request.RequestUri!.AbsolutePath.StartsWith("/media/")) cancellation.Cancel(); return null; });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, cancellation.Token));
        Assert(!Directory.EnumerateFiles(files.Directory).Any(), "Canceled extraction published a file.");
    }
    private static async Task Mp3Hls()
    {
        using var files = new Files(); var track = Track(); var formats = track["media"]!["transcodings"]!.AsArray(); formats.RemoveAt(1);
        formats[0]!["format"]!["protocol"] = "hls"; formats[0]!["url"] = "https://api-v2.soundcloud.com/media/111/hls";
        var result = await Engine(track, [], request => request.RequestUri!.AbsolutePath == "/playlist.m3u8" ? Response("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nhttps://cf-media.sndcdn.com/segment.mp3\n#EXT-X-ENDLIST") : null)
            .DownloadAsync(Request(files.Directory) with { MaximumHeight = 240, MaximumFrameRate = 24, VideoCodec = "h264" }, null, null, default);
        Assert(result.Succeeded && result.OutputPath!.EndsWith(".mp3") && File.ReadAllText(result.OutputPath) == "audio bytes", "Audio HLS inherited video constraints or kept the wrong extension.");
    }

    internal static NativeEngine Engine(JsonNode track, List<Uri> requests, Func<HttpRequestMessage, HttpResponseMessage?>? overrideResponse = null) =>
        new(httpFactory: _ => Http(request => { requests.Add(request.RequestUri!); return overrideResponse?.Invoke(request) ?? Respond(request, track); }));
    private static NativeEngine PlaylistEngine(JsonNode playlist, List<Uri> requests, JsonNode? batch = null) => new(httpFactory: _ => Http(request =>
    {
        requests.Add(request.RequestUri!);
        if (request.RequestUri!.AbsolutePath == "/artist/sets/collection") return Response(Page(playlist));
        if (request.RequestUri.AbsolutePath == "/tracks") return Response((batch ?? new JsonArray(Track(222, "two"))).ToJsonString());
        return Respond(request, Track(222, "two"));
    }));
    private static HttpResponseMessage Respond(HttpRequestMessage request, JsonNode track)
    {
        var url = request.RequestUri!;
        if (url.Host == "on.soundcloud.com") return new(HttpStatusCode.OK) { Content = new StringContent(Page(track)), RequestMessage = new(HttpMethod.Get, TrackUrl) };
        if (url.Host == "soundcloud.com") return Response(url.AbsolutePath == "/" ? Page(null) : Page(track));
        if (url.Host == "api-v2.soundcloud.com")
        {
            if (url.AbsolutePath.StartsWith("/tracks/")) return Response(track.ToJsonString());
            if (url.AbsolutePath.StartsWith("/media/")) return Response(JsonSerializer.Serialize(new { url = url.AbsolutePath.EndsWith("/direct") ? "https://cf-media.sndcdn.com/audio.mp3?signed=%2B" : "https://playback.media-streaming.soundcloud.cloud/playlist.m3u8" }));
        }
        return Response("audio bytes");
    }
    internal static NativeDownloadRequest Request(string folder) => new() { Url = TrackUrl, OutputDirectory = folder, FileName = "result", Retries = 0 };
    internal static HttpResponseMessage Response(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));
    private static string? Query(Uri url, string name) => url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).Where(pair => pair[0] == name).Select(pair => Uri.UnescapeDataString(pair[1])).SingleOrDefault();
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
