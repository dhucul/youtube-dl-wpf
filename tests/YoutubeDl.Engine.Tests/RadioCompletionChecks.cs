using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.RadioFrance;

internal static partial class RadioCompletionChecks
{
    internal static readonly string[] RadioFranceModes = ["metadata", "collection", "series", "unavailable", "limit", "partial", "cursor", "duplicate", "parent", "canonical", "station", "type", "uuid", "foreign", "duration", "denied", "drm", "redirect", "http", "headers", "renewal", "changed-renewal", "member-replaced", "archive"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => RadioFranceModes.Select(mode => ("Radio completion RF " + mode, (Func<Task>)(() => RadioFranceContract(mode))))
        .Concat(new[] { "cycle", "reference", "tag", "depth", "duplicate-field", "positive", "date" }.Select(mode => ("Radio completion JSON " + mode, (Func<Task>)(() => JsonContract(mode)))))
        .Concat(GeoChecks).Append(("Radio completion URL boundaries", Addresses));
    private static async Task RadioFranceContract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new RadioFranceFixture { Mode = mode }; var collection = mode is "collection" or "series" or "unavailable" or "limit" or "partial" or "cursor" or "duplicate" or "parent" or "member-replaced";
        var request = Request(f, files.Directory, collection); if (mode == "limit") request = request with { MaximumPlaylistEntries = 2 }; using var http = f.Http(mode == "headers");
        if (mode == "archive")
        {
            request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, "franceculture 1234567\n");
            var result = await f.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && f.MediaCalls == 0, "Archived Radio France media was transferred."); return;
        }
        if (mode is "limit" or "partial" or "cursor" or "duplicate" or "parent" or "canonical" or "station" or "type" or "uuid" or "foreign" or "duration" or "redirect" or "http")
        { await ProviderFamiliesChecks.Expect<Exception>(() => f.Provider.ExtractAsync(request, http, default)); Assert(f.MediaCalls == 0, "A refused RF descriptor transferred media."); return; }
        var page = await f.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "Radio France denial became successful media."); return; }
        if (collection)
        {
            Assert(page.IsPlaylist && page.Entries.Count == 3 && page.Entries.Select(row => row.Position).SequenceEqual(new[] { 1, 2, 3 }) && page.Entries.All(row => row.ResolvedMedia is null), "Radio France lost complete collection order or retained media URLs.");
            if (mode == "unavailable") Assert(!page.Entries[2].Available, "Radio France hid an unavailable member.");
            if (mode == "member-replaced") { f.Mode = "replaced"; await ProviderFamiliesChecks.Expect<InvalidDataException>(() => f.Provider.ExtractAsync(request with { Url = f.Url }, http, default)); }
            Assert(f.MediaCalls == 0, "RF collection inspection transferred media."); return;
        }
        Assert(page.Id == "1234567" && page.Extractor == "FranceCulture" && page.DurationSeconds == 3 && page.Media[0].Extension == "m4a", "Radio France selected another episode or full duration.");
        if (mode == "headers") Assert(f.RequestHeaders.All(headers => !headers.Contains("Authorization") && !headers.Contains("X-Owned-Secret") && !headers.Contains("Cookie") && !headers.Contains("Referer")), "Source credentials entered the public RF API.");
        if (mode is "renewal" or "changed-renewal")
        {
            using var anonymous = http.CreateAnonymousSession(); using var playback = anonymous.WithAuthorization(page.PlaybackAuthorization!); var target = Path.Combine(files.Directory, "media.bin");
            if (mode == "changed-renewal") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, target, null, null, default, 0)); Assert(!File.Exists(target) && f.MediaCalls == 1, "Changed RF renewal transferred replacement media."); }
            else { await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, target, null, null, default, 0); Assert(File.ReadAllBytes(target).SequenceEqual(f.Data) && f.MediaCalls == 2 && f.Generation == 2, "RF bounded source renewal changed."); }
        }
    }
    internal static NativeDownloadRequest Request(RadioFranceFixture fixture, string directory, bool collection = false) => new() { Url = collection ? fixture.Collection : fixture.Url, OutputDirectory = directory, FileName = "recording", MaximumPlaylistEntries = 100, Retries = 0, FragmentRetries = 0, NoMtime = true, NoCache = true };
    private static Task JsonContract(string mode)
    {
        JsonElement envelope;
        if (mode == "positive") envelope = RadioFranceFixture.Encode(new { title = "owned", array = new[] { 1, 2 } });
        else if (mode == "date") envelope = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"data\",\"nodes\":[{\"type\":\"data\",\"data\":[{\"date\":1},[\"Date\",\"2020-01-01T00:00:00Z\"]]}]}");
        else
        {
            var raw = mode switch { "cycle" => "[{\"next\":0}]", "reference" => "[{\"next\":8}]", "tag" => "[[\"Function\",\"owned text\"]]", "duplicate-field" => "[{\"a\":1,\"a\":1},\"x\"]", _ => "[" + string.Join(',', Enumerable.Range(0, 70).Select(i => "{\"next\":" + (i + 1) + "}")) + ",null]" };
            envelope = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"data\",\"nodes\":[{\"type\":\"data\",\"data\":" + raw + "}]}");
        }
        if (mode is "positive" or "date") Assert(SvelteData.Decode(envelope).Length == 1, "A bounded RF JSON graph was rejected.");
        else { try { SvelteData.Decode(envelope); throw new Exception("An invalid RF JSON graph was accepted."); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or JsonException) { } }
        return Task.CompletedTask;
    }
    private static Task Addresses()
    {
        Assert(RadioFranceAddress.Parse(new("https://www.radiofrance.fr/franceculture/podcasts/owned/episode-1234567")).Id == "1234567", "RF lost the captured numeric page identity.");
        foreach (var url in new[] { "https://www.radiofrance.fr.evil.example/franceculture/podcasts/owned", "https://www.radiofrance.fr/franceculture/podcasts/owned?p=2", "https://user@www.radiofrance.fr/franceculture/podcasts/owned", "https://www.radiofrance.fr/franceculture" }) { try { RadioFranceAddress.Parse(new(url)); throw new Exception("Accepted an invalid RF URL."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } }
        return Task.CompletedTask;
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class RadioFranceFixture
    {
        internal const string Parent = "11111111-1111-4111-8111-111111111111";
        internal string Mode = "metadata"; internal int ApiCalls, MediaCalls, Generation; internal byte[] Data = "owned complete radio audio"u8.ToArray(); internal List<string[]> RequestHeaders = [];
        internal Uri Url => new("https://www.radiofrance.fr/franceculture/podcasts/owned/episode-1234567"); internal Uri Collection => new("https://www.radiofrance.fr/franceculture/podcasts/owned");
        internal IMediaProvider Provider => new RadioFranceProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal MediaHttp Http(bool headers = false)
        {
            var options = new MediaHttpOptions { AllowAutoRedirect = false }; if (headers) { options.Headers["Authorization"] = "Bearer owned-secret"; options.Headers["X-Owned-Secret"] = "owned"; options.Referer = "https://owned.example/private"; }
            return new MediaHttp(new Handler(this), options);
        }
        internal static string Uuid(int id) => "22222222-2222-4222-8222-" + id.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        private static JsonObject Obj(object value) => (JsonObject)JsonSerializer.SerializeToNode(value)!;
        private JsonObject Item(int id)
        {
            return Obj(new { __typename = "Expression", id = Uuid(id), type = "episode", brand = "franceculture", conceptId = Mode == "parent" ? Uuid(8) : Parent, duration = 3, manifestation = Mode == "unavailable" && id == 1234569 ? null : new { __typename = "ManifestationAudio", duration = 3 }, titleProps = new { title = "Owned RF episode " + id, href = "/franceculture/podcasts/owned/episode-" + id } });
        }
        internal JsonElement Response(Uri url)
        {
            var path = url.AbsolutePath[..^"/__data.json".Length].Trim('/'); var collection = path == "franceculture/podcasts/owned"; JsonObject content;
            if (collection)
            {
                var second = url.Query == "?p=2"; var items = second ? new JsonArray(Item(Mode == "duplicate" ? 1234567 : 1234569)) : new JsonArray(Item(1234567), Item(1234568));
                content = Obj(new { __typename = "Concept", id = Parent, type = Mode == "series" ? "serie" : "podcast", path, title = "Owned RF podcast", expressionCount = 3, limit = 2, pagination = new { lastPage = 2 }, expressions = new { next = second ? null : Mode == "cursor" ? "invalid" : "Mg==", prev = second ? "MA==" : null, items } });
                if (Mode == "partial") content["expressionCount"] = 4;
            }
            else
            {
                Generation++; var id = int.Parse(path.Split('-').Last(), System.Globalization.CultureInfo.InvariantCulture); var uuid = Mode == "uuid" ? "bad" : Uuid(Mode == "replaced" || Mode == "changed-renewal" && Generation > 1 ? id + 1 : id);
                content = Obj(new { __typename = Mode == "type" ? "Concept" : "Expression", id = uuid, type = "episode", brand = "franceculture", path, title = "Owned RF episode", isStreamable = Mode != "denied", duration = 3, drm = Mode == "drm", startDate = 1577836800, conceptTitle = "Owned RF podcast", manifestation = new { __typename = "ManifestationAudio", id = Uuid(90), duration = Mode == "duration" ? 8 : 3, url = "https://" + (Mode == "foreign" ? "other.example" : "media.radiofrance-podcast.net") + "/owned.m4a?generation=" + Generation } });
            }
            return Encode(new { contentType = collection ? "Concept" : "Expression", pageType = collection ? Mode == "series" ? "Serie" : "Podcast" : "Expression", content, seo = new { canonicalUrl = "https://www.radiofrance.fr/" + (Mode == "canonical" ? "other" : path) }, station = new { name = Mode == "station" ? "franceinter" : "franceculture", title = "France Culture" } });
        }
        internal static JsonElement Encode(object value)
        {
            var source = JsonSerializer.SerializeToNode(value)!; var values = new List<JsonNode?>();
            int Add(JsonNode? node)
            {
                var index = values.Count; values.Add(null);
                values[index] = node switch { JsonObject obj => new JsonObject(obj.Select(field => new KeyValuePair<string, JsonNode?>(field.Key, JsonValue.Create(Add(field.Value))))), JsonArray array => new JsonArray(array.Select(item => (JsonNode?)JsonValue.Create(Add(item))).ToArray()), _ => node?.DeepClone() }; return index;
            }
            Add(source); return JsonSerializer.SerializeToElement(new { type = "data", nodes = new[] { new { type = "data", data = values } } });
        }
        private sealed class Handler(RadioFranceFixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
                if (url.Host == "www.radiofrance.fr")
                {
                    fixture.ApiCalls++; fixture.RequestHeaders.Add(request.Headers.Select(x => x.Key).ToArray());
                    if (fixture.Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    if (fixture.Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/data") } });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture.Response(url).GetRawText()) });
                }
                Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "RF media received API/source credentials."); fixture.MediaCalls++;
                if (fixture.Mode is "renewal" or "changed-renewal" && fixture.MediaCalls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Data) });
            }
        }
    }
}
