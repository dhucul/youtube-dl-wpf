using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class AttestationChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native attestation extraction decodes escaped page JSON without execution", () => Sync(Page)),
        ("Malformed and ambiguous challenges fail explicitly", () => Sync(Invalid)),
        ("Challenge programs and interpreter text enforce byte limits", () => Sync(Limits)),
        ("Attestation interpreter addresses retain strict service boundaries", () => Sync(Addresses)),
        ("Page and inline challenge material require no additional HTTP request", Embedded),
        ("Native attestation API requests use the current public client context", Api),
        ("Interpreter redirects and oversized HTTP bodies are rejected", Interpreter),
        ("Canceled attestation requests do not contact the service", Cancellation)
    ];

    private const string Response = """
        {"challenge":"guest-challenge","bgChallenge":{"globalName":"guestVm","program":"fixture-program","interpreterHash":"fixture-hash","interpreterUrl":{"privateDoNotAccessOrElseTrustedResourceUrlWrappedValue":"//www.google.com/js/th/fixture.js"},"clientExperimentsStateBlob":"fixture-experiments"}}
        """;
    private static string Html(string json) => "<script>window.ytAtN({'R': '" + string.Concat(json.Select(character => character <= 255 ? "\\x" + ((int)character).ToString("X2") : "\\u" + ((int)character).ToString("X4"))) + "'});</script>";

    private static void Page()
    {
        var challenge = YouTubeAttestationParser.ReadPage(Html(Response))!;
        Assert(challenge.GlobalName == "guestVm" && challenge.Program == "fixture-program" && challenge.Challenge == "guest-challenge" &&
            challenge.InterpreterUrl!.AbsoluteUri == "https://www.google.com/js/th/fixture.js" && challenge.ClientExperimentsStateBlob == "fixture-experiments", "Page challenge fields were changed.");
        Assert(YouTubeAttestationParser.ReadPage("<script>window.UNRELATED='no challenge';</script>") is null, "An unrelated page was treated as a challenge.");
        var inline = JsonNode.Parse(Response)!;
        inline["bgChallenge"]!.AsObject().Remove("interpreterUrl");
        inline["bgChallenge"]!["interpreterJavascript"] = new JsonObject { ["privateDoNotAccessOrElseSafeScriptWrappedValue"] = "/* π and 'quoted'; { braces } */ throw new Error('fixture');" };
        Assert(YouTubeAttestationParser.ReadPage(Html(inline.ToJsonString()))!.InterpreterScript == inline["bgChallenge"]!["interpreterJavascript"]!["privateDoNotAccessOrElseSafeScriptWrappedValue"]!.GetValue<string>(), "Nested escapes or Unicode script text were decoded twice.");
    }

    private static void Invalid()
    {
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadPage(Html(Response) + Html(Response)));
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadPage("<script>ytAtN({'R':'\\xZZ'});</script>"));
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(Response.Replace("\"globalName\":\"guestVm\"", "\"globalName\":\"guestVm\",\"globalName\":\"otherVm\"")));
        foreach (var path in new[] { "globalName", "program", "interpreterHash" })
        {
            var node = JsonNode.Parse(Response)!;
            node["bgChallenge"]![path] = new JsonArray("wrong type");
            Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(node.ToJsonString()));
        }
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(Response.Replace("guestVm", "invalid-name")));
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse("{\"challenge\":\"fixture\"}"));
    }

    private static void Limits()
    {
        var node = JsonNode.Parse(Response)!;
        node["bgChallenge"]!["program"] = new string('é', 140 * 1024);
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(new string(' ', YouTubeAttestationParser.MaximumResponseBytes + 1)));
        node = JsonNode.Parse(Response)!;
        node["bgChallenge"]!.AsObject().Remove("interpreterUrl");
        node["bgChallenge"]!["interpreterJavascript"] = new JsonObject { ["privateDoNotAccessOrElseSafeScriptWrappedValue"] = new string('x', YouTubeAttestationParser.MaximumInterpreterBytes + 1) };
        Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(node.ToJsonString()));
    }

    private static void Addresses()
    {
        foreach (var url in new[] { "http://www.google.com/js/th/fixture.js", "https://www.google.com.evil.example/js/th/fixture.js", "https://www.google.com:444/js/th/fixture.js", "https://user@www.google.com/js/th/fixture.js", "https://www.google.com/other/fixture.js", "https://www.google.com/js/th/fixture.js#fragment", "file:///js/th/fixture.js" })
        {
            var node = JsonNode.Parse(Response)!;
            node["bgChallenge"]!["interpreterUrl"]!["privateDoNotAccessOrElseTrustedResourceUrlWrappedValue"] = url;
            Throws<InvalidDataException>(() => YouTubeAttestationParser.ReadResponse(node.ToJsonString()));
        }
    }

    private static async Task Embedded()
    {
        using var http = new MediaHttp(new Handler(_ => throw new InvalidOperationException("Unexpected HTTP request.")));
        var challenge = await YouTubeAttestationClient.GetAsync(Html(Response), http, default);
        Assert(challenge.Program == "fixture-program", "The embedded challenge was not used.");
        var inline = challenge with { InterpreterUrl = null, InterpreterScript = "throw new Error('must not execute');" };
        Assert(await YouTubeAttestationClient.LoadInterpreterAsync(inline, http, default) == inline.InterpreterScript, "Loading challenge text executed or changed it.");
    }

    private static async Task Api()
    {
        var count = 0;
        using var http = new MediaHttp(new Handler(request =>
        {
            count++;
            Assert(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://www.youtube.com/youtubei/v1/att/get?prettyPrint=false", "The wrong challenge endpoint was contacted.");
            Assert(request.Headers.GetValues("Origin").Single() == "https://www.youtube.com", "The request origin was lost.");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body.RootElement.GetProperty("engagementType").GetString() == "ENGAGEMENT_TYPE_UNBOUND" &&
                body.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientVersion").GetString() == "2.fixture", "The current challenge context was not used.");
            return new(HttpStatusCode.OK) { Content = new StringContent(Response) };
        }));
        var challenge = await YouTubeAttestationClient.GetAsync("<script>{\"INNERTUBE_CLIENT_VERSION\":\"2.fixture\"}</script>", http, default);
        Assert(count == 1 && challenge.GlobalName == "guestVm", "The API response was not parsed.");
        await Expect<InvalidDataException>(() => YouTubeAttestationClient.GetAsync("<html>no version</html>", http, default));
        Assert(count == 1, "An invalid client context started a request.");
    }

    private static async Task Interpreter()
    {
        var challenge = YouTubeAttestationParser.ReadResponse(Response);
        using var valid = new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("fixture interpreter") }));
        Assert(await YouTubeAttestationClient.LoadInterpreterAsync(challenge, valid, default) == "fixture interpreter", "Valid interpreter text was lost.");
        using var redirected = new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("other origin"), RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/script.js") }));
        await Expect<InvalidDataException>(() => YouTubeAttestationClient.LoadInterpreterAsync(challenge, redirected, default));
        using var oversized = new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', YouTubeAttestationParser.MaximumInterpreterBytes + 1)) }));
        await Expect<InvalidDataException>(() => YouTubeAttestationClient.LoadInterpreterAsync(challenge, oversized, default));
        using var excessive = new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', YouTubeAttestationParser.MaximumResponseBytes + 1)) }));
        await Expect<InvalidDataException>(() => YouTubeAttestationClient.GetAsync("{\"INNERTUBE_CLIENT_VERSION\":\"2.fixture\"}", excessive, default));
    }

    private static async Task Cancellation()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var http = new MediaHttp(new Handler(_ => throw new InvalidOperationException("Canceled request contacted the service.")));
        await Expect<OperationCanceledException>(() => YouTubeAttestationClient.GetAsync(Html(Response), http, cancel.Token));
        await Expect<OperationCanceledException>(() => YouTubeAttestationClient.LoadInterpreterAsync(YouTubeAttestationParser.ReadResponse(Response), http, cancel.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
