using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class EncodedPlayerChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => Enumerable.Range(1, 7).Select(version => ("Encoded player address version " + version, (Func<Task>)(() => Vectors(version))))
        .Concat(new[] { "partial", "clear", "invalid", "scope", "cancel", "metadata", "preview", "drm", "foreign", "truncated", "preference", "fresh-page", "ph-fresh-page", "unrelated-cache", "explicit-referrer" }.Select(mode => ("Encoded player " + mode, (Func<Task>)(() => Contract(mode)))));
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static JsonElement[] ReadVectors()
    { using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xplayer-addresses.json"))); return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray(); }
    private static Task Vectors(int version)
    {
        foreach (var row in ReadVectors().Where(row => row.GetProperty("Version").GetInt32() == version))
            Assert(XplayerLinkDecoder.Decode(row.GetProperty("Encoded").GetString()!, new("https://xhamster.com/videos/owned"), default) == row.GetProperty("Expected").GetString(), "Independent unsigned seed/address vector changed.");
        return Task.CompletedTask;
    }
    private static async Task Contract(string mode)
    {
        var row = ReadVectors().First(); var encoded = row.GetProperty("Encoded").GetString()!; var expected = row.GetProperty("Expected").GetString()!; var page = new Uri("https://xhamster.com/videos/owned");
        if (mode is "fresh-page" or "ph-fresh-page" or "unrelated-cache" or "explicit-referrer")
        {
            var host = mode == "fresh-page" ? "xhamster.com" : mode is "ph-fresh-page" or "explicit-referrer" ? "www.pornhub.com" : "owned.example";
            var reference = mode == "explicit-referrer" ? "https://publisher.owned.example/selected" : "https://" + host + "/watch";
            var seen = false; using var policyHttp = new MediaHttp(new SabrFixture.Handler(request =>
            { seen = true; Assert((request.Headers.CacheControl?.NoCache == true) == (mode != "unrelated-cache"), "Fresh signed-page policy was omitted or escaped to an unrelated host."); Assert(request.Headers.Referrer?.AbsoluteUri == (mode is "explicit-referrer" or "unrelated-cache" ? reference : null), "Automatic navigation or explicit caller referrer changed incorrectly."); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<video src='https://media.owned.example/clip.mp4'></video>") }); }), new() { AllowAutoRedirect = false, Referer = reference });
            await new GenericPageProvider().ExtractAsync(new() { Url = new("https://" + host + "/watch"), OutputDirectory = Path.GetTempPath(), Referer = mode == "explicit-referrer" ? reference : null }, policyHttp, default); Assert(seen, "Page policy was not exercised."); return;
        }
        if (mode == "partial")
        {
            var wrapped = "https://video-fixture.xhcdn.com/" + encoded + ",144p/master.m3u8";
            Assert(XplayerLinkDecoder.Decode(wrapped, page, default) == "https://video-fixture.xhcdn.com/" + expected + ",144p/master.m3u8", "The first wrapped path segment or comma suffix changed."); return;
        }
        if (mode == "clear") { Assert(XplayerLinkDecoder.Decode(expected, page, default) == expected, "A clear source changed."); return; }
        if (mode == "scope") { await ProviderFamiliesChecks.Expect<NotSupportedException>(() => Task.FromResult(XplayerLinkDecoder.Decode(encoded, new("https://other.example/watch"), default))); return; }
        if (mode == "cancel") { using var stop = new CancellationTokenSource(); stop.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => Task.FromResult(XplayerLinkDecoder.Decode(encoded, page, stop.Token))); return; }
        if (mode == "invalid")
        {
            foreach (var value in new[] { encoded[..^1], "0100000000", "010000000000", new string('a', 32780) }) await ProviderFamiliesChecks.Expect<Exception>(() => Task.FromResult(XplayerLinkDecoder.Decode(value, page, default)));
            await ProviderFamiliesChecks.Expect<NotSupportedException>(() => Task.FromResult(XplayerLinkDecoder.Decode("08" + encoded[2..], page, default))); return;
        }
        var f = new Fixture { Mode = mode }; using var http = f.Http();
        if (mode is "foreign" or "truncated") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new GenericPageProvider().ExtractAsync(f.Request(Path.GetTempPath()), http, default)); Assert(f.MediaTransfers == 0, "An invalid decoded destination was requested."); return; }
        var result = await new GenericPageProvider().ExtractAsync(f.Request(Path.GetTempPath()), http, default);
        if (mode == "preference") { var plan = YoutubeDl.Engine.Download.MediaDownloadPlan.Create(result, f.Request(Path.GetTempPath())); Assert(plan.Primary.Kind == MediaKind.Hls && result.Media.Single(m => m.Kind == MediaKind.Progressive).IsFallback, "Automatic selection ignored the player's preferred HLS delivery."); return; }
        if (mode is "preview" or "drm") { Assert(result.Media.Count == 0 && result.Warnings.Count > 0 && f.MediaTransfers == 0, "Restricted player data fell through to decoding or advertising."); return; }
        Assert(result.DurationSeconds == 22 && result.Media.Count == 1 && result.Media[0].Url.AbsoluteUri == expected && result.Media[0].Height == 240 && f.MediaTransfers == 0, "Native metadata did not expose the decoded selected source and duration without a media transfer.");
    }
    internal sealed class Fixture
    {
        internal string Mode = "metadata"; internal byte[] Media = [1, 2, 3]; internal int MediaTransfers; internal int Duration = 22; internal Dictionary<string, byte[]> Segments = [];
        internal NativeDownloadRequest Request(string output) => new() { Url = new("https://xhamster.com/videos/owned"), OutputDirectory = output, FileName = "recording", Retries = 0, NoCache = true, NoMtime = true };
        internal NativeEngine Engine => new([new GenericPageProvider()], _ => Http());
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["X-Private-Fixture"] = "owned" } });
        private string Html()
        {
            var encoded = ReadVectors()[0].GetProperty("Encoded").GetString()!;
            if (Mode == "truncated") encoded = encoded[..^1];
            // Version2 with a zero state has a zero byte mask: independent malicious-address vector.
            if (Mode == "foreign") encoded = "0200000000" + Convert.ToHexString("https://other.example/foreign.mp4"u8);
            var hls = "0200000000" + Convert.ToHexString("https://video-fixture.xhcdn.com/master.m3u8"u8);
            var source = Mode == "hls" ? hls : encoded;
            var selected = Mode == "hls" ? "hls:{h264:{url:'" + source + "'}}" : "standard:{h264:[{url:'" + source + "',quality:'240p'}]}";
            if (Mode == "preference") selected += ",hls:{h264:{url:'" + hls + "'}}";
            return "<meta property='og:title' content='Owned non-explicit clip'><script>window.initials={xplayerSettings:{duration:" + Duration + "," + (Mode == "preview" ? "isPreview:true," : Mode == "drm" ? "drm:true," : "") + "sources:{" + selected + "}}};var advertising='https://ads.example/advert.mp4';</script>";
        }
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
                if (url.Host == "xhamster.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture.Html()) });
                Assert(url.Host == "video-fixture.xhcdn.com" && !request.Headers.Contains("X-Private-Fixture"), "Decoded playback escaped its publisher or forwarded private page headers.");
                Assert(request.Headers.CacheControl is null && request.Headers.Pragma.Count == 0, "Page refresh headers leaked into CDN transfers.");
                if (fixture.Mode == "preference" && url.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal)) throw new Exception("Preferred adaptive delivery attempted the unavailable direct fallback.");
                if (!url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal)) fixture.MediaTransfers++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Segments.GetValueOrDefault(Path.GetFileName(url.AbsolutePath)) ?? fixture.Media) });
            }
        }
    }
}
