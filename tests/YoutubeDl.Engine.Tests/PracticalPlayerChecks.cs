using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class PracticalPlayerChecks
{
    private static readonly string[] Modes = ["jw", "videojs", "flashvars", "html5", "xplayer", "preview", "drm", "multiple", "computed", "nonliteral", "duplicate-key", "strict-default", "cancel"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Practical common player " + mode, (Func<Task>)(() => Contract(mode))));
    internal static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Contract(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var f = new Fixture { Mode = mode }; var request = f.Request(files.Directory); using var http = f.Http();
        if (mode == "strict-default") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => Task.FromResult(JavaScriptDataLiteral.ObjectArgument("{file:'owned.mp4'}", 0, out _, default))); return; }
        if (mode == "cancel") { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await ProviderFamiliesChecks.Expect<OperationCanceledException>(() => new GenericPageProvider().ExtractAsync(request, http, cancellation.Token)); Assert(f.Transfers == 0, "Canceled player extraction transferred media."); return; }
        if (mode is "multiple" or "computed" or "duplicate-key") { await ProviderFamiliesChecks.Expect<Exception>(() => new GenericPageProvider().ExtractAsync(request, http, default)); Assert(f.Transfers == 0, "Invalid player data transferred a source."); return; }
        var page = await new GenericPageProvider().ExtractAsync(request, http, default);
        if (mode is "preview" or "drm" or "nonliteral") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "A preview/protected/computed player fell back to advertising media."); return; }
        Assert(page.Extractor == "generic:player" && page.Media.Count > 0 && page.Media.All(media => media.Url.AbsolutePath.Contains("/media/", StringComparison.Ordinal)) && page.Media.All(media => !media.Url.AbsolutePath.Contains("advert", StringComparison.Ordinal)), "The selected player was replaced by a page advertisement.");
        if (mode == "html5") Assert(page.Media.Count == 2 && page.Media.Select(media => media.Label).ToHashSet().SetEquals(new[] { "High", "Low" }), "Common high/low player choices were lost.");
    }
    internal sealed class Fixture
    {
        internal string Mode = "jw"; internal int Transfers; internal byte[] Media = "complete owned media"u8.ToArray(); internal Dictionary<string, byte[]> Files = []; internal bool Hls;
        internal NativeDownloadRequest Request(string directory) => new() { Url = new("https://owned.example/watch/one"), OutputDirectory = directory, FileName = "recording", NoCache = true, NoMtime = true, Retries = 0 };
        internal NativeEngine Engine => new([new GenericPageProvider()], _ => Http());
        internal MediaHttp Http() => new(new Handler(this), new() { AllowAutoRedirect = false, Headers = { ["X-Owned-Secret"] = "source" } });
        private string Page()
        {
            var source = Hls ? "https://cdn.owned.example/media/master.m3u8" : "https://cdn.owned.example/media/recording.mp4?one=1&two=2";
            var setup = "jwplayer('owned').setup({sources:[{file:'" + source + "',label:'72p'}]" + (Mode == "preview" ? ",isPreview:true" : Mode == "drm" ? ",drm:true" : "") + "});";
            var player = Mode switch
            {
                "videojs" => "videojs('owned',{sources:[{src:'" + source + "',type:'video/mp4'}]});",
                "flashvars" => "var flashvars_123={mediaDefinitions:[{videoUrl:'" + source + "',format:'mp4',quality:'72'}]};",
                "html5" => "html5player.setVideoUrlHigh('" + source + "');html5player.setVideoUrlLow('https://cdn.owned.example/media/low.mp4');",
                "xplayer" => "window.initials={xplayerSettings:{sources:{standard:{mp4:[{url:'" + source + "',quality:'72p'}]}}}};",
                "multiple" => setup + setup,
                "computed" => "jwplayer('owned').setup({file:getSource()});",
                "nonliteral" => "jwplayer('owned').setup(getConfig());",
                "duplicate-key" => "jwplayer('owned').setup({file:'" + source + "',file:'" + source + "'});",
                _ => setup
            };
            return "<meta property=\"og:title\" content=\"Owned common player\"><script>var advertising='https://ads.owned.example/advert.mp4';</script><script>" + player + "</script>";
        }
        private sealed class Handler(Fixture f) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
                if (url.Host == "owned.example") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(f.Page()) });
                Assert(url.Host == "cdn.owned.example" && !request.Headers.Contains("X-Owned-Secret"), "Advertising or private source headers entered public player delivery.");
                if (!url.AbsolutePath.EndsWith(".m3u8", StringComparison.Ordinal)) f.Transfers++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Files.GetValueOrDefault(Path.GetFileName(url.AbsolutePath)) ?? f.Media) });
            }
        }
    }
}
