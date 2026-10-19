using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Facebook;

internal static class PracticalFacebookChecks
{
    private static readonly string[] Modes = ["metadata", "auxiliary", "urls", "registry", "short", "short-foreign", "identity", "canonical", "duplicate", "foreign", "live", "drm", "denied", "computed", "renewal", "changed-renewal", "archive", "cancel"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Practical Facebook " + mode, (Func<Task>)(() => Contract(mode))));
    internal static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Contract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture { Mode = mode }; var request = fixture.Request(files.Directory); using var http = fixture.Http();
        if (mode == "registry") { Assert(MediaProviderRegistry.CreateDefault().Resolve(request.Url).Info.Id == "facebook", "Facebook was not registered."); return; }
        if (mode == "urls")
        {
            foreach (var url in new[] { "https://www.facebook.com/watch/?v=" + Fixture.Id, "https://www.facebook.com/video.php?v=" + Fixture.Id, "https://www.facebook.com/owned/videos/" + Fixture.Id + "/?mibextid=owned", "https://m.facebook.com/reel/" + Fixture.Id + "/", "https://www.facebook.com/plugins/video.php?href=" + Uri.EscapeDataString(request.Url.AbsoluteUri) }) Assert(FacebookAddress.Parse(new(url)).Id == Fixture.Id, "A common Facebook video/reel URL lost its identity.");
            foreach (var url in new[] { "https://www.facebook.com/watch/?v=11111&v=22222", "https://user@www.facebook.com/reel/" + Fixture.Id, "https://www.facebook.com/profile.php?id=" + Fixture.Id, "https://other.example/reel/" + Fixture.Id }) await ProviderFamiliesChecks.Expect<Exception>(() => Task.FromResult(FacebookAddress.Parse(new(url)))); return;
        }
        if (mode == "archive") { request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, "facebook " + Fixture.Id + "\n"); var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.ApiCalls == 0 && fixture.Transfers == 0, "Archived Facebook video reopened discovery."); return; }
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => fixture.Provider.ExtractAsync(request, http, cancellation.Token)); Assert(fixture.ApiCalls == 0, "Canceled Facebook extraction contacted the network."); return; }
        if (mode.StartsWith("short", StringComparison.Ordinal)) request = request with { Url = new("https://fb.watch/owned123/") };
        if (mode is "short-foreign" or "identity" or "canonical" or "duplicate" or "foreign" or "computed") { await ProviderFamiliesChecks.Expect<Exception>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "An invalid Facebook grant transferred media."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default);
        if (mode is "live" or "drm" or "denied") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "Ungrantable Facebook playback became downloadable."); return; }
        Assert(page.Id == Fixture.Id && page.Media.Count == 2 && page.DurationSeconds == 3 && page.Media.All(row => row.IsCompleteMedia), "Facebook lost complete HD/SD media or source identity.");
        if (mode is "renewal" or "changed-renewal")
        {
            using var clean = http.CreatePublicServiceSession(); using var authorized = clean.WithAuthorization(page.PlaybackAuthorization!); var path = Path.Combine(files.Directory, "media.mp4");
            if (mode == "changed-renewal") await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(authorized).DownloadAsync(page.Media[0].Url, path, null, null, default, 0));
            else { await new FileDownloader(authorized).DownloadAsync(page.Media[0].Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(fixture.Bytes) && fixture.Generation == 2, "Facebook renewal changed media or repeated without bounds."); }
        }
    }
    internal sealed class Fixture
    {
        internal const string Id = "10153231379946729"; internal string Mode = "metadata"; internal int ApiCalls, Transfers, Generation; internal byte[] Bytes = "complete owned video"u8.ToArray();
        internal IMediaProvider Provider => new FacebookProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string output) => new() { Url = new("https://www.facebook.com/reel/" + Id + "/"), OutputDirectory = output, FileName = "recording", NoCache = true, NoMtime = true, Retries = 0 };
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-secret", ["X-Owned-Secret"] = "source" } });
        private string Embed()
        {
            Generation++; if (Mode == "denied") return "<title>Unavailable</title>";
            var facts = Uri.EscapeDataString(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { xpv_asset_id = Mode == "changed-renewal" && Generation > 1 ? 222 : 111, duration_s = 3 })));
            string Url(string quality) => "https://" + (Mode == "foreign" ? "other.example" : "video.owned.fbcdn.net") + "/owned/" + quality + ".mp4?efg=" + facts + "&g=" + Generation;
            var video = new { video_id = Mode == "identity" ? "99999999999999999" : Id, video_url = "https://www.facebook.com/reel/" + (Mode == "canonical" ? "99999999999999999" : Id) + "/", stream_type = "progressive", is_live_stream = Mode == "live", hd_src = Url("hd"), sd_src = Url("sd") };
            var config = new { video_id = Id, videoData = new[] { video }, videoLicenseUriMap = Mode == "drm" ? new Dictionary<string, string> { ["owned"] = "https://other.example/license" } : [] };
            object[] instance = ["owned-instance", new[] { "VideoConfig" }, new[] { config }, 1]; var root = new { instances = Mode == "duplicate" ? new[] { instance, instance } : new[] { instance } };
            return (Mode == "auxiliary" ? "<script>var s=(new ServerJS());s.handle({\"define\":[]});</script>" : "") + "<script>var s=(new ServerJS());s.handle(" + JsonSerializer.Serialize(root) + (Mode == "computed" ? "+danger()" : "") + ");</script>";
        }
        private sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!; Assert(request.Headers.Authorization is null && !request.Headers.Contains("X-Owned-Secret"), "Source secrets entered Facebook public playback.");
                if (url.Host == "fb.watch") { f.ApiCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new(f.Mode == "short-foreign" ? "https://other.example/foreign" : "https://www.facebook.com/reel/" + Id + "/") } }); }
                if (url.Host == "www.facebook.com") { f.ApiCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(url.AbsolutePath == "/plugins/video.php" ? f.Embed() : "<meta property=\"og:title\" content=\"Owned video\"><meta property=\"og:description\" content=\"Complete owned Facebook video\">") }); }
                if ((f.Mode == "renewal" || f.Mode == "changed-renewal") && url.Query.EndsWith("g=1", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                f.Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Bytes) });
            }
        }
    }
}
