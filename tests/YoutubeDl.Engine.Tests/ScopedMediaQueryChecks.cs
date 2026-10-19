using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

internal static class ScopedMediaQueryChecks
{
    private static readonly Uri Scope = new("https://media.example/id/video/rev/revision/resource/");
    internal static readonly string[] Parameters = ["Policy=A%2Bb", "Signature=C%2Fd", "Key-Pair-Id=fixture"];
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Scoped media query credentials preserve encoded values and unrelated query fields", () => Sync(Values)),
        ("Scoped media query credentials never cross origins paths or resource-prefix boundaries", () => Sync(Boundaries)),
        ("Scoped media query credentials reject duplicates conflicts and malformed scopes", () => Sync(Conflicts)),
        ("Scoped media HTTP query sessions survive header copies without changing the parent session", Sessions),
        ("Scoped media query credentials are redacted from metadata JSON", () => Sync(Redaction))
    ];
    private static void Values()
    {
        var rule = new ScopedMediaQuery(Scope, Parameters); var url = rule.Apply(new(Scope, "video.m3u8?quality=720"));
        Assert(url.Query == "?quality=720&" + string.Join('&', Parameters), "Signed values or original query parameters changed.");
        Assert(rule.Apply(url) == url, "Credential application duplicated existing parameters.");
    }
    private static void Boundaries()
    {
        var rule = new ScopedMediaQuery(Scope, Parameters);
        foreach (var value in new[] { "https://other.example/id/video/rev/revision/resource/a.ts", "https://media.example/id/other/rev/revision/resource/a.ts",
            "https://media.example/id/video/rev/revision/resources/a.ts", "https://media.example/id/video/rev/revision/resource2/a.ts", "http://media.example/id/video/rev/revision/resource/a.ts",
            "https://media.example:8443/id/video/rev/revision/resource/a.ts", "https://media.example/id/video/rev/revision/resource/../private/a.ts" })
        { var uri = new Uri(value); Assert(rule.Apply(uri) == uri, "Credentials escaped their HTTPS origin/resource scope."); }
    }
    private static void Conflicts()
    {
        var rule = new ScopedMediaQuery(Scope, Parameters);
        foreach (var query in new[] { "?Signature=other", "?Signature=C%2Fd&Signature=C%2Fd", "?Sign%61ture=other" }) Reject<InvalidDataException>(() => rule.Apply(new(Scope, "a.ts" + query)));
        Reject<ArgumentException>(() => new ScopedMediaQuery(new("https://media.example/resource"), Parameters));
        Reject<ArgumentException>(() => new ScopedMediaQuery(Scope, ["Signature=A&B", "Policy=C"]));
        Reject<ArgumentException>(() => new ScopedMediaQuery(Scope, ["Signature=A", "Signature=B"]));
    }
    private static async Task Sessions()
    {
        var urls = new List<Uri>(); using var parent = new MediaHttp(new Handler(request => { urls.Add(request.RequestUri!); return new(HttpStatusCode.OK) { Content = new StringContent("ok") }; }), new() { AllowAutoRedirect = false });
        using var scoped = parent.WithQueries([new(Scope, Parameters)]); using var headers = scoped.WithHeaders(new Dictionary<string, string> { ["Referer"] = "https://page.example/" });
        using var anonymous = headers.CreateAnonymousSession();
        await anonymous.GetStringAsync(new(Scope, "a.ts"), 32, default); await parent.GetStringAsync(new(Scope, "a.ts"), 32, default);
        Assert(urls[0].Query == "?" + string.Join('&', Parameters) && urls[1].Query == "", "Query session copies changed the owning parent or lost scope.");
    }
    private static void Redaction()
    {
        var rule = new ScopedMediaQuery(Scope, Parameters); var page = new PageMetadata { PlaybackQueries = [rule] };
        var json = JsonSerializer.Serialize(page);
        Assert(!json.Contains("Signature") && !json.Contains("Policy") && !rule.ToString().Contains("A%2Bb"), "Playback credentials entered metadata JSON or diagnostic text.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(run(request)); }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
