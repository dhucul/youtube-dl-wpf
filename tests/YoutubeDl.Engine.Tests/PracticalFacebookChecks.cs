using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.Facebook;

internal static class PracticalFacebookChecks
{
    private static readonly string[] Modes = ["metadata", "auxiliary", "urls", "registry", "short", "short-foreign", "identity", "canonical", "duplicate", "foreign", "live", "drm", "denied", "computed", "renewal", "changed-renewal", "archive", "cancel",
        "dash", "dash-only", "dash-fallback", "dash-live", "dash-hls", "dash-drm", "dash-dynamic", "dash-segmented", "dash-foreign", "dash-identity", "dash-duplicate", "dash-duration", "dash-renewal", "dash-changed-renewal", "dash-changed-duration"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Practical Facebook " + mode, (Func<Task>)(() => Contract(mode)))).Concat(FacebookFlowChecks.All);
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
        if (mode is "short-foreign" or "identity" or "canonical" or "duplicate" or "foreign" or "computed" or "dash-drm" or "dash-dynamic" or "dash-segmented" or "dash-foreign" or "dash-identity" or "dash-duplicate" or "dash-duration") { await ProviderFamiliesChecks.Expect<Exception>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "An invalid Facebook grant transferred media."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default);
        if (mode is "live" or "drm" or "denied" or "dash-live" or "dash-hls") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "Ungrantable Facebook playback became downloadable."); return; }
        var dash = mode.StartsWith("dash", StringComparison.Ordinal) && mode != "dash-fallback";
        if (dash)
        {
            Assert(page.Id == Fixture.Id && page.DurationSeconds == 3 && page.Media.Count == (mode == "dash-only" ? 2 : 4), "Facebook lost its static DASH inventory.");
            var video = QualitySelector.Select(page.Media, QualityPreference.Best, null, false);
            var audio = QualitySelector.Select(page.Media, QualityPreference.Best, null, true);
            Assert(video.Id == "dash-video" && video.Height == 720 && video.HasVideo == true && video.HasAudio == false && video.VideoCodec == "avc1.64001f" && !video.IsCompleteMedia &&
                audio.Id == "dash-audio" && audio.HasVideo == false && audio.HasAudio == true && audio.AudioCodec == "mp4a.40.2" && audio.TimelineId == video.TimelineId, "Facebook lost separate full video/audio track facts or quality selection.");
            Assert(page.Media.Where(row => row.IsCompleteMedia).All(row => row.Id is "hd" or "sd"), "Facebook misreported separate DASH tracks as muxed files.");
        }
        else Assert(page.Id == Fixture.Id && page.Media.Count == 2 && page.DurationSeconds == 3 && page.Media.All(row => row.IsCompleteMedia), "Facebook lost complete HD/SD media or source identity.");
        if (mode is "renewal" or "changed-renewal" or "dash-renewal" or "dash-changed-renewal" or "dash-changed-duration")
        {
            using var clean = http.CreatePublicServiceSession(); using var authorized = clean.WithAuthorization(page.PlaybackAuthorization!); var path = Path.Combine(files.Directory, "media.mp4");
            var selected = page.Media.Single(row => row.Id == (dash ? "dash-video" : "hd"));
            if (mode is "changed-renewal" or "dash-changed-renewal" or "dash-changed-duration") await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new FileDownloader(authorized).DownloadAsync(selected.Url, path, null, null, default, 0));
            else { await new FileDownloader(authorized).DownloadAsync(selected.Url, path, null, null, default, 0); Assert(File.ReadAllBytes(path).SequenceEqual(fixture.VideoBytes ?? fixture.Bytes) && fixture.Generation == 2, "Facebook renewal changed media or repeated without bounds."); }
        }
    }
    internal sealed class Fixture
    {
        internal const string Id = "10153231379946729"; internal string Mode = "metadata"; internal int ApiCalls, Transfers, Generation; internal byte[] Bytes = "complete owned video"u8.ToArray();
        internal byte[]? VideoBytes, AudioBytes; internal int Probes;
        internal CancellationTokenSource? CancelProbe;
        internal TaskCompletionSource? RenewalEntered, RenewalRelease;
        internal Func<HttpRequestMessage,CancellationToken,HttpResponseMessage?>? MediaResponse;
        internal IMediaProvider Provider => new FacebookProvider(); internal NativeEngine Engine => new([Provider], _ => Http());
        internal NativeDownloadRequest Request(string output) => new() { Url = new("https://www.facebook.com/reel/" + Id + "/"), OutputDirectory = output, FileName = "recording", NoCache = true, NoMtime = true, Retries = 0 };
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["Authorization"] = "Bearer owned-secret", ["X-Owned-Secret"] = "source" } });
        private string Embed()
        {
            Generation++; if (Mode == "denied") return "<title>Unavailable</title>";
            string Url(string quality)
            {
                var track = quality is "video" or "audio";
                var changed = Mode is "changed-renewal" or "dash-changed-renewal" && Generation > 1 || Mode is "renewal-recovery-asset" or "renewal-recovery-newfacts" && Generation == 2;
                var facts = Uri.EscapeDataString(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { xpv_asset_id = changed ? 222 : 111, duration_s = 3, video_id = Mode == "dash-identity" && track ? "99999999999999999" : Id })));
                return "https://" + (Mode == "foreign" || Mode == "dash-foreign" && track ? "other.example" : "video.owned.fbcdn.net") + "/owned/" + quality + ".mp4?" + (Mode == "renewal-recovery-newfacts" && Generation == 1 ? "" : "efg=" + facts + "&") + "g=" + Generation;
            }
            var proposedDash = Mode.StartsWith("renewal-recovery-", StringComparison.Ordinal) || Mode == "renewal-new-dash";
            var dash = Mode.StartsWith("dash", StringComparison.Ordinal) || Mode.StartsWith("fallback-dash", StringComparison.Ordinal) || Mode.StartsWith("silent-dash", StringComparison.Ordinal) || Mode == "fallback-renewal" || proposedDash && Generation > 1 || Mode is "renewal-concurrent" or "renewal-cancel-waiter";
            var manifestDuration = Mode == "dash-duration" ? "7" : proposedDash && Generation > 1 ? Generation == 2 ? "3.5" : "3.25" : Mode == "dash-changed-duration" && Generation > 1 ? "3.5" : "3";
            var xml = $$"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="{{(Mode == "dash-dynamic" ? "dynamic" : "static")}}" mediaPresentationDuration="PT{{manifestDuration}}S">
                  <Period id="0" duration="PT{{manifestDuration}}S">
                    <AdaptationSet contentType="video" frameRate="30/1">
                      {{(Mode == "dash-drm" ? "<ContentProtection schemeIdUri=\"urn:mpeg:dash:mp4protection:2011\" value=\"cenc\"/>" : "")}}
                      <Representation id="video" mimeType="video/mp4" codecs="avc1.64001f" width="{{(Mode == "dash-width-renewal" && Generation == 2 ? 960 : 1280)}}" height="720" bandwidth="1000000">
                        <BaseURL>{{WebUtility.HtmlEncode(Url("video"))}}</BaseURL>
                        {{(Mode == "dash-segmented" ? "<SegmentList><Initialization sourceURL=\"init.mp4\"/><SegmentURL media=\"part.mp4\"/></SegmentList>" : "<SegmentBase indexRange=\"100-199\" timescale=\"15360\"><Initialization range=\"0-99\"/></SegmentBase>")}}
                      </Representation>
                    </AdaptationSet>
                    <AdaptationSet contentType="audio">
                      <Representation id="{{(Mode == "dash-duplicate" ? "video" : "audio")}}" mimeType="audio/mp4" codecs="mp4a.40.2" bandwidth="128000">
                        <BaseURL>{{WebUtility.HtmlEncode(Url("audio"))}}</BaseURL>
                        <SegmentBase indexRange="100-199" timescale="44100"><Initialization range="0-99"/></SegmentBase>
                      </Representation>
                    </AdaptationSet>
                  </Period>
                </MPD>
                """;
            if(Mode.StartsWith("silent-dash",StringComparison.Ordinal)&&!(Mode=="silent-dash-audio-change"&&Generation>1)||Mode=="dash-audio-disappears"&&Generation>1)
            {
                var document=XDocument.Parse(xml);document.Descendants().Where(node=>node.Name.LocalName=="AdaptationSet"&&(string?)node.Attribute("contentType")=="audio").Remove();xml=document.ToString();
            }
            var dashOnly=Mode is "dash-only" or "dash-audio-disappears"||Mode.StartsWith("silent-dash",StringComparison.Ordinal)&&Mode!="silent-dash-mixed-audio";
            var video = new { video_id = Mode == "identity" ? "99999999999999999" : Id, video_url = "https://www.facebook.com/reel/" + (Mode == "canonical" ? "99999999999999999" : Id) + "/", stream_type = dash ? "dash" : "progressive", is_live_stream = Mode is "live" or "dash-live", is_hls = Mode == "dash-hls", hd_src = dashOnly ? null : Url("hd"), sd_src = dashOnly ? null : Url("sd"), dash_manifest = dash && Mode != "dash-fallback" ? xml : null };
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
                if (url.Host == "www.facebook.com")
                {
                    f.ApiCalls++;
                    if(url.AbsolutePath=="/plugins/video.php"&&f.Generation>0&&f.RenewalRelease is not null)return Delayed();
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(url.AbsolutePath == "/plugins/video.php" ? f.Embed() : "<meta property=\"og:title\" content=\"Owned video\"><meta property=\"og:description\" content=\"Complete owned Facebook video\">") });
                    async Task<HttpResponseMessage> Delayed(){f.RenewalEntered!.TrySetResult();await f.RenewalRelease!.Task.WaitAsync(ct);return new(HttpStatusCode.OK){Content=new StringContent(f.Embed())};}
                }
                if(f.MediaResponse?.Invoke(request,ct) is { } supplied)return Task.FromResult(supplied);
                if (request.Headers.Range is { } range)
                {
                    if(f.CancelProbe is { } cancel){cancel.Cancel();ct.ThrowIfCancellationRequested();}
                    if(f.Mode=="fallback-dash-invalid")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("invalid movie metadata")});
                    if(f.Mode is "fallback-dash-forbidden" or "fallback-dash-missing" or "fallback-dash-gone" or "fallback-unavailable" ||
                        f.Mode=="fallback-sd"&&url.AbsolutePath.EndsWith("/hd.mp4",StringComparison.Ordinal) ||
                        f.Generation>1&&(f.Mode=="fallback-renewal"||f.Mode=="fallback-selected"&&url.AbsolutePath.EndsWith("/hd.mp4",StringComparison.Ordinal)))
                        return Task.FromResult(new HttpResponseMessage(f.Mode=="fallback-dash-missing"?HttpStatusCode.NotFound:f.Mode=="fallback-dash-gone"?HttpStatusCode.Gone:HttpStatusCode.Forbidden));
                    f.Probes++; var bytes = f.Bytes.Length >= 8 && f.Bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8) ? f.Bytes : FacebookFlowChecks.Movie(f.Mode == "probe-renewal-change" && f.Generation > 1 || f.Mode is "renewal-recovery-facts" or "renewal-recovery-newfacts" && f.Generation == 2 ? 720 : url.AbsolutePath.EndsWith("/hd.mp4", StringComparison.Ordinal) ? 546 : 358,audio:!f.Mode.StartsWith("silent-progressive",StringComparison.Ordinal)&&!(f.Mode=="silent-audio-change"&&f.Generation==1));
                    var selected = range.Ranges.Single(); var start = selected.From!.Value; var end = Math.Min(selected.To!.Value, bytes.Length - 1);
                    if (start > end) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
                    var content = new ByteArrayContent(bytes[(int)start..((int)end + 1)]); content.Headers.ContentRange = new(start, end, bytes.Length);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
                }
                if (f.Mode is "renewal" or "changed-renewal" or "dash-renewal" or "dash-changed-renewal" or "dash-changed-duration" or "probe-renewal-change" or "fallback-renewal" or "fallback-selected" or "silent-progressive-renewal" or "silent-dash-renewal" && url.Query.EndsWith("g=1", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                f.Transfers++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(url.AbsolutePath.EndsWith("/video.mp4", StringComparison.Ordinal) ? f.VideoBytes ?? f.Bytes : url.AbsolutePath.EndsWith("/audio.mp4", StringComparison.Ordinal) ? f.AudioBytes ?? f.Bytes : f.Bytes) });
            }
        }
    }
}
