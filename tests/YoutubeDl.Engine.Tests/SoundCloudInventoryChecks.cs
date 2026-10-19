using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.SoundCloud;

internal static class SoundCloudInventoryChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All
    {
        get
        {
            foreach (var mode in new[] { "user", "user-tracks", "user-api", "related", "search" })
                yield return ("SoundCloud inventories complete metadata-only " + mode, () => Inventory(mode));
            foreach (var mode in new[] { "foreign", "path", "query", "client", "duplicate-query", "cycle", "empty", "invalid-next", "wrong-kind", "wrong-owner", "changed-urn", "limit", "redirect", "repeated-only", "wrong-profile" })
                yield return ("SoundCloud inventories reject " + mode, () => Invalid(mode));
            foreach (var mode in new[] { "reorder", "removed", "changed-track", "renew", "renew-identity", "renew-format", "renew-denied", "renew-canceled" })
                yield return ("SoundCloud inventories selected freshness " + mode, () => Selected(mode));
            foreach (var value in new[] { "search?q=", "search/sounds?q=a&q=b", "search/sounds?q=a&filter=all", "search/users?q=a", "artist/sets", "artist/one/recommended/extra", "settings", "artist/one/s-secret" })
                yield return ("SoundCloud inventories unsupported source " + value, () => Address(value));
            yield return ("SoundCloud inventories preserve explicit empty results", Empty);
            yield return ("SoundCloud inventories accept absent terminal continuation", AbsentTerminal);
            yield return ("SoundCloud inventories cancellation stops pagination", Canceled);
        }
    }
    internal static Uri Source(string mode) => new(mode switch
    {
        "search" => "https://soundcloud.com/search/sounds?q=Native+%2B+sound",
        "related" => "https://soundcloud.com/artist/one/recommended",
        "user-api" => "https://api.soundcloud.com/users/7",
        "user-tracks" => "https://m.soundcloud.com/artist/tracks",
        _ => "https://soundcloud.com/artist"
    });
    internal static JsonNode Track(int id, string slug)
    {
        var track = SoundCloudChecks.Track(id, slug); track["user"]!["id"] = 7;
        return track;
    }
    internal static JsonNode User() => new JsonObject { ["id"] = 7, ["kind"] = "user", ["username"] = "Artist", ["permalink_url"] = "https://soundcloud.com/artist", ["track_count"] = 9 };
    internal static string Page(JsonNode? item, bool user = false) => "<script>window.__sc_hydration=" + new JsonArray(
        new JsonObject { ["hydratable"] = "apiClient", ["data"] = new JsonObject { ["id"] = SoundCloudChecks.Client } },
        item is null ? null : new JsonObject { ["hydratable"] = user ? "user" : "sound", ["data"] = item.DeepClone() }).ToJsonString() + ";</script>";
    internal static string Collection(IEnumerable<JsonNode> tracks, string? next = null, string? urn = null) => new JsonObject
    { ["collection"] = new JsonArray(tracks.Select(track => track.DeepClone()).ToArray()), ["next_href"] = next, ["query_urn"] = urn }.ToJsonString();
    internal static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> action) => AdaptiveAccountChecks.Http((message, _) => Task.FromResult(action(message)));
    internal static HttpResponseMessage Json(string text) => SoundCloudChecks.Response(text);
    internal static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static NativeDownloadRequest Request(string folder, string mode = "user") => SoundCloudChecks.Request(folder) with { Url = Source(mode), MaximumPlaylistEntries = 20 };

    private static async Task Inventory(string mode)
    {
        var calls = new List<Uri>(); var path = mode == "related" ? "/tracks/111/related" : mode == "search" ? "/search/tracks" : "/users/7/tracks";
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var uri = message.RequestUri!; calls.Add(uri);
            if (uri.Host == "soundcloud.com") return Json(Page(mode == "related" ? Track(111, "one") : mode == "search" || mode == "user-api" ? null : User(), mode != "related"));
            if (uri.AbsolutePath == "/users/7") return Json(User().ToJsonString());
            Check(uri.AbsolutePath == path && SoundCloudAddress.Query(uri)["client_id"] == SoundCloudChecks.Client, "Unexpected endpoint/client.");
            if (SoundCloudAddress.Query(uri).ContainsKey("offset"))
            {
                if (mode == "search") Check(SoundCloudAddress.Query(uri)["q"] == "Native + sound", "Search terms changed encoding.");
                var restricted = Track(333, "three"); restricted["policy"] = "BLOCK";
                return Json(Collection([Track(222, "two"), restricted], urn: "fixture-query"));
            }
            return Json(Collection([Track(111, "one"), Track(222, "two")], "https://api-v2.soundcloud.com" + path + "?offset=cursor%2Bvalue&limit=200", "fixture-query"));
        }));
        var info = await engine.InspectAsync(Request(Path.GetTempPath(), mode), default);
        Check(info.IsPlaylist && info.Entries.Select(entry => entry.Id).SequenceEqual(new[] { "111", "222", "333" }) && info.PlaylistSnapshot?.Count == 3 && !info.Entries[2].Available, "Inventory completeness/order/access was lost.");
        Check(calls.Count == (mode == "user-api" ? 4 : 3) && calls.All(uri => !uri.AbsolutePath.StartsWith("/media/")), "Inventory resolved child playback.");
    }
    private static async Task Invalid(string mode)
    {
        var page = 0; var calls = new List<Uri>();
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            var uri = message.RequestUri!; calls.Add(uri);
            if (uri.Host == "soundcloud.com")
            {
                var user = User(); if (mode == "wrong-profile") user["permalink_url"] = "https://soundcloud.com/another";
                return Json(Page(user, true));
            }
            Check(uri.Host == "api-v2.soundcloud.com" && uri.AbsolutePath == "/users/7/tracks", "An unsafe continuation was requested.");
            page++; var track = Track(page == 1 || mode == "repeated-only" ? 111 : 222, "one");
            if (mode == "wrong-owner") track["user"]!["id"] = 8;
            if (mode == "wrong-kind") track["kind"] = "playlist";
            var next = mode switch
            {
                "foreign" => "https://evil.example/users/7/tracks?offset=2",
                "path" => "https://api-v2.soundcloud.com/users/8/tracks?offset=2",
                "query" => "https://api-v2.soundcloud.com/users/7/tracks?offset=2&secret=unexpected",
                "client" => "https://api-v2.soundcloud.com/users/7/tracks?offset=2&client_id=other",
                "duplicate-query" => "https://api-v2.soundcloud.com/users/7/tracks?offset=2&offset=3",
                "cycle" => uri.AbsoluteUri,
                _ => "https://api-v2.soundcloud.com/users/7/tracks?offset=" + page
            };
            var json = Collection(mode == "empty" ? [] : [track], next, page == 1 ? "query-a" : mode == "changed-urn" ? "query-b" : "query-a");
            if (mode == "invalid-next") { var node = JsonNode.Parse(json)!; node["next_href"] = true; json = node.ToJsonString(); }
            var response = Json(json);
            if (mode == "redirect") response.RequestMessage = new(HttpMethod.Get, "https://api-v2.soundcloud.com/another");
            return response;
        }));
        await Fails(() => engine.InspectAsync(Request(Path.GetTempPath()) with { MaximumPlaylistEntries = mode == "limit" ? 1 : 20 }, default));
        Check(calls.All(uri => !uri.AbsolutePath.StartsWith("/media/")) && page <= 3, "Invalid inventory fetched media or looped.");
    }
    private static async Task Selected(string mode)
    {
        var folder = Path.Combine(Path.GetTempPath(), "soundcloud-inventory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var fresh = false; var children = 0; var media = 0; var lookups = 0; var callbacks = new List<string>(); using var cancel = new CancellationTokenSource();
            var engine = new NativeEngine(httpFactory: _ => Http(message =>
            {
                var uri = message.RequestUri!;
                if (uri.AbsolutePath == "/artist") return Json(Page(User(), true));
                if (uri.AbsolutePath == "/users/7/tracks") return Json(Collection(fresh && mode == "removed" ? [Track(111, "one")] : fresh ? [Track(222, "two"), Track(111, "one")] : [Track(111, "one"), Track(222, "two")]));
                if (uri.AbsolutePath == "/artist/two")
                {
                    children++; var track = Track(fresh && mode == "changed-track" || children > 1 && mode == "renew-identity" ? 999 : 222, "two");
                    track["media"]!["transcodings"]!.AsArray().RemoveAt(1);
                    if (children > 1 && mode == "renew-format") track["media"]!["transcodings"]![0]!["format"]!["mime_type"] = "audio/mp4";
                    return Json(Page(track));
                }
                if (uri.AbsolutePath.StartsWith("/media/")) { lookups++; return Json("{\"url\":\"https://cf-media.sndcdn.com/audio-" + children + ".mp3?signature=%2Bfresh\"}"); }
                Check(uri.Host == "cf-media.sndcdn.com", "Unselected media was resolved."); media++;
                if (mode.StartsWith("renew", StringComparison.Ordinal) && (children == 1 || mode == "renew-denied"))
                {
                    if (mode == "renew-canceled") cancel.Cancel();
                    return new(HttpStatusCode.Forbidden);
                }
                return Json("complete fixture audio");
            }));
            var output = Path.Combine(folder, "002 - result.mp3"); var archive = Path.Combine(folder, "archive.txt");
            await File.WriteAllTextAsync(output, "previous output"); await File.WriteAllTextAsync(archive, "prior 1\n");
            var request = Request(folder) with { PlaylistItems = "2", FormatId = "mp3_0_0-progressive", OutputTemplate = "%(playlist_index)03d - result.%(ext)s", DownloadArchiveFile = archive };
            var info = await engine.InspectAsync(request, default); Check(children == 0 && media == 0 && lookups == 0, "Inspection resolved playback."); fresh = true;
            request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
            NativeDownloadResult? result = null;
            var bad = mode is "removed" or "changed-track" or "renew-identity" or "renew-format" or "renew-denied" or "renew-canceled";
            try { result = await engine.DownloadAsync(request, null, null, cancel.Token, callbacks.Add); }
            catch (Exception ex) when (bad && ex is InvalidDataException or HttpRequestException or OperationCanceledException) { }
            if (bad)
            {
                Check(result?.Succeeded != true && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous output" && await File.ReadAllTextAsync(archive) == "prior 1\n", "Failed selected playback changed prior publication.");
                if (mode == "removed") Check(children == 0 && media == 0, "Removed member was opened.");
                if (mode == "changed-track") Check(media == 0, "Changed child identity downloaded media.");
                return;
            }
            Check(result!.Succeeded && children == (mode == "renew" ? 2 : 1) && media == children && await File.ReadAllTextAsync(output) == "complete fixture audio", "Selected fresh playback/renewal failed: " + result.Error + $" children={children}, media={media}, output={result.OutputPath}");
            var before = (children, media, lookups, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
            Check(repeat.Succeeded && repeat.Skipped && before == (children, media, lookups, callbacks.Count), "Archived selected playback fetched streams/media or emitted callbacks.");
        }
        finally { Directory.Delete(folder, true); }
    }
    private static Task Address(string value)
    {
        try { _ = SoundCloudAddress.Parse(new Uri("https://soundcloud.com/" + value)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Task.CompletedTask; }
        throw new Exception("An unsupported source was accepted.");
    }
    private static async Task Empty()
    {
        var engine = new NativeEngine(httpFactory: _ => Http(message => Json(message.RequestUri!.Host == "soundcloud.com" ? Page(null) : Collection([]))));
        var info = await engine.InspectAsync(Request(Path.GetTempPath(), "search"), default);
        Check(info.IsPlaylist && info.Entries.Count == 0, "Explicit empty search was refused or fabricated.");
    }
    private static async Task Canceled()
    {
        using var cancel = new CancellationTokenSource(); var calls = 0;
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            calls++; if (calls == 1) return Json(Page(User(), true));
            cancel.Cancel(); return Json(Collection([Track(111, "one")], "https://api-v2.soundcloud.com/users/7/tracks?offset=2"));
        }));
        await Fails(() => engine.InspectAsync(Request(Path.GetTempPath()), cancel.Token)); Check(calls == 2, "Cancellation requested a continuation.");
    }
    private static async Task AbsentTerminal()
    {
        var engine = new NativeEngine(httpFactory: _ => Http(message =>
        {
            if (message.RequestUri!.Host == "soundcloud.com") return Json(Page(User(), true));
            var node = JsonNode.Parse(Collection([Track(111, "one")]))!; node.AsObject().Remove("next_href"); return Json(node.ToJsonString());
        }));
        Check((await engine.InspectAsync(Request(Path.GetTempPath()), default)).Entries.Single().Id == "111", "Documented terminal pagination was refused.");
    }
    private static async Task Fails(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or OperationCanceledException) { return; }
        throw new Exception("The invalid inventory succeeded.");
    }
}
