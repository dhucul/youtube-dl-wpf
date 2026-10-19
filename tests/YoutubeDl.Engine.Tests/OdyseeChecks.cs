using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Odysee;

internal static class OdyseeChecks
{
    internal static readonly string StreamId = new('a', 40), ChannelId = new('b', 40), OtherId = new('d', 40), CollectionId = new('e', 40), Hash = new('c', 96);
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Odysee resolves public claim identities and offered original media facts", Metadata),
        ("Odysee web, embed, download and native LBRY aliases use the same stream", Aliases),
        ("Odysee channel pagination preserves all original positions without playback requests", Channel),
        ("Odysee channel limits, changing pages and withheld results never truncate silently", ChannelLimits),
        ("Odysee public collections preserve order, repeats and unavailable positions", Collection),
        ("Odysee selected collection downloads fetch only the selected stream", Selected),
        ("Odysee paid, member, scheduled and unsupported claims never reach playback lookup", Access),
        ("Odysee mismatched claims, publishers and playback resources fail explicitly", Invalid),
        ("Odysee normal video constraints and explicit audio-only claims remain distinct", Constraints),
        ("Odysee RPC failures and cancellation cannot publish a file", Failures),
        ("Odysee archives skip published claims without repeating media transfer", Archive),
        ("Odysee strips explicit account headers from public metadata and playback", Credentials),
        ("Anonymous HTTP sessions isolate real cookies and preserve the original session", AnonymousSession)
    ];
    internal static JsonObject Stream(string? id = null, string name = "video") => new()
    {
        ["claim_id"] = id ?? StreamId, ["name"] = name, ["value_type"] = "stream", ["is_channel_signature_valid"] = true,
        ["signing_channel"] = ChannelClaim(),
        ["value"] = new JsonObject { ["title"] = "Fixture " + name, ["description"] = "Description", ["stream_type"] = "video",
            ["source"] = new JsonObject { ["sd_hash"] = Hash, ["media_type"] = "video/mp4", ["name"] = name + ".mp4", ["size"] = "12" },
            ["video"] = new JsonObject { ["width"] = 1280, ["height"] = 720, ["duration"] = 4 },
            ["thumbnail"] = new JsonObject { ["url"] = "https://thumbnails.odycdn.com/example.jpg" } }
    };
    internal static JsonObject ChannelClaim() => new() { ["claim_id"] = ChannelId, ["name"] = "@channel", ["value_type"] = "channel", ["value"] = new JsonObject { ["title"] = "Fixture channel" } };
    private static JsonObject CollectionClaim(params string[] ids) => new() { ["claim_id"] = CollectionId, ["name"] = "list", ["value_type"] = "collection", ["value"] = new JsonObject { ["title"] = "Fixture list", ["claims"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) } };
    private static JsonObject Search(JsonObject[] items, int total, int page = 1, int pageSize = 50) => new()
    { ["items"] = new JsonArray(items.Select(item => item.DeepClone()).ToArray()), ["page"] = page, ["page_size"] = pageSize, ["total_items"] = total, ["total_pages"] = (total + pageSize - 1) / pageSize, ["blocked"] = new JsonObject { ["total"] = 0, ["channels"] = new JsonArray() } };
    internal static NativeEngine Engine(JsonObject claim, List<string> calls, Func<JsonObject, JsonNode>? search = null, Func<string, JsonNode>? playback = null,
        Func<HttpRequestMessage, HttpResponseMessage>? media = null, MediaHttpOptions? options = null, Action<HttpRequestMessage>? inspect = null) => new(httpFactory: _ => new MediaHttp(new Handler(async (request, ct) =>
    {
        inspect?.Invoke(request);
        if (request.RequestUri!.Host == "api.na-backend.odysee.com")
        {
            var rpc = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject(); var method = rpc["method"]!.GetValue<string>(); calls.Add(method);
            Assert(request.RequestUri.Query == "?m=" + method && rpc["jsonrpc"]!.GetValue<string>() == "2.0", "Odysee RPC routing changed.");
            var parameters = rpc["params"]!.AsObject(); JsonNode value;
            if (method == "resolve") value = new JsonObject { [parameters["urls"]![0]!.GetValue<string>()] = claim.DeepClone() };
            else if (method == "claim_search") value = search?.Invoke(parameters) ?? throw new InvalidOperationException("Unexpected claim search.");
            else if (method == "get")
            {
                Assert(parameters["save_file"]!.GetValue<bool>() == false, "Playback lookup requested server-side file saving.");
                var uri = parameters["uri"]!.GetValue<string>();
                value = playback?.Invoke(uri) ?? new JsonObject { ["streaming_url"] = "https://player.odycdn.com/v6/streams/" + claim["claim_id"]!.GetValue<string>() + "/cccccc.mp4?access=a%2Bb" };
            }
            else throw new InvalidOperationException("A non-playback method was requested.");
            return Text(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["result"] = value }.ToJsonString());
        }
        calls.Add("media"); return media?.Invoke(request) ?? Text("Odysee media");
    }), options));
    internal static NativeDownloadRequest Request(string directory) => new() { Url = new("https://odysee.com/@channel:b/video:a"), OutputDirectory = directory, FileName = "odysee", Retries = 0 };
    private static async Task Metadata()
    {
        var calls = new List<string>(); var info = await Engine(Stream(), calls).InspectAsync(Request(Path.GetTempPath()), default);
        Assert(info.Id == StreamId && info.Title == "Fixture video" && info.Uploader == "Fixture channel" && info.DurationSeconds == 4 &&
            info.Formats.Single() is { Width: 1280, Height: 720, FileSize: 12, VideoCodec: null, HasAudio: null } && calls.SequenceEqual(["resolve", "get"]),
            "Odysee claim facts were lost or codec/audio facts were invented.");
    }
    private static async Task Aliases()
    {
        using var files = new Files();
        foreach (var url in new[] { "https://odysee.com/video:a", "https://lbry.tv/@channel:b/video:a", "https://odysee.com/$/embed/@channel:b/video:a",
            "https://odysee.com/$/download/video/" + StreamId, "https://odysee.com/$/stream/video/" + StreamId, "lbry://@channel#b/video#a", "lbry://video#" + StreamId })
        {
            var calls = new List<string>();
            var result = await Engine(Stream(), calls, media: request => { Assert(request.RequestUri!.Query == "?access=a%2Bb", "A literal playback query was changed."); return Text("Odysee media"); })
                .DownloadAsync(Request(files.Directory) with { Url = new(url) }, null, null, default);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "Odysee media", "An Odysee/LBRY alias did not download its resolved stream.");
        }
        Assert(NativeEngine.CanHandle(new("lbry://video#" + StreamId)), "Native LBRY URIs were not registered.");
        var generic = new MediaProviderRegistry([new GenericPageProvider()]);
        Assert(!generic.CanHandle(new("lbry://video#" + StreamId)), "An unregistered LBRY URI reached generic HTTP discovery.");
        foreach (var url in new[] { "https://odysee.com/video:a?t=10", "https://odysee.com/$/playlist/local-playlist", "https://odysee.com/video:xyz", "https://odysee.com/video:a/other", "lbry://video?extra=1" })
            await Expect<Exception>(() => Engine(Stream(), []).InspectAsync(Request(files.Directory) with { Url = new(url) }, default));
    }
    private static async Task Channel()
    {
        var items = Enumerable.Range(1, 53).Select(index => Stream(index.ToString("x40"), "video" + index)).ToArray(); var calls = new List<string>();
        items[26]["is_channel_signature_valid"] = false;
        var engine = Engine(ChannelClaim(), calls, parameters =>
        {
            Assert(parameters["channel_ids"]![0]!.GetValue<string>() == ChannelId && parameters["page_size"]!.GetValue<int>() == 50 && parameters["has_source"]!.GetValue<bool>(), "Channel scope or source filter was lost.");
            var page = parameters["page"]!.GetValue<int>(); return Search(items.Skip((page - 1) * 50).Take(50).ToArray(), 53, page);
        });
        var result = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://odysee.com/@channel:b") }, default);
        Assert(result.IsPlaylist && result.Id == ChannelId && result.Entries.Count == 53 && result.Entries.Select(entry => entry.Position).SequenceEqual(Enumerable.Range(1, 53)) &&
            result.Entries.Count(entry => entry.Available) == 52 && !result.Entries[26].Available && calls.SequenceEqual(["resolve", "claim_search", "claim_search"]), "A channel inventory was truncated, renumbered or fetched playback URLs.");
    }
    private static async Task ChannelLimits()
    {
        var request = Request(Path.GetTempPath()) with { Url = new("https://odysee.com/@channel:b"), MaximumPlaylistEntries = 1 };
        await Expect<InvalidDataException>(() => Engine(ChannelClaim(), [], _ => Search([Stream(), Stream(OtherId)], 2)).InspectAsync(request, default));
        await Expect<InvalidDataException>(() => Engine(ChannelClaim(), [], _ => Search([Stream()], 2)).InspectAsync(request with { MaximumPlaylistEntries = 10 }, default));
        await Expect<NotSupportedException>(() => Engine(ChannelClaim(), [], _ => { var result = Search([], 0); result["blocked"]!["total"] = 1; return result; }).InspectAsync(request, default));
        var foreign = Stream(); foreign["signing_channel"]!["claim_id"] = OtherId;
        await Expect<InvalidDataException>(() => Engine(ChannelClaim(), [], _ => Search([foreign], 1)).InspectAsync(request, default));
    }
    private static async Task Collection()
    {
        var missing = new string('f', 40); var collection = CollectionClaim(StreamId, OtherId, StreamId, missing); var calls = new List<string>();
        var engine = Engine(collection, calls, parameters => parameters["page_size"]!.GetValue<int>() == 1 ? Search([collection], 1, pageSize: 1) : Search([Stream(OtherId, "second"), Stream()], 2));
        var info = await engine.InspectAsync(Request(Path.GetTempPath()) with { Url = new("https://odysee.com/$/playlist/" + CollectionId) }, default);
        Assert(info.IsPlaylist && info.Id == CollectionId && info.Entries.Select(entry => entry.Id).SequenceEqual([StreamId, OtherId, StreamId, missing]) &&
            info.Entries.Take(3).All(entry => entry.Available) && !info.Entries[3].Available && info.Entries[3].Position == 4 && !calls.Contains("get"),
            "Public collection order, repeats or unavailable positions were lost.");
    }
    private static async Task Selected()
    {
        using var files = new Files(); var collection = CollectionClaim(StreamId, OtherId); var calls = new List<string>();
        var sessions = 0;
        var engine = new NativeEngine(httpFactory: _ => { sessions++; return Factory(); });
        MediaHttp Factory() => new(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.Host != "api.na-backend.odysee.com") { calls.Add("media"); return Text("selected"); }
            var rpc = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!; var method = rpc["method"]!.GetValue<string>(); calls.Add(method);
            JsonNode result = method switch {
                "resolve" => new JsonObject { [rpc["params"]!["urls"]![0]!.GetValue<string>()] =
                    rpc["params"]!["urls"]![0]!.GetValue<string>() == "lbry://second#" + OtherId ? Stream(OtherId, "second") : collection.DeepClone() },
                "claim_search" => Search([Stream(OtherId, "second"), Stream()], 2),
                "get" when rpc["params"]!["uri"]!.GetValue<string>() == "lbry://second#" + OtherId => new JsonObject { ["streaming_url"] = "https://player.odycdn.com/v6/streams/" + OtherId + "/cccccc.mp4" },
                _ => throw new InvalidOperationException("An unselected item requested playback.") };
            return Text(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["result"] = result }.ToJsonString());
        }));
        var result = await engine.DownloadAsync(Request(files.Directory) with { Url = new("https://odysee.com/list:e"), PlaylistItems = "2" }, null, null, default);
        Assert(result.Succeeded && sessions == 1 && result.OutputPaths.Count == 1 && Path.GetFileName(result.OutputPaths[0]).StartsWith("002 - ") && calls.Count(item => item == "get") == 1 &&
            File.ReadAllText(result.OutputPaths[0]) == "selected", "Selection fetched or published the wrong collection item.");
    }
    private static async Task Access()
    {
        foreach (var mode in new[] { "paid", "tiny-fee", "unknown-fee", "member", "rental", "scheduled", "missing-source", "document" })
        {
            var claim = Stream(); var value = claim["value"]!.AsObject(); var calls = new List<string>();
            if (mode is "paid" or "tiny-fee" or "unknown-fee") value["fee"] = mode == "unknown-fee" ? new JsonObject() : new JsonObject { ["amount"] = mode == "paid" ? "1" : "1e-999" };
            else if (mode is "member" or "rental" or "scheduled") { value["tags"] = new JsonArray(mode == "member" ? "c:members-only" : mode == "rental" ? "rental:fixture" : "c:scheduled:show"); value["release_time"] = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(); }
            else if (mode == "missing-source") value.Remove("source"); else value["stream_type"] = "document";
            await Expect<NotSupportedException>(() => Engine(claim, calls).InspectAsync(Request(Path.GetTempPath()), default));
            Assert(calls.SequenceEqual(["resolve"]), "A paid/restricted/unsupported claim reached playback lookup.");
        }
    }
    private static async Task Invalid()
    {
        var wrong = Stream(OtherId); await Expect<InvalidDataException>(() => Engine(wrong, []).InspectAsync(Request(Path.GetTempPath()), default));
        wrong = Stream(); wrong["is_channel_signature_valid"] = false;
        await Expect<InvalidDataException>(() => Engine(wrong, []).InspectAsync(Request(Path.GetTempPath()), default));
        foreach (var url in new[] { "https://player.odycdn.com.evil.example/v6/streams/" + StreamId + "/cccccc.mp4", "https://player.odycdn.com/v6/streams/" + OtherId + "/cccccc.mp4", "https://player.odycdn.com/v6/streams/" + StreamId + "/dddddd.mp4" })
            await Expect<InvalidDataException>(() => Engine(Stream(), [], playback: _ => new JsonObject { ["streaming_url"] = url }).InspectAsync(Request(Path.GetTempPath()), default));
    }
    private static async Task Constraints()
    {
        using var files = new Files(); var calls = new List<string>();
        await Expect<InvalidOperationException>(() => Engine(Stream(), calls).DownloadAsync(Request(files.Directory) with { MaximumHeight = 360 }, null, null, default));
        Assert(!calls.Contains("media"), "An incompatible original rendition was downloaded despite a maximum height.");
        var audio = Stream(); audio["value"]!["stream_type"] = "audio"; audio["value"]!.AsObject().Remove("video"); audio["value"]!["audio"] = new JsonObject { ["duration"] = 5 };
        audio["value"]!["source"]!["media_type"] = "audio/mpeg";
        var result = await Engine(audio, [], playback: _ => new JsonObject { ["streaming_url"] = "https://player.odycdn.com/v6/streams/" + StreamId + "/cccccc.mp4" })
            .DownloadAsync(Request(files.Directory) with { MaximumHeight = 360 }, null, null, default);
        Assert(result.Succeeded && result.OutputPath!.EndsWith(".mp3"), "A genuine audio-only claim was blocked by video constraints.");
    }
    private static async Task Failures()
    {
        var calls = 0;
        var denied = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler((_, _) => { calls++; return Task.FromResult(Text("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1}}")); })));
        await Expect<NotSupportedException>(() => denied.InspectAsync(Request(Path.GetTempPath()), default)); Assert(calls == 1, "An RPC failure caused alternative endpoint requests.");
        using var files = new Files(); using var stop = new CancellationTokenSource();
        var engine = Engine(Stream(), [], media: _ => { stop.Cancel(); return Text("partial"); });
        await Expect<OperationCanceledException>(() => engine.DownloadAsync(Request(files.Directory), null, null, stop.Token));
        Assert(!File.Exists(Path.Combine(files.Directory, "odysee.mp4")), "Canceled Odysee media was published.");
    }
    private static async Task Archive()
    {
        using var files = new Files(); var calls = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt"); var engine = Engine(Stream(), calls);
        var request = Request(files.Directory) with { DownloadArchiveFile = archive };
        await engine.DownloadAsync(request, null, null, default); calls.Clear();
        var result = await engine.DownloadAsync(request, null, null, default);
        Assert(result.Skipped && calls.SequenceEqual(["resolve", "get"]) && File.ReadAllText(archive).Trim() == "lbry " + StreamId, "Odysee archive identity or recorded-media skipping changed.");
    }
    private static async Task Credentials()
    {
        using var files = new Files(); var options = new MediaHttpOptions(); options.Headers["Authorization"] = "Bearer fixture"; options.Headers["X-Lbry-Auth-Token"] = "fixture"; options.Headers["Cookie"] = "account=fixture";
        var result = await Engine(Stream(), [], options: options, inspect: request => Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("X-Lbry-Auth-Token") && !request.Headers.Contains("Cookie"),
            "A public Odysee operation forwarded account credentials.")).DownloadAsync(Request(files.Directory), null, null, default);
        Assert(result.Succeeded, "Anonymous public playback could not complete.");
    }
    private static async Task AnonymousSession()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var url = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/sample");
            var cookies = new CookieContainer(); cookies.Add(url, new Cookie("account", "fixture"));
            var options = new MediaHttpOptions { CookieContainer = cookies, UserAgent = "Fixture/1" }; options.Headers["Authorization"] = "Bearer fixture"; options.Headers["X-Lbry-Auth-Token"] = "fixture";
            var server = Observe(); using var original = new MediaHttp(options); using var anonymous = original.CreateAnonymousSession();
            Assert(await anonymous.GetStringAsync(url, 16, stop.Token) == "OK", "Anonymous HTTP response failed.");
            var headers = await server;
            Assert(!headers.Contains("Cookie:", StringComparison.OrdinalIgnoreCase) && !headers.Contains("Authorization:", StringComparison.OrdinalIgnoreCase) && !headers.Contains("X-Lbry-Auth-Token:", StringComparison.OrdinalIgnoreCase) &&
                headers.Contains("Fixture/1") && cookies.Count == 1, "Anonymous session copied credentials, lost connection preferences or cleared the original cookie jar.");
            async Task<string> Observe()
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token); using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var lines = new StringBuilder(); string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token))) { lines.AppendLine(line); if (lines.Length > 16384) throw new InvalidDataException("Excessive test headers."); }
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"u8.ToArray(), stop.Token); return lines.ToString();
            }
        }
        finally { listener.Stop(); }
    }
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "odysee-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
