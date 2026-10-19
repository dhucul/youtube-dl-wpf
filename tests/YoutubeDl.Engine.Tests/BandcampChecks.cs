using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Bandcamp;

internal static class BandcampChecks
{
    private const string Json = """
    {"id":12345,"artist":"Artist","current":{"title":"Album","about":"Description"},"item_type":"album","trackinfo":[
      {"track_id":111,"track_num":1,"title":"First","duration":12.5,"title_link":"/track/first","file":{"mp3-128":"https://cdn.example/first"}},
      {"track_id":222,"track_num":2,"title":"Second","duration":20,"title_link":"/track/second","file":null}
    ]}
    """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Bandcamp albums retain exposed audio tracks and unavailable-track diagnostics", () => Sync(Metadata)),
        ("Bandcamp invalid URLs, positions, and track limits fail explicitly", () => Sync(Invalid)),
        ("Native Bandcamp album downloads preserve completed tracks across unavailable items", Download),
        ("Native Bandcamp track URLs resolve one selected audio item", Single)
    ];
    private static Uri Album => new("https://artist.bandcamp.com/album/fixture");
    private static void Metadata()
    {
        var page = BandcampProvider.ParseTralbum(Json, Album);
        var first = page.Entries[0].ResolvedMedia!;
        Assert(page.IsPlaylist && page.Entries.Count == 2 && first.Uploader == "Artist" && first.Media.Single().IsAudioOnly && first.Media[0].Bandwidth == 128000 && first.DurationSeconds == 12.5,
            "Album/audio metadata lost.");
        Assert(page.Entries[1].ResolvedMedia!.Warnings.Single().Contains("public stream"), "Unavailable track diagnostics were lost.");
    }
    private static void Invalid()
    {
        Throws<InvalidDataException>(() => BandcampProvider.ParseTralbum(Json, Album, 1));
        Throws<InvalidDataException>(() => BandcampProvider.ParseTralbum(Json.Replace("https://cdn.example/first", "file:///private.mp3"), Album));
        Throws<InvalidDataException>(() => BandcampProvider.ParseTralbum(Json.Replace("\"track_num\":2", "\"track_num\":1"), Album));
        var provider = new BandcampProvider();
        Assert(!provider.CanHandle(new Uri("https://artist.bandcamp.com.evil.example/album/fixture")), "Spoofed host accepted.");
    }
    private static async Task Download()
    {
        using var files = new Files();
        var requests = 0;
        var engine = Engine(Json, () => requests++);
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = Album, OutputDirectory = files.Directory, Retries = 0 }, null, null, default);
        Assert(!result.Succeeded && result.OutputPaths.Count == 1 && result.Failures.Single().Position == 2 && File.ReadAllText(result.OutputPaths[0]) == "audio bytes" && requests == 2,
            "Unavailable album track discarded completed audio or caused re-extraction.");
    }
    private static async Task Single()
    {
        using var files = new Files();
        var single = "{\"id\":111,\"artist\":\"Artist\",\"current\":{\"title\":\"First\"},\"item_type\":\"track\",\"trackinfo\":[{\"track_id\":111,\"title\":\"First\",\"duration\":12.5,\"file\":{\"mp3-128\":\"https://cdn.example/first\"}}]}";
        var result = await Engine(single).DownloadAsync(new NativeDownloadRequest { Url = new Uri("https://artist.bandcamp.com/track/first"), OutputDirectory = files.Directory, Retries = 0 }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 1 && result.OutputPath!.EndsWith(".mp3"), "Single-track output was treated as an album.");
    }
    private static NativeEngine Engine(string json, Action? onRequest = null) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        onRequest?.Invoke();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host.EndsWith("bandcamp.com")
            ? "<script data-tralbum=\"" + WebUtility.HtmlEncode(json) + "\"></script>" : "audio bytes") };
    })));
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
