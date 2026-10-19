using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Rtve;

internal static partial class RtveChecks
{
    internal sealed class Fixture
    {
        internal string Mode = "audio"; internal byte[] Media = "complete owned MP3 fixture"u8.ToArray(); internal int Api, Transfers, Html;
        internal int Total => Mode is "pages" or "changing" or "repeat-page" ? 51 : 3;
        internal Dictionary<string, object?> Audio(string id = "41", bool inventory = false)
        {
            var row = new Dictionary<string, object?> { ["id"] = id, ["title"] = "Selected &amp; complete " + id, ["htmlUrl"] = "https://www.rtve.es/play/audios/radio-fixture/selected/" + id + "/", ["assetType"] = "audio", ["contentType"] = "audio", ["consumption"] = "podcast", ["duration"] = 3000, ["publicationDateTimestamp"] = 1609459200000L, ["pubState"] = new { code = "ENPUB" }, ["programInfo"] = new { id = "19", title = "Radio fixture" }, ["thumbnail"] = "https://img.rtve.es/a/41.jpg", ["qualities"] = new[] { new { identifier = 9, filePath = "https://ztnr.rtve.es/ztnr/" + id + ".mp3", filesize = Media.Length, duration = 3000, type = "audio/mpeg", bitRate = 128000 } } };
            if (Mode == "identity") row["id"] = "99";
            if (Mode == "foreign-page") row["htmlUrl"] = "https://other.example/a/41";
            if (Mode == "wrong-type") row["assetType"] = "video";
            if (Mode == "live") row["consumption"] = "live";
            if (Mode == "duration") row["duration"] = 0;
            if (Mode == "cross-program") row["programInfo"] = new { id = "999" };
            if (Mode is "drm" or "paid" or "region" or "disabled" or "not-downloadable") row[Mode switch { "drm" => "hasDRM", "paid" => "paidContent", "region" => "geolocalizado", "disabled" => "disabledAlacarta", _ => "notDownloadable" }] = true;
            if (Mode == "unpublished") row["pubState"] = new { code = "DRAFT" };
            if (inventory && id == "42" && Mode == "unavailable") row["notDownloadable"] = true;
            if (Mode == "missing-quality") row["qualities"] = System.Array.Empty<object>();
            if (Mode is "foreign-media" or "placeholder" or "quality-duration" or "quality-size" or "codec") row["qualities"] = new[] { new { identifier = 9, filePath = Mode == "foreign-media" ? "https://other.example/41.mp3" : Mode == "placeholder" ? "https://ztnr.rtve.es/ztnr/_TOKEN_/41.mp3" : "https://ztnr.rtve.es/ztnr/41.mp3", filesize = Mode == "quality-size" ? 0 : Media.Length, duration = Mode == "quality-duration" ? 9000 : 3000, type = Mode == "codec" ? "audio/aac" : "audio/mpeg", bitRate = 128000 } };
            return row;
        }
        internal object Program => new { id = "19", contentType = "programa", title = "Radio fixture", htmlUrl = "https://www.rtve.es/play/audios/" + (Mode == "wrong-slug" ? "other" : "radio-fixture") + "/", thumbnail = "https://img.rtve.es/p/19.jpg", pubState = new { code = "ENPUB" }, paidContent = Mode == "parent-paid", notDownloadable = Mode == "parent-restricted" };
        internal static object Envelope(object[] rows, int number = 1, int size = 1, int total = 1, int offset = 0, int? totalPages = null) => new { page = new { items = rows, number, size, total, offset, totalPages = totalPages ?? (total + size - 1) / size, numElements = rows.Length } };
        internal MediaHttp Http => AdaptiveAccountChecks.Http((request, ct) =>
        {
            ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
            if (url.Host == "api.rtve.es")
            {
                Api++; if (Mode == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                if (Mode == "redirect") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new("https://other.example/api") } });
                object value;
                if (url.AbsolutePath == "/api/programas/19.json") value = Envelope([Program]);
                else if (url.AbsolutePath == "/api/programas/19/audios.json")
                {
                    var page = int.Parse(RtveAddress.Match(url.Query, "[?&]page=(?<n>[0-9]+)").Groups["n"].Value, CultureInfo.InvariantCulture);
                    var offset = (page - 1) * 50; var rows = Enumerable.Range(offset, Math.Min(50, Total - offset)).Select(index => (object)Audio(Mode == "duplicates" && index == 1 ? "41" : Mode == "moved" && index == 1 ? "44" : (41 + index).ToString(CultureInfo.InvariantCulture), true)).ToArray();
                    value = Envelope(rows, Mode == "repeat-page" && page == 2 ? 1 : page, 50, Mode == "changing" && page == 2 ? Total + 1 : Total, offset);
                }
                else value = Envelope([Audio(RtveAddress.Match(url.AbsolutePath, @"/audios/(?<id>[0-9]+)\.json\z").Groups["id"].Value)]);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
            }
            if (url.Host == "www.rtve.es") { Html++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<meta content=\"" + (Mode == "no-id" ? "" : "19") + "\" name=\"DC.identifier\"><meta name=\"unrelated\" content=\"99\">") }); }
            Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Media) });
        });
        internal NativeEngine Engine => new([new RtveProvider()], _ => Http);
    }
    internal static NativeDownloadRequest Request(string folder, bool collection = false) => new() { Url = new(collection ? "https://www.rtve.es/play/audios/radio-fixture/" : "https://www.rtve.es/a/41/"), OutputDirectory = folder, FileName = "recording", Retries = 0, MaximumPlaylistEntries = 100, NoMtime = true };
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        new[] { "audio", "program", "pages", "duplicates", "unavailable", "identity", "foreign-page", "wrong-type", "live", "duration", "cross-program", "drm", "paid", "region", "disabled", "not-downloadable", "unpublished", "missing-quality", "foreign-media", "placeholder", "quality-duration", "quality-size", "codec", "changing", "repeat-page", "wrong-slug", "no-id", "http", "redirect", "limit", "parent-paid", "parent-restricted" }
            .Select(mode => ("RTVE contract " + mode, (Func<Task>)(() => Contract(mode))))
        .Concat(new[] { "archive", "no-overwrite", "unsupported-proxy", "opaque-proxy" }.Select(mode => ("RTVE protected job " + mode, (Func<Task>)(() => Protected(mode)))))
        .Concat(new[] { "http://127.0.0.1:81", "https://proxy.example/" }.Select((proxy, index) => ("RTVE verification proxy syntax " + index, (Func<Task>)(() => { NativeGeoVerification.Validate(new() { Proxy = proxy }); return Task.CompletedTask; }))))
        .Concat(new[] { "private-value", "socks5://proxy.example", "http://user:secret@proxy.example", "http://proxy.example/path", "http://proxy.example?token=secret", "http://proxy.example/#fragment", "" }.Select((proxy, index) => ("RTVE verification proxy refusal " + index, (Func<Task>)(() => { Reject(() => NativeGeoVerification.ProxyValue(proxy)); return Task.CompletedTask; }))))
        .Concat(new[] { ("RTVE addresses aliases defaults and analysis", (Func<Task>)Aliases), ("RTVE actual verification proxy and renewal isolation", (Func<Task>)(() => Proxy(false))), ("RTVE actual curl verification proxy isolation", (Func<Task>)(() => Proxy(false, true))), ("RTVE strict verification proxy certificate isolation", (Func<Task>)(() => Proxy(true))), ("RTVE playback denial survives verification proxy", (Func<Task>)(() => Proxy(false, false, true))), ("RTVE verification connection cannot reuse another cached grant", (Func<Task>)ChangedVerification) });
    private static async Task ChangedVerification()
    {
        var fixture = new Fixture(); using var http = fixture.Http; var provider = new RtveProvider(); var request = Request("unused");
        await provider.ExtractAsync(request, http, default);
        await Expect<NotSupportedException>(() => provider.ExtractAsync(request with { GeoVerification = new() { Proxy = "http://proxy.example" } }, http, default));
    }
    private static async Task Contract(string mode)
    {
        var fixture = new Fixture { Mode = mode }; using var http = fixture.Http;
        var collection = mode is "program" or "pages" or "duplicates" or "unavailable" or "cross-program" or "changing" or "repeat-page" or "wrong-slug" or "no-id" or "limit" or "parent-paid" or "parent-restricted";
        var request = Request("unused", collection); if (mode == "limit") request = request with { MaximumPlaylistEntries = 2 };
        if (mode is "audio" or "program" or "pages" or "duplicates" or "unavailable" or "parent-paid" or "parent-restricted" or "cross-program")
        {
            var page = await new RtveProvider().ExtractAsync(request, http, default);
            if (!collection) Assert(page.Id == "41" && page.Media.Single().FileSize == fixture.Media.Length && page.DurationSeconds == 3 && page.Title == "Selected & complete 41", "RTVE lost selected complete audio metadata.");
            else
            {
                Assert(page.IsPlaylist && page.Id == "19" && page.Entries.Count == fixture.Total && page.Entries.Select(row => row.Position).SequenceEqual(Enumerable.Range(1, fixture.Total)) && page.Entries.All(row => row.ResolvedMedia is null), "RTVE lost inventory positions or materialized media grants.");
                if (mode == "pages") Assert(fixture.Api == 3, "RTVE did not read all programme pages.");
                if (mode == "duplicates") Assert(page.Entries[0].Id == page.Entries[1].Id, "RTVE deduplicated repeated programme occurrences.");
                if (mode == "unavailable") Assert(!page.Entries[1].Available && page.Entries[2].Position == 3, "RTVE shifted an unavailable position.");
                if (mode is "parent-paid" or "parent-restricted") Assert(page.Entries.All(entry => !entry.Available) && page.Warnings.Count == 1, "RTVE ignored a programme's explicit access restriction.");
            }
        }
        else if (mode is "drm" or "paid" or "region" or "disabled" or "not-downloadable" or "unpublished")
        {
            var page = await new RtveProvider().ExtractAsync(request, http, default); Assert(page.Media.Count == 0 && page.Warnings.Count == 1, "RTVE fabricated playback despite an access refusal.");
        }
        else await Expect<Exception>(() => new RtveProvider().ExtractAsync(request, http, default));
        Assert(fixture.Transfers == 0, "RTVE discovery transferred media.");
    }
    private static async Task Protected(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture(); var request = Request(files.Directory);
        if (mode == "archive") { request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, "rtve.es:audio 41\n"); var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.Api == 0, "Archived RTVE audio reopened metadata."); }
        else if (mode == "no-overwrite") { request = request with { NoOverwrites = true }; await File.WriteAllTextAsync(Path.Combine(files.Directory, "recording.mp3"), "saved"); var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.Transfers == 0, "Protected RTVE output fetched media."); }
        else
        {
            request = request with { GeoVerification = new() { Proxy = "http://127.0.0.1:1" } };
            if (mode == "unsupported-proxy") { request = request with { Url = new("https://other.example/watch") }; await Expect<NotSupportedException>(() => new NativeEngine(httpFactory: _ => throw new Exception("Unsupported verification reached HTTP.")).InspectAsync(request, default)); }
            else await Expect<NotSupportedException>(() => fixture.Engine.InspectAsync(request, default));
            Assert(fixture.Api == 0 && fixture.Transfers == 0, "A refused verification connection performed source traffic.");
        }
    }
    private static Task Aliases()
    {
        foreach (var text in new[] { "https://www.rtve.es/a/41/", "https://rneaudio.rtve.es/a/41", "http://www.rtve.es/alacarta/audios/radio-fixture/selected/41/", "https://api.rtve.es/api/audios/41.json" }) Assert(RtveAddress.Parse(new(text)).Id == "41", "RTVE audio alias changed its identity.");
        foreach (var text in new[] { "https://www.rtve.es/play/videos/show/clip/41/", "https://www.rtve.es.evil.example/a/41/", "https://www.rtve.es/a/41/?other=1" }) Reject(() => RtveAddress.Parse(new(text)));
        var request = Request("unused"); var configured = NativeExtraArguments.Apply(request, ["--geo-verification-proxy", "http://proxy.example:8080"]);
        Assert(configured.GeoVerification!.Proxy == "http://proxy.example:8080" && !JsonSerializer.Serialize(configured).Contains("proxy.example", StringComparison.Ordinal) && !configured.ToString().Contains("proxy.example", StringComparison.Ordinal), "Verification configuration was not typed/redacted.");
        var analysis = NativeAnalysisArguments.Select(["--geo-verification-proxy", "http://proxy.example:8080"]); Assert(analysis.Count == 2, "Analysis discarded a provider verification option.");
        Assert(YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(request) == YoutubeDl.Engine.Download.DownloadWorkspace.Recipe(configured), "A verification connection invalidated media recovery."); return Task.CompletedTask;
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected failure was not raised."); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return; } throw new Exception("An unsupported RTVE choice was accepted."); }
}
