using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

internal static class WaaChecks
{
    private const string Key = "guest-request-key";
    private static string Data => JsonSerializer.Serialize(new object?[] { "msg1", new object?[] { null, null, null, null, null, "globalThis.fixture='π';" }, null, "fixture-hash", "fixture-program", "guestVm", null, "[null,null,null,null,null,[]]" });
    private static string Wrapped => "[" + Data + "]";
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native WAA challenge readers preserve the current wrapped script format", () => Sync(Plain)),
        ("Scrambled WAA responses decode exactly as UTF-8 without executing scripts", () => Sync(Scrambled)),
        ("Invalid WAA wrappers and interpreter origins remain unusable", () => Sync(Invalid)),
        ("Native integrity responses retain token bytes, lifetime and fallback separately", () => Sync(Integrity)),
        ("Invalid token metadata and excessive WAA bodies fail explicitly", () => Sync(Limits)),
        ("Native WAA RPC requests use exact endpoints and protobuf JSON bodies", Rpc),
        ("WAA redirects and oversized HTTP replies fail before token use", Boundaries),
        ("Invalid or canceled WAA operations do not contact the service", Cancellation)
    ];

    private static void Plain()
    {
        foreach (var json in new[] { Data, Wrapped })
        {
            var challenge = YouTubeWaaClient.ReadChallenge(json, Key);
            Assert(challenge.RequestKey == Key && challenge.MessageId == "msg1" && challenge.Material.GlobalName == "guestVm" &&
                challenge.Material.InterpreterScript == "globalThis.fixture='π';" && challenge.Material.Program == "fixture-program", "The WAA challenge lost fields or confused the request key and message ID.");
        }
    }

    private static void Scrambled()
    {
        var bytes = Encoding.UTF8.GetBytes(Data);
        for (var index = 0; index < bytes.Length; index++) bytes[index] = unchecked((byte)(bytes[index] - 97));
        var response = JsonSerializer.Serialize(new object?[] { null, Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') });
        var challenge = YouTubeWaaClient.ReadChallenge(response, Key);
        Assert(challenge.Material.InterpreterScript == "globalThis.fixture='π';", "The scrambled Unicode script was changed or decoded twice.");
    }

    private static void Invalid()
    {
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge("{}", Key));
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge("[null,\"***\"]", Key));
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge("[[]]", Key));
        var ambiguous = JsonNode.Parse(Data)!;
        ambiguous[1]![0] = "ambiguous";
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge(ambiguous.ToJsonString(), Key));
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge("[\"msg\",null,[\"https://evil.example/interpreter.js\"],null,\"program\",\"guestVm\"]", Key));
        Throws<ArgumentException>(() => YouTubeWaaClient.ReadChallenge(Data, "invalid key"));
    }

    private static void Integrity()
    {
        var response = YouTubeWaaClient.ReadIntegrity("[\"AQID_w\",43200,10,\"BAU\"]");
        Assert(response.IntegrityToken.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 255 }) && response.FallbackToken.AsSpan().SequenceEqual(new byte[] { 4, 5 }) &&
            response.EstimatedTtlSeconds == 43200 && response.MintRefreshThreshold == 10, "WAA integrity bytes or metadata were changed.");
        var fallback = YouTubeWaaClient.ReadIntegrity("[null,43200,null,\"BAU\"]");
        Assert(fallback.IntegrityToken.Length == 0 && fallback.FallbackToken.Length == 2 && fallback.MintRefreshThreshold == 0, "A fallback response was presented as an integrity token.");
    }

    private static void Limits()
    {
        foreach (var json in new[] { "[]", "[null,0]", "[null,-1]", "[null,9999999]", "[null,\"43200\"]", "[null,43200,-1]", "[null,43200,\"bad\"]", "[\"***\",43200]" })
            Throws<InvalidDataException>(() => YouTubeWaaClient.ReadIntegrity(json));
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadIntegrity(new string('x', 65537)));
        Throws<InvalidDataException>(() => YouTubeWaaClient.ReadChallenge(new string('x', YouTubeAttestationParser.MaximumResponseBytes + 1), Key));
    }

    private static async Task Rpc()
    {
        var count = 0;
        using var http = Http(request =>
        {
            count++;
            var url = request.RequestUri ?? throw new InvalidOperationException("No request address.");
            var content = request.Content ?? throw new InvalidOperationException("No request body.");
            Assert(request.Method == HttpMethod.Post && url.Host == "jnn-pa.googleapis.com" &&
                content.Headers.ContentType!.MediaType == "application/json+protobuf" && request.Headers.GetValues("Origin").Single() == "https://www.youtube.com" &&
                request.Headers.Contains("x-goog-api-key") && request.Headers.GetValues("x-user-agent").Single() == "grpc-web-javascript/0.1", "The RPC protocol headers or origin were lost.");
            using var body = JsonDocument.Parse(content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body.RootElement[0].GetString() == Key, "The request key was changed.");
            var create = url.AbsolutePath.EndsWith("/Create");
            Assert(create ? body.RootElement.GetArrayLength() == 1 : body.RootElement.GetArrayLength() == 2 && body.RootElement[1].GetString() == "guest-snapshot", "The RPC body contains unexpected fields.");
            return new(HttpStatusCode.OK) { Content = new StringContent(create ? Wrapped : "[\"AQID\",43200,10]") };
        });
        var challenge = await YouTubeWaaClient.CreateAsync(Key, http, default);
        var integrity = await YouTubeWaaClient.GenerateIntegrityAsync(Key, "guest-snapshot", http, default);
        Assert(count == 2 && challenge.Material.Program == "fixture-program" && integrity.IntegrityToken.Length == 3, "The native RPC responses were not read.");
    }

    private static async Task Boundaries()
    {
        using var automatic = new MediaHttp(new Handler(_ => throw new InvalidOperationException("Automatic redirects must fail before HTTP.")));
        await Expect<ArgumentException>(() => YouTubeWaaClient.CreateAsync(Key, automatic, default));
        using var redirect = Http(_ => new(HttpStatusCode.OK) { Content = new StringContent(Wrapped), RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://evil.example/Create") });
        await Expect<InvalidDataException>(() => YouTubeWaaClient.CreateAsync(Key, redirect, default));
        using var excessive = Http(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', YouTubeAttestationParser.MaximumResponseBytes + 1)) });
        await Expect<InvalidDataException>(() => YouTubeWaaClient.CreateAsync(Key, excessive, default));
        using var excessiveToken = Http(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 65537)) });
        await Expect<InvalidDataException>(() => YouTubeWaaClient.GenerateIntegrityAsync(Key, "guest-snapshot", excessiveToken, default));
    }

    private static async Task Cancellation()
    {
        using var http = Http(_ => throw new InvalidOperationException("An invalid or canceled request contacted the service."));
        await Expect<ArgumentException>(() => YouTubeWaaClient.CreateAsync("bad key", http, default));
        await Expect<ArgumentException>(() => YouTubeWaaClient.GenerateIntegrityAsync(Key, "", http, default));
        await Expect<ArgumentException>(() => YouTubeWaaClient.GenerateIntegrityAsync(Key, new string('x', 65537), http, default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Expect<OperationCanceledException>(() => YouTubeWaaClient.CreateAsync(Key, http, cancel.Token));
        await Expect<OperationCanceledException>(() => YouTubeWaaClient.GenerateIntegrityAsync(Key, "snapshot", http, cancel.Token));
    }

    private static MediaHttp Http(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response), new MediaHttpOptions { AllowAutoRedirect = false });
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
