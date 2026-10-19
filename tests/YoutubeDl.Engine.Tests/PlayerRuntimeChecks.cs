using System.Net;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.PlayerRuntime;
using YoutubeDl.Engine.Providers.YouTube;

internal static class PlayerRuntimeChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Managed player discovery tolerates changed minified names", () => Sync(Discovery)),
        ("Signature and throttling transforms execute exactly once per format", () => Sync(Combined)),
        ("Clear signatures survive native throttling transformation", () => Sync(Clear)),
        ("Unchanged or missing player results never become ready formats", () => Sync(Unresolved)),
        ("A player cannot redirect media to another origin or path", () => Sync(Origins)),
        ("A faulted player preserves clear formats and metadata", () => Sync(Faulted)),
        ("Unknown, ambiguous and oversized player layouts fail explicitly", () => Sync(Layouts)),
        ("Managed player scripts cannot access CLR, files, fetch or browser credentials", () => Sync(Isolation)),
        ("Player execution is bounded by statement limits", () => Sync(StatementLimit)),
        ("Cancellation interrupts executing player code", Cancellation),
        ("Pre-canceled player operations and disposed sessions refuse execution", () => Sync(Lifetime)),
        ("Native YouTube downloading uses transformed addresses and one player session", Download),
        ("Unknown players retain metadata and explicitly unavailable formats", Unknown),
        ("Unexpected player-script redirects never execute another site's code", ScriptRedirect),
        ("Invalid mobile SABR contexts preserve unavailable formats without fetching a player", SabrOnly)
    ];

    // Independently authored mock player. No service script or third-party extraction code.
    internal const string Script = """
        var _yt_player={};
        (function(g){
          g.Address=function(value,flag){this.value=value;};
          g.Address.prototype.set=function(key,value){this.value+=(this.value.indexOf('?')<0?'?':'&')+key+'='+encodeURIComponent(value);return this;};
          g.Address.prototype.toMediaAddress=function(){return this.value.replace(/([?&]n=)([^&#]+)/,function(all,key,value){return key+'solved-'+value;});};
          var helper;
          helper=function(K,q="",N=""){K=new g.Address(K,!0);K.set("alr","yes");if(N)K.set(q,N.split('').reverse().join(''));return K;};
        })(_yt_player);
        """;

    private static void Discovery()
    {
        foreach (var script in new[] { Script, Script.Replace("helper", "$renamed").Replace("Address", "Z9").Replace("toMediaAddress", "x") })
        {
            using var session = new ManagedYouTubePlayerRuntime().CreateSession(script, default);
            var output = session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "abc+", default);
            Assert(output.Query.Contains("sig=%2Bcba") && output.Query.Contains("n=solved-fixture"), "The discovered player did not preserve literal signature values.");
        }
    }

    private static void Combined()
    {
        var response = Response();
        var challenged = response["streamingData"]!["adaptiveFormats"]![3]!;
        challenged["signatureCipher"] = "url=" + Uri.EscapeDataString("https://r1.googlevideo.com/videoplayback?n=once") + "&sp=sig&s=abc";
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script, default);
        var page = YouTubePlayerParser.ParseResponse(response.ToJsonString(), "abcdefghijk", player: session);
        var format = page.ReportedFormats.Single(item => item.Id == "299");
        Assert(format.Availability == FormatAvailability.Ready && format.Url!.Query.Contains("n=solved-once") && !format.Url.Query.Contains("solved-solved"), "Throttling was applied twice after signature resolution.");
        Assert(format.Url!.Query.Contains("sig=cba"), "The signature was not added.");
    }

    private static void Clear()
    {
        var response = Response();
        response["streamingData"]!["adaptiveFormats"]![3]!["signatureCipher"] = "url=" +
            Uri.EscapeDataString("https://r1.googlevideo.com/videoplayback?n=clear") + "&sp=sig&sig=a%2Bb";
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script, default);
        var format = YouTubePlayerParser.ParseResponse(response.ToJsonString(), "abcdefghijk", player: session).ReportedFormats.Single(item => item.Id == "299");
        Assert(format.Url!.Query.Contains("sig=a%2Bb") && format.Url.Query.Contains("n=solved-clear"), "Clear signature was transformed or lost.");
    }

    private static void Unresolved()
    {
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script.Replace("key+'solved-'+value", "key+value").Replace("if(N)K.set(q,N.split('').reverse().join(''));", ""), default);
        var page = YouTubePlayerParser.ParseResponse(YouTubeChecks.FixtureJson, "abcdefghijk", player: session);
        Assert(page.ReportedFormats.Where(item => item.Id is "299" or "248").All(item => item.Availability == FormatAvailability.PlayerChallengeRequired && item.Url is null), "Unsolved output was advertised as ready.");
        Assert(page.ReportedFormats.Single(item => item.Id == "18").Availability == FormatAvailability.Ready, "A failed transform discarded a clear format.");
    }

    private static void Origins()
    {
        foreach (var value in new[] { "https://example.com/videoplayback?n=changed", "https://r1.googlevideo.com/changed?n=changed", "https://r1.googlevideo.com:444/videoplayback?n=changed", "https://r1.googlevideo.com/videoplayback?n=changed#fragment" })
        {
            var script = Script.Replace("return this.value.replace", "return '" + value + "';return this.value.replace");
            using var session = new ManagedYouTubePlayerRuntime().CreateSession(script, default);
            Throws<InvalidDataException>(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "", default));
            Throws<ObjectDisposedException>(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "", default));
        }
        using var valid = new ManagedYouTubePlayerRuntime().CreateSession(Script, default);
        Throws<InvalidDataException>(() => valid.Transform(new Uri("https://example.com/media"), "sig", "", default));
        Throws<InvalidDataException>(() => valid.Transform(new Uri("https://r1.googlevideo.com/videoplayback"), "sig&other", "", default));
    }

    private static void Faulted()
    {
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script.Replace("return this.value.replace", "throw new Error('fixture failure');return this.value.replace"), default);
        var page = YouTubePlayerParser.ParseResponse(YouTubeChecks.FixtureJson, "abcdefghijk", player: session);
        Assert(page.Title.Length > 0 && page.ReportedFormats.Single(item => item.Id == "18").Availability == FormatAvailability.Ready &&
            page.ReportedFormats.Where(item => item.Id is "299" or "248").All(item => item.Availability == FormatAvailability.PlayerChallengeRequired), "A faulted interpreter discarded metadata or exposed a failed URL.");
    }

    private static void Layouts()
    {
        var runtime = new ManagedYouTubePlayerRuntime();
        Throws<NotSupportedException>(() => runtime.CreateSession("var player={};", default));
        Throws<NotSupportedException>(() => runtime.CreateSession(Script.Replace("})(_yt_player);", "helper2=function(K,q,N){K=new g.Address(K,!0);K.set(\"alr\",\"yes\");return K;};})(_yt_player);"), default));
        Throws<InvalidDataException>(() => runtime.CreateSession(new string('x', ManagedYouTubePlayerRuntime.MaximumScriptBytes + 1), default));
    }

    private static void Isolation()
    {
        const string check = "if(typeof System!=='undefined'||typeof importNamespace!=='undefined'||typeof require!=='undefined'||typeof fetch!=='undefined'||document.cookie!=='')throw new Error('Host access');";
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script.Replace("var helper;", check + "var helper;"), default);
        Assert(session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "", default).Host == "r1.googlevideo.com", "The isolated player did not run.");
    }

    private static void StatementLimit()
    {
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script.Replace("return this.value.replace", "while(true){};return this.value.replace"), default);
        Throws<NotSupportedException>(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "", default));
    }

    private static async Task Cancellation()
    {
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script.Replace("return this.value.replace", "while(true){};return this.value.replace"), default);
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(30);
        await Expect<OperationCanceledException>(() => Task.Run(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback?n=fixture"), "sig", "", cancel.Token)));
    }

    private static void Lifetime()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Throws<OperationCanceledException>(() => new ManagedYouTubePlayerRuntime().CreateSession(Script, cancel.Token));
        using var session = new ManagedYouTubePlayerRuntime().CreateSession(Script, default);
        Throws<OperationCanceledException>(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback"), "sig", "", cancel.Token));
        session.Dispose();
        Throws<ObjectDisposedException>(() => session.Transform(new Uri("https://r1.googlevideo.com/videoplayback"), "sig", "", default));
    }

    private static async Task Download()
    {
        var response = Response();
        var format = response["streamingData"]!["formats"]![0]!.AsObject();
        format.Remove("url");
        format["signatureCipher"] = "url=" + Uri.EscapeDataString("https://r1.googlevideo.com/videoplayback?n=download") + "&sp=sig&s=abc";
        var requests = new List<Uri>();
        var engine = Engine(response, Script, requests);
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", "native-player-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new Uri("https://youtu.be/abcdefghijk"), OutputDirectory = directory, FormatId = "18", Retries = 0 }, null, null, default);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "combined", "Transformed native download did not finish.");
            Assert(requests.Count == 3 && requests.Count(url => url.AbsolutePath.EndsWith("base.js")) == 1, "The player was not fetched once.");
            Assert(requests.Last().Query.Contains("sig=cba") && requests.Last().Query.Contains("n=solved-download") && !requests.Last().Query.Contains("solved-solved"), "The transfer did not use the resolved URL.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task Unknown()
    {
        var requests = new List<Uri>();
        var info = await Engine(Response(), "var unknown={};", requests).InspectAsync(new NativeDownloadRequest { Url = new Uri("https://youtube.com/watch?v=abcdefghijk"), OutputDirectory = Path.GetTempPath() }, default);
        Assert(info.Id == "abcdefghijk" && info.Formats.Count == 7 && info.Warnings.Any(warning => warning.Contains("player version")), "An unknown player lost metadata or its limitation.");
        Assert(info.Formats.Where(item => item.Id is "299" or "248").All(item => item.Availability == FormatAvailability.PlayerChallengeRequired), "An unknown player exposed challenged formats.");
    }

    private static async Task ScriptRedirect()
    {
        using var http = new MediaHttp(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("base.js")
            ? new(HttpStatusCode.OK) { Content = new StringContent(Script), RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/player.js") }
            : new(HttpStatusCode.OK) { Content = new StringContent(YouTubeChecks.FixtureHtml) }));
        var provider = new YouTubeProvider(new ForbiddenRuntime());
        var page = await provider.ExtractAsync(new NativeDownloadRequest { Url = new Uri("https://youtube.com/watch?v=abcdefghijk"), OutputDirectory = Path.GetTempPath() }, http, default);
        Assert(page.ReportedFormats.Single(item => item.Id == "299").Availability == FormatAvailability.PlayerChallengeRequired && page.Warnings.Any(warning => warning.Contains("player version")), "An unexpected script redirect was accepted.");
        foreach (var url in new[] { "https://user@www.youtube.com/s/player/x/base.js", "https://www.youtube.com:444/s/player/x/base.js", "https://www.youtube.com/s/player/x/base.js#fragment" })
            Assert(YouTubePageJson.ReadPlayerScriptUrl("{\"jsUrl\":" + System.Text.Json.JsonSerializer.Serialize(url) + "}") is null, "An invalid script address was advertised.");
    }

    private sealed class ForbiddenRuntime : IYouTubePlayerRuntime
    { public IYouTubePlayerSession CreateSession(string playerScript, CancellationToken ct) => throw new InvalidOperationException("Unexpected script execution."); }

    private static async Task SabrOnly()
    {
        var response = Response();
        foreach (var group in new[] { "formats", "adaptiveFormats" }) foreach (var format in response["streamingData"]![group]!.AsArray())
        { format!.AsObject().Remove("url"); format.AsObject().Remove("signatureCipher"); }
        var requests = new List<Uri>();
        var info = await Engine(response, Script, requests).InspectAsync(new NativeDownloadRequest { Url = new Uri("https://youtube.com/watch?v=abcdefghijk"), OutputDirectory = Path.GetTempPath() }, default);
        Assert(requests.Count == 2 && requests.All(url => !url.AbsolutePath.EndsWith("base.js")) && info.Formats.All(item => item.Availability == FormatAvailability.AdaptiveTransportRequired), "An invalid mobile context or player execution falsely enabled SABR formats.");
    }

    private static NativeEngine Engine(JsonNode response, string script, List<Uri> requests) => new(playerRuntime: new ManagedYouTubePlayerRuntime(), httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var url = request.RequestUri!;
        requests.Add(url);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(url.Host == "r1.googlevideo.com" ? "combined" : url.AbsolutePath.EndsWith("base.js") ? script :
            "<script>ytInitialPlayerResponse=" + response.ToJsonString() + ";</script><script>{\"jsUrl\":\"/s/player/native/player_es6.vflset/en_US/base.js\"}</script>") };
    })));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static JsonNode Response() => JsonNode.Parse(YouTubeChecks.FixtureJson)!;
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
