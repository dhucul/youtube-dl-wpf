using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class SimpleDownloadChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "xvideos", "xnxx", "aliases", "case", "scope", "legacy", "archive", "redirect", "unrelated", "distinct" }
        .Select(mode => ("Simple downloader identity " + mode, (Func<Task>)(() => Contract(mode))));
    internal static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async Task Contract(string mode)
    {
        if (mode is "xvideos" or "xnxx")
        {
            var fixture = new Fixture { Site = mode }; using var http = fixture.Http(); var provider = new GenericPageProvider();
            var first = await provider.ExtractAsync(fixture.Request(Path.GetTempPath()), http, default);
            var second = await provider.ExtractAsync(fixture.Request(Path.GetTempPath()), http, default);
            Assert(first.Media[0].Url != second.Media[0].Url && first.Id == second.Id && first.ArchiveIdentity == second.ArchiveIdentity &&
                first.ArchiveIdentity == provider.GetArchiveIdentity(fixture.Request(Path.GetTempPath())) && fixture.Transfers == 0, "Rotating delivery signatures changed the canonical video identity."); return;
        }
        if (mode == "aliases")
        {
            foreach (var site in new[] { "xvideos", "xnxx" })
            {
                var fixture = new Fixture { Site = site }; var first = PublicVideoPageIdentity.Read(fixture.Page);
                var alias = new UriBuilder(fixture.Page) { Host = "fr." + site + ".com", Query = "tracking=other", Path = fixture.Page.AbsolutePath.Split('/')[1] + "/different-slug" }.Uri;
                Assert(first is not null && first == PublicVideoPageIdentity.Read(alias), "Language, slug or tracking changed the selected video identity.");
            }
            return;
        }
        if (mode == "scope")
        {
            foreach (var url in new[] { "https://xvideos.com.attacker.example/video.owned001/title", "https://xvideos.com/videos.owned001/title", "https://xvideos.com/?video=owned001", "https://user@xvideos.com/video.owned001/title", "https://xvideos.com:444/video.owned001/title", "http://xvideos.com/video.owned001/title", "https://xvideos.com/video.owned001/title#another", "https://xnxx.com/video.owned001/title", "https://xvideos.com/video-/title" })
                Assert(PublicVideoPageIdentity.Read(new(url)) is null, "An unrelated or ambiguous URL acquired a canonical video identity.");
            return;
        }
        if (mode == "legacy")
        { Assert(PublicVideoPageIdentity.Read(new("https://www.xvideos.com/video12345/title"))?.Id == "video12345" && PublicVideoPageIdentity.Read(new("https://www.xnxx.com/video12345/title"))?.Id == "video12345", "Legacy numeric video addresses were lost."); return; }
        if (mode is "case" or "distinct")
        {
            var first = PublicVideoPageIdentity.Read(new("https://www.xvideos.com/video.owned001/title"));
            Assert(first != PublicVideoPageIdentity.Read(new(mode == "case" ? "https://www.xvideos.com/video.Owned001/title" : "https://www.xvideos.com/video.other001/title")), "Distinct selected videos shared a case-insensitive identity."); return;
        }
        if (mode == "archive")
        {
            using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture(); var archive = Path.Combine(files.Directory, "archive.txt");
            await File.WriteAllTextAsync(archive, PublicVideoPageIdentity.Read(fixture.Page)!.Record + "\n");
            var result = await fixture.Engine.DownloadAsync(fixture.Request(files.Directory) with { DownloadArchiveFile = archive }, null, null, default);
            Assert(result.Succeeded && result.Skipped && fixture.Pages == 0 && fixture.Transfers == 0, "An archived public video reopened its issuer or CDN."); return;
        }
        if (mode == "redirect")
        {
            var fixture = new Fixture { WrongPage = true }; using var http = fixture.Http();
            await ProviderFamiliesChecks.Expect<InvalidDataException>(() => new GenericPageProvider().ExtractAsync(fixture.Request(Path.GetTempPath()), http, default));
            Assert(fixture.Transfers == 0, "A different redirected video transferred media."); return;
        }
        if (mode == "unrelated")
        {
            var fixture = new Fixture { Site = "publisher" }; using var http = fixture.Http(); var provider = new GenericPageProvider();
            var first = await provider.ExtractAsync(fixture.Request(Path.GetTempPath()), http, default);
            var second = await provider.ExtractAsync(fixture.Request(Path.GetTempPath()), http, default);
            Assert(first.Id != second.Id && first.ArchiveIdentity is null && provider.GetArchiveIdentity(fixture.Request(Path.GetTempPath())) is null, "The site-specific fix redefined unrelated generic media identities.");
        }
    }
    internal sealed class Fixture
    {
        internal string Site = "xvideos"; internal int Pages, Transfers; internal bool WrongPage; internal byte[] Bytes = [1, 2, 3];
        internal Uri Page => new("https://www." + Site + ".com/" + (Site == "xnxx" ? "video-owned001" : "video.owned001") + "/selected");
        internal NativeDownloadRequest Request(string output) => new() { Url = Page, OutputDirectory = output, OutputTemplate = "%(id)s.%(ext)s", Retries = 0, NoCache = true, NoMtime = true };
        internal NativeEngine Engine => new([new GenericPageProvider()], _ => Http());
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false });
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
                if (url.Host == fixture.Page.Host)
                {
                    var generation = Interlocked.Increment(ref fixture.Pages);
                    var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@type"] = "VideoObject", ["name"] = "Owned non-explicit sample", ["duration"] = "PT3S", ["contentUrl"] = "https://cdn.owned.example/source.mp4?signature=" + generation });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StructuredMediaChecks.Html(json)), RequestMessage = fixture.WrongPage ? new(HttpMethod.Get, new Uri(fixture.Page.AbsoluteUri.Replace("owned001", "other001", StringComparison.Ordinal))) : request });
                }
                Assert(url.Host == "cdn.owned.example", "Unexpected identity-test destination."); Interlocked.Increment(ref fixture.Transfers);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) });
            }
        }
    }
}
