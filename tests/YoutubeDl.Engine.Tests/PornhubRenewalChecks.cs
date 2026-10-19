using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PornhubRenewalChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "401", "403", "410", "412", "twice", "exhausted", "404", "path", "quality", "duration", "preview", "drm", "foreign", "redirect", "foreign-redirect", "page-redirect", "wrong-page", "cancel", "concurrent", "segment", "child", "key", "init", "stale-refresh", "timeline", "global-scope", "job-bound" }
        .Select(mode => ("Pornhub renewal " + mode, (Func<Task>)(() => Contract(mode))));
    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async Task Contract(string mode)
    {
        var fixture = new Fixture { Mode = mode };
        using var owner = fixture.Http();
        var page = await new GenericPageProvider().ExtractAsync(fixture.Request(Path.GetTempPath()), owner, default);
        Assert(page.PlaybackAuthorization is not null, "The public selected player has no renewal binding.");
        using var publicHttp = owner.CreatePublicServiceSession(); using var headers = publicHttp.WithHeaders(page.PlaybackHeaders);
        using var media = headers.WithAuthorization(page.PlaybackAuthorization!);
        if (mode == "global-scope")
        {
            using var ordinary = new RenewableMediaAuthorization(page, _ => throw new Exception("Unrelated 410 renewed."));
            using var plain = headers.WithAuthorization(ordinary);
            using var response = await plain.SendAsync(new(HttpMethod.Get, page.Media[0].Url), default);
            Assert(response.StatusCode == HttpStatusCode.Gone && fixture.Pages == 1, "Pornhub statuses escaped into other sessions."); return;
        }
        var root = page.Media[0].Url;
        var fails = new[] { "exhausted", "404", "path", "quality", "duration", "preview", "drm", "foreign", "foreign-redirect", "wrong-page", "cancel", "timeline", "job-bound" }.Contains(mode);
        try
        {
            if (mode == "job-bound")
            {
                for (var i = 0; i < 20; i++)
                {
                    var lease = await page.PlaybackAuthorization!.AuthorizeAsync(root, default);
                    await page.PlaybackAuthorization.RenewAsync(root, lease.Generation, default);
                }
            }
            else if (mode is "child" or "segment" or "key" or "init" or "concurrent" or "stale-refresh" or "timeline")
            {
                var master = await media.GetDocumentAsync(root, 8192, default); await media.ObservePlaybackManifestAsync(master, default);
                var child = new Uri(root, "child.m3u8?generation=1");
                var document = await media.GetDocumentAsync(child, 8192, default); await media.ObservePlaybackManifestAsync(document, default);
                if (mode != "child")
                {
                    var path = mode is "key" or "init" ? mode + ".bin" : "part1.ts";
                    var target = new Uri(root, path + "?generation=1");
                    var identity = media.RecoveryIdentity(target);
                    var count = mode == "concurrent" ? 8 : 1;
                    var results = await Task.WhenAll(Enumerable.Range(0, count).Select(async _ =>
                    { using var response = await media.SendAsync(new(HttpMethod.Get, target), default); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsByteArrayAsync(); }));
                    Assert(results.All(bytes => bytes.SequenceEqual(Fixture.Payload)), "Renewed segment/key/init bytes changed.");
                    Assert(media.RecoveryIdentity(target) == identity && media.CanonicalizePlaybackUri(target) == target, "Renewal discarded a validated checkpoint identity.");
                }
            }
            else
            {
                using var stop = new CancellationTokenSource(); if (mode == "cancel") stop.Cancel();
                using var request = new HttpRequestMessage(HttpMethod.Get, root); request.Headers.Range = new(2, 5);
                using var response = await media.SendAsync(request, stop.Token); response.EnsureSuccessStatusCode();
                Assert(request.RequestUri == root, "Renewal lost the caller's canonical source.");
            }
            Assert(!fails, "A revoked, changed or repeatedly rejected playback resource was accepted.");
            Assert(fixture.Pages == (mode is "twice" or "stale-refresh" ? 3 : mode is "redirect" or "page-redirect" ? 1 : 2), "Unexpected issuer renewal count.");
            if (mode == "concurrent") Assert(fixture.OldFailures == 8, "Concurrent rejected requests did not share the same generation.");
        }
        catch (Exception ex) when (fails && ex is HttpRequestException or InvalidDataException or OperationCanceledException or NotSupportedException)
        {
            Assert(fixture.Pages <= (mode == "job-bound" ? 13 : 7), "Playback exceeded its bounded page-refresh count.");
            if (mode is "404" or "cancel" or "foreign-redirect") Assert(fixture.Pages == 1, "A non-expiry failure needlessly reread the issuer.");
            if (mode == "exhausted") Assert(fixture.Pages == 7 && fixture.OldFailures == 7, "Repeated expiry did not stop after six renewals.");
            if (mode == "job-bound") Assert(fixture.Pages == 13, "The job-wide renewal bound changed.");
        }
        Assert(fixture.CacheBustedReads == fixture.Pages - 1, "Renewal reused a stale issuer cache key.");
        Assert(fixture.ForeignRequests == 0, "A redirected or changed resource escaped the publisher's CDN.");
    }

    internal sealed class Fixture
    {
        internal static readonly byte[] Payload = [1, 2, 3, 4, 5, 6];
        internal string Mode = "410";
        internal int Pages, OldFailures, ForeignRequests, CacheBustedReads;
        internal Dictionary<string, byte[]>? Media;
        internal readonly Dictionary<string, int> SuccessfulParts = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _allRejected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _canonicalPageRedirected;
        internal NativeDownloadRequest Request(string directory) => new() { Url = new("https://www.pornhub.com/view_video.php?viewkey=ownedfixture"), OutputDirectory = directory, FileName = "recording", Retries = 0, FragmentRetries = 0, ConcurrentFragments = 1, NoCache = true, NoMtime = true };
        internal NativeEngine Engine => new([new GenericPageProvider()], _ => Http());
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["X-Private-Fixture"] = "private", ["Authorization"] = "Bearer fixture" } });
        private string Html(int generation)
        {
            var source = "https://" + (generation == 1 ? "hv-h" : "ev-h") + ".phncdn.com/asset/" + (Mode == "path" && generation > 1 ? "different.m3u8" : "master.m3u8") + "?generation=" + generation;
            return "<meta property='og:title' content='Owned non-explicit test'><script>var flashvars_owned={duration:" + (Mode == "duration" && generation > 1 ? 4 : 3) + "," +
                (generation > 1 && Mode == "preview" ? "isPreview:true," : generation > 1 && Mode == "drm" ? "drm:true," : "") +
                "mediaDefinitions:[{videoUrl:'" + (Mode == "foreign" && generation > 1 ? source.Replace("ev-h.phncdn.com", "foreign.example") : source) + "',quality:'" + (Mode == "quality" && generation > 1 ? "720" : "240") + "'}]};</script>";
        }
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
                Assert(!request.Headers.Contains("Authorization") && !request.Headers.Contains("X-Private-Fixture"), "Private discovery headers reached a public player/CDN.");
                if (url.Host.EndsWith("pornhub.com", StringComparison.Ordinal))
                {
                    Assert(request.Headers.CacheControl?.NoCache == true && request.Headers.Referrer is null, "Issuer navigation reused cache or a self-referrer.");
                    if ((fixture.Mode == "page-redirect" || fixture.Mode == "wrong-page" && fixture.Pages > 0) && Interlocked.Exchange(ref fixture._canonicalPageRedirected, 1) == 0)
                    { var redirect = Response("", HttpStatusCode.Redirect); redirect.Headers.Location = new("https://pornhub.com/view_video.php?viewkey=" + (fixture.Mode == "wrong-page" ? "another" : "ownedfixture")); return redirect; }
                    if (url.Query.Contains("&_native_refresh=", StringComparison.Ordinal)) Interlocked.Increment(ref fixture.CacheBustedReads);
                    return Response(fixture.Html(Interlocked.Increment(ref fixture.Pages)));
                }
                if (!url.Host.EndsWith(".phncdn.com", StringComparison.Ordinal)) { Interlocked.Increment(ref fixture.ForeignRequests); throw new Exception("Unexpected destination."); }
                Assert(request.Headers.Referrer?.Host == "www.pornhub.com", "CDN lost the selected page referrer.");
                var generation = int.TryParse(url.Query.Split('=').Last(), out var declared) ? declared : url.Host == "hv-h.phncdn.com" ? 1 : fixture.Pages; var name = Path.GetFileName(url.AbsolutePath);
                if (fixture.Mode is "redirect" or "foreign-redirect" && url.Host == "hv-h.phncdn.com")
                { var response = Response("", HttpStatusCode.Redirect); response.Headers.Location = new("https://" + (fixture.Mode == "redirect" ? "ev-h.phncdn.com" : "foreign.example") + url.PathAndQuery); return response; }
                var adaptive = fixture.Mode is "child" or "segment" or "key" or "init" or "concurrent" or "stale-refresh" or "timeline" or "media-video" or "media-audio" or "media-midway";
                var target = fixture.Mode == "child" ? "child.m3u8" : fixture.Mode is "key" or "init" ? fixture.Mode + ".bin" : adaptive && fixture.Mode is not ("media-video" or "media-audio") ? "part1.ts" : "master.m3u8";
                var reject = name == target && (generation <= (fixture.Mode is "twice" or "stale-refresh" ? 2 : 1) || fixture.Mode == "exhausted") && fixture.Mode is not ("redirect" or "page-redirect" or "job-bound");
                if (fixture.Mode == "stale-refresh" && generation == 2 && name == "master.m3u8") reject = true;
                if (reject)
                {
                    var failures = Interlocked.Increment(ref fixture.OldFailures);
                    if (fixture.Mode == "concurrent") { if (failures == 8) fixture._allRejected.TrySetResult(); await fixture._allRejected.Task.WaitAsync(ct); }
                    var status = int.TryParse(fixture.Mode, out var code) ? (HttpStatusCode)code : HttpStatusCode.Gone;
                    return Response("Expired fixture link", status);
                }
                if (fixture.Media is { } files && files.TryGetValue(name, out var data))
                { lock (fixture.SuccessfulParts) fixture.SuccessfulParts[name] = fixture.SuccessfulParts.GetValueOrDefault(name) + 1;
                    if (name.EndsWith(".m3u8", StringComparison.Ordinal))
                    {
                        var lines = Encoding.UTF8.GetString(data).Replace("generation=1", "generation=" + generation).Split('\n');
                        data = Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => line.Trim().Length > 0 && !line.StartsWith('#') && !line.Contains('?') ? line.TrimEnd('\r') + "?generation=" + generation : line)));
                    }
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }; }
                if (name == "master.m3u8") return Response("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=426x240\nchild.m3u8?generation=" + generation);
                if (name == "child.m3u8") return Response("#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:0\n" + (fixture.Mode == "key" ? "#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin?generation=" + generation + "\",IV=0x00000000000000000000000000000001\n" : "") +
                    (fixture.Mode == "init" ? "#EXT-X-MAP:URI=\"init.bin?generation=" + generation + "\"\n" : "") + "#EXTINF:" + (fixture.Mode == "timeline" && generation > 1 ? "2" : "3") + ",\npart1.ts?generation=" + generation + "\n#EXT-X-ENDLIST\n");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            }
        }
        private static HttpResponseMessage Response(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
    }
}
