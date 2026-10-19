using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Panopto;

internal static class PanoptoCollectionChecks
{
    internal const string FolderId = "66666666-6666-4666-8666-666666666666";
    internal const string PlaylistId = "55555555-5555-4555-8555-555555555555";
    internal const string ListId = "77777777-7777-4777-8777-777777777777";
    internal static string Id(int number) => number == 1 ? PanoptoChecks.DeliveryId : $"00000000-0000-4000-8000-{number:000000000000}";
    internal static NativeDownloadRequest Folder(string directory) => PanoptoChecks.Request(directory) with { Url = new("https://campus.example/Panopto/Pages/Sessions/List.aspx#folderID=%22" + FolderId + "%22&maxResults=2") };
    internal static NativeDownloadRequest Playlist(string directory) => PanoptoChecks.Request(directory) with { Url = new("https://campus.example/Panopto/Pages/Viewer.aspx?pid=" + PlaylistId) };
    private static JsonNode FolderInfo(bool ordered = false) => new JsonObject { ["d"] = new JsonObject { ["ID"] = FolderId, ["Name"] = "Public folder", ["DeliveriesHaveSpecifiedOrder"] = ordered } };
    internal static JsonNode FolderPage(int page, int total = 6) => new JsonObject { ["d"] = new JsonObject { ["TotalNumber"] = total,
        ["Results"] = new JsonArray(Enumerable.Range(page * 2 + 1, Math.Min(2, Math.Max(0, total - page * 2))).Select(number => Row(number)).ToArray()) } };
    internal static JsonNode Row(int number) => new JsonObject { ["DeliveryID"] = Id(number), ["FolderID"] = FolderId, ["SessionName"] = "Recording " + number,
        ["Duration"] = 2, ["PlayableObjectType"] = 0, ["Status"] = 4, ["HasStreams"] = true, ["OrderSort"] = 10 - number,
        ["ViewerUrl"] = "https://campus.example/Panopto/Pages/Viewer.aspx?id=" + Id(number) };
    internal static JsonNode PlaylistInfo() => new JsonObject { ["Id"] = PlaylistId, ["RepositoryKey"] = PlaylistId, ["Name"] = "Public playlist", ["TypeName"] = "Playlist", ["SessionListId"] = ListId };
    internal static JsonNode PlaylistList(int count = 3) => new JsonObject { ["Id"] = ListId, ["RepositoryKey"] = ListId, ["UpdateDate"] = "2026-10-09T00:00:00Z",
        ["Items"] = new JsonArray(Enumerable.Range(1, count).Select(number => Item(number)).ToArray()) };
    internal static JsonNode Item(int number) => new JsonObject { ["Id"] = Id(number), ["Name"] = "Recording " + number, ["TypeName"] = "Session", ["Status"] = 4,
        ["ViewerUri"] = "https://campus.example/Panopto/Pages/Viewer.aspx?id=" + Id(number), ["Duration"] = 2 };
    internal static JsonNode Recording(string id, string extension = "mp4")
    {
        var data = PanoptoChecks.Data(); data["Delivery"]!["PublicID"] = id;
        data["Delivery"]!["PodcastStreams"]![0]!["StreamUrl"] = "https://cdn.example/sessions/" + PanoptoChecks.SessionId + "/" + id + "-" + PanoptoChecks.PodcastId + "." + extension + PanoptoChecks.Query;
        data["Delivery"]!["PodcastStreams"]![0]!["ViewerMediaFileTypeName"] = extension;
        data["Delivery"]!["Streams"] = new JsonArray(); return data;
    }
    internal static MediaHttp Http(List<(Uri Url, string Body)> calls, Func<Uri, JsonNode?, HttpResponseMessage?>? overrideResponse = null) => new(new Handler(async request =>
    {
        var text = request.Content is null ? "" : await request.Content.ReadAsStringAsync(); calls.Add((request.RequestUri!, text));
        var body = request.Content?.Headers.ContentType?.MediaType == "application/json" ? JsonNode.Parse(text) : null; var url = request.RequestUri!;
        if (overrideResponse?.Invoke(url, body) is { } overridden) return overridden;
        if (url.AbsolutePath.EndsWith("GetFolderInfo")) return Json(FolderInfo());
        if (url.AbsolutePath.EndsWith("GetSessions")) return Json(FolderPage(body!["queryParameters"]!["page"]!.GetValue<int>()));
        if (url.AbsolutePath.Contains("/Api/Playlists/")) return Json(PlaylistInfo());
        if (url.AbsolutePath.Contains("/Api/SessionLists/")) return Json(PlaylistList());
        if (url.AbsolutePath.EndsWith("DeliveryInfo.aspx"))
        {
            var fields = text.Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
            return fields.ContainsKey("getCaptions") ? new(HttpStatusCode.OK) { Content = new StringContent(PanoptoChecks.Captions) } : Json(Recording(fields["deliveryId"]));
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(PanoptoChecks.Html) };
    }), new MediaHttpOptions { AllowAutoRedirect = false });
    internal static HttpResponseMessage Json(JsonNode data) => new(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString()) };
    private static async Task<PageMetadata> Read(NativeDownloadRequest request, List<(Uri Url, string Body)>? calls = null, Func<Uri, JsonNode?, HttpResponseMessage?>? response = null)
    { using var http = Http(calls ?? [], response); return await new PanoptoProvider().ExtractAsync(request, http, default); }
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Panopto folder pagination preserves six source positions and recording archive keys", FolderFacts),
        ("Panopto folder specified order is retained unless the URL requests a different sort", FolderOrder),
        ("Panopto collection pages reject changed counts repeated IDs and cross-folder rows", FolderBounds),
        ("Panopto folder search and root-library scope reach typed discovery requests", Filters),
        ("Panopto playlist items retain intentional repeated recordings and unavailable positions", PlaylistFacts),
        ("Panopto no-playlist links inspect only the displayed recording", Single),
        ("Panopto playlist identity and viewer bindings reject mismatched resources", PlaylistBounds),
        ("Panopto paged authenticated playlists complete beyond the legacy inventory cap", Large),
        ("Panopto paged playlist access denial repeated pages and changed revisions fail closed", LargeBounds),
        ("Panopto collection selection resolves only selected entries and skips known archives", Selected),
        ("Panopto collection URL validation rejects ambiguous state and invalid limits", Addresses)
    ];
    private static async Task FolderFacts()
    {
        var calls = new List<(Uri Url, string Body)>(); var page = await Read(Folder(Path.GetTempPath()), calls);
        Assert(page.IsPlaylist && page.Entries.Count == 6 && page.Entries.Select(entry => entry.Position).SequenceEqual(Enumerable.Range(1, 6)), "Folder inventory or source positions changed.");
        Assert(page.Title == "Public folder" && page.PlaylistId == FolderId && page.Entries[2].ExpectedArchiveIdentity?.Id == "campus.example/" + Id(3) && calls.Count == 4,
            "Folder name, archive scope or complete pagination changed.");
    }
    private static async Task FolderOrder()
    {
        HttpResponseMessage? Ordered(Uri url, JsonNode? body) => url.AbsolutePath.EndsWith("GetFolderInfo") ? Json(FolderInfo(true)) : null;
        var page = await Read(Folder(Path.GetTempPath()), response: Ordered!);
        Assert(page.Entries.Select(entry => entry.Id).SequenceEqual(Enumerable.Range(1, 6).Reverse().Select(Id)), "Specified folder order was ignored.");
        page = await Read(Folder(Path.GetTempPath()) with { Url = new(Folder(Path.GetTempPath()).Url.AbsoluteUri + "&sortColumn=1") }, response: Ordered!);
        Assert(page.Entries[0].Id == Id(1), "Specified order overrode the explicit URL sort.");
    }
    private static async Task FolderBounds()
    {
        foreach (var mutation in new[] { "count", "repeat", "folder", "short" })
            await Reject<InvalidDataException>(() => Read(Folder(Path.GetTempPath()), response: (url, body) =>
            {
                if (!url.AbsolutePath.EndsWith("GetSessions")) return null!;
                var index = body!["queryParameters"]!["page"]!.GetValue<int>(); var data = FolderPage(index);
                if (index == 1) { if (mutation == "count") data["d"]!["TotalNumber"] = 7; if (mutation == "repeat") data["d"]!["Results"]![0] = Row(1);
                    if (mutation == "folder") data["d"]!["Results"]![0]!["FolderID"] = PlaylistId; if (mutation == "short") data["d"]!["Results"] = new JsonArray(); }
                return Json(data);
            }));
        await Reject<NotSupportedException>(() => Read(Folder(Path.GetTempPath()) with { MaximumPlaylistEntries = 2 }));
    }
    private static async Task Filters()
    {
        var calls = new List<(Uri Url, string Body)>(); await Read(Folder(Path.GetTempPath()) with { Url = new("https://campus.example/Panopto/Pages/Sessions/List.aspx#query=hello%20world&maxResults=2&sortColumn=1&sortAscending=true&view=2") }, calls);
        var parameters = JsonNode.Parse(calls[0].Body)!["queryParameters"]!;
        Assert(parameters["query"]!.GetValue<string>() == "hello world" && parameters["folderID"] is null && parameters["sortAscending"]!.GetValue<bool>(), "Root search scope or sort was discarded.");
    }
    private static async Task PlaylistFacts()
    {
        var page = await Read(Playlist(Path.GetTempPath()), response: (url, _) =>
        {
            if (!url.AbsolutePath.Contains("/Api/SessionLists/")) return null!;
            var data = PlaylistList(); data["Items"]![1] = Item(1); data["Items"]![2]!["IsDeleted"] = true; return Json(data);
        });
        Assert(page.IsPlaylist && page.Entries.Count == 3 && page.Entries[0].Id == page.Entries[1].Id && !page.Entries[2].Available && page.Entries[2].Position == 3,
            "Repeated records or unavailable source positions were removed.");
    }
    private static async Task Single()
    {
        var calls = new List<(Uri Url, string Body)>(); var request = Playlist(Path.GetTempPath()) with { Url = new(Playlist(Path.GetTempPath()).Url.AbsoluteUri + "&id=" + Id(1) + "&advance=true"), DownloadPlaylist = false };
        var page = await Read(request, calls);
        Assert(!page.IsPlaylist && page.Id == Id(1) && !calls.Any(call => call.Url.AbsolutePath.Contains("/Api/")) && !calls[0].Url.Query.Contains("pid="), "No-playlist requested the collection or lost the displayed recording.");
    }
    private static async Task PlaylistBounds()
    {
        foreach (var mutation in new[] { "metadata", "list", "viewer" })
            await Reject<InvalidDataException>(() => Read(Playlist(Path.GetTempPath()), response: (url, _) =>
            {
                if (mutation == "metadata" && url.AbsolutePath.Contains("/Api/Playlists/")) { var data = PlaylistInfo(); data["Id"] = FolderId; return Json(data); }
                if (url.AbsolutePath.Contains("/Api/SessionLists/")) { var data = PlaylistList(); if (mutation == "list") data["Id"] = FolderId;
                    if (mutation == "viewer") data["Items"]![0]!["ViewerUri"] = "https://other.example/Panopto/Pages/Viewer.aspx?id=" + Id(1); return Json(data); }
                return null!;
            }));
    }
    private static JsonNode V1Page(int start, int count) => new JsonObject { ["Results"] = new JsonArray(Enumerable.Range(start, count).Select(number => (JsonNode)new JsonObject {
        ["Id"] = Id(number), ["Name"] = "Recording " + number, ["Status"] = "Complete", ["Duration"] = 2,
        ["Urls"] = new JsonObject { ["ViewerUrl"] = "https://campus.example/Panopto/Pages/Viewer.aspx?id=" + Id(number) } }).ToArray()) };
    private static async Task Large()
    {
        var page = await Read(Playlist(Path.GetTempPath()) with { MaximumPlaylistEntries = 1000 }, response: (url, _) =>
        {
            if (url.AbsolutePath.Contains("/Api/SessionLists/")) return Json(PlaylistList(500));
            if (url.AbsolutePath.Contains("/api/v1/")) return Json(url.Query.EndsWith("=0") ? V1Page(1, 500) : url.Query.EndsWith("=1") ? V1Page(501, 1) : V1Page(1, 0));
            return null!;
        });
        Assert(page.Entries.Count == 501 && page.Entries[500].Position == 501 && page.Entries[500].Id == Id(501), "Large playlist was truncated to the legacy cap.");
    }
    private static async Task LargeBounds()
    {
        foreach (var mutation in new[] { "access", "repeat", "revision" })
        {
            var listReads = 0;
            await Reject<Exception>(() => Read(Playlist(Path.GetTempPath()) with { MaximumPlaylistEntries = 1000 }, response: (url, _) =>
            {
                if (url.AbsolutePath.Contains("/Api/SessionLists/")) { listReads++; var data = PlaylistList(500); if (mutation == "revision" && listReads > 1) data["UpdateDate"] = "changed"; return Json(data); }
                if (url.AbsolutePath.Contains("/api/v1/")) { if (mutation == "access") return new(HttpStatusCode.Unauthorized);
                    return Json(mutation == "repeat" || url.Query.EndsWith("=0") ? V1Page(1, 500) : V1Page(1, 0)); }
                return null!;
            }), ex => ex is InvalidDataException or NotSupportedException);
        }
    }
    private static async Task Selected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "native-panopto-collection-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var calls = new List<(Uri Url, string Body)>(); var engine = new NativeEngine([new PanoptoProvider()], _ => Http(calls));
            var request = Folder(directory) with { PlaylistItems = "2", Simulate = true };
            var result = await engine.DownloadAsync(request, null, null, default);
            Assert(result.Succeeded && calls.Count(call => call.Url.AbsolutePath.EndsWith("DeliveryInfo.aspx")) == 1 && calls.Single(call => call.Url.AbsolutePath.EndsWith("Viewer.aspx")).Url.Query.Contains(Id(2)), "Unselected records were resolved or downloaded.");
            calls.Clear(); var archive = Path.Combine(directory, "archive.txt"); await File.WriteAllTextAsync(archive, "panopto:delivery campus.example/" + Id(2) + "\n");
            result = await engine.DownloadAsync(request with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && !calls.Any(call => call.Url.AbsolutePath.EndsWith("Viewer.aspx") || call.Url.AbsolutePath.EndsWith("DeliveryInfo.aspx")), "Known collection archive identities did not skip selected discovery.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Addresses()
    {
        foreach (var value in new[] { "folderID=bad", "folderID=" + FolderId + "&folderID=" + FolderId, "maxResults=0", "page=2", "unknownFilter=true", "sortAscending=yes" })
            await Reject<Exception>(() => Read(Folder(Path.GetTempPath()) with { Url = new("https://campus.example/Panopto/Pages/Sessions/List.aspx#" + value) }), ex => ex is ArgumentException or NotSupportedException);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => run(request); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> run, Func<Exception, bool>? condition = null) where T : Exception
    { try { await run(); } catch (T ex) when (condition?.Invoke(ex) ?? true) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
