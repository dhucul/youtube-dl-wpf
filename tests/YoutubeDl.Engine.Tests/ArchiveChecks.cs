using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDl.Engine.Providers.InternetArchive;

internal static class ArchiveChecks
{
    private const string Id = "native-archive-fixture";
    private const string Json = """
    {"metadata":{"identifier":"native-archive-fixture","title":["Archive title"],"description":"Description","creator":["Creator one","Creator two"],"mediatype":"movies"},"files":[
      {"name":"movie original.mp4","source":"original","format":"MPEG4","height":"1080","size":"100","length":"00:12.5"},
      {"name":"movie low.mp4","source":"derivative","original":"movie original.mp4","format":"MPEG4","height":360,"size":"50"},
      {"name":"movie.ogg","source":"derivative","original":"movie original.mp4","format":"Ogg Video","height":144,"size":"20"},
      {"name":"secret.mp4","source":"original","private":"true"},
      {"name":"description.txt","source":"original","format":"Text"}
    ]}
    """;
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Internet Archive item and direct-file routes stay distinct", () => Sync(Addresses)),
        ("Archive metadata groups derivatives and retains public video/audio facts", () => Sync(Metadata)),
        ("Invalid archive paths, identities, derivative chains, and limits fail explicitly", () => Sync(Invalid)),
        ("Native archive downloads retain metadata and select exposed files", Single),
        ("Archive playlist quality selection uses each entry's resolved media metadata", Playlist)
    ];
    private static void Addresses()
    {
        var registry = MediaProviderRegistry.CreateDefault();
        foreach (var path in new[] { "details/" + Id, "download/" + Id + "/" })
        {
            var uri = new Uri("https://archive.org/" + path);
            Assert(registry.Resolve(uri) is InternetArchiveProvider && InternetArchiveProvider.ParseIdentifier(uri) == Id, "Item route lost.");
        }
        Assert(registry.Resolve(new Uri("https://archive.org/download/" + Id + "/movie.mp4")) is DirectMediaProvider, "Direct file was misclassified as an item.");
        Throws<ArgumentException>(() => InternetArchiveProvider.ParseIdentifier(new Uri("https://archive.org.evil.example/details/" + Id)));
        Throws<NotSupportedException>(() => InternetArchiveProvider.ParseIdentifier(new Uri("https://archive.org/details/" + Id + "/extra")));
    }
    private static void Metadata()
    {
        var page = InternetArchiveProvider.ParseMetadata(Json, Id);
        Assert(page.Id == Id && page.Title == "Archive title" && page.Uploader == "Creator one; Creator two" && page.DurationSeconds == 12.5, "Archive metadata lost.");
        Assert(page.Media.Count == 3 && !page.IsPlaylist && page.ReportedFormats[0].FileSize == 100, "Derivatives were mistaken for separate playlist entries.");
        Assert(page.Media[0].Url.AbsoluteUri.Contains("movie%20original.mp4") && page.Media.Single(format => format.Id == "movie.ogg").IsAudioOnly == false, "File names or explicit video types were changed.");
        var audio = InternetArchiveProvider.ParseMetadata(Json.Replace(".mp4", ".mp3").Replace(".ogg", ".flac").Replace("Ogg Video", "FLAC"), Id);
        Assert(audio.Media.All(format => format.IsAudioOnly), "Audio files were mislabeled as video.");
    }
    private static void Invalid()
    {
        Throws<InvalidOperationException>(() => InternetArchiveProvider.ParseMetadata("[]", Id));
        Throws<InvalidDataException>(() => InternetArchiveProvider.ParseMetadata(Json, "wrong"));
        Throws<InvalidDataException>(() => InternetArchiveProvider.ParseMetadata(Json.Replace("movie low.mp4", "../movie low.mp4"), Id));
        Throws<InvalidDataException>(() => InternetArchiveProvider.ParseMetadata(Json.Replace("\"original\":\"movie original.mp4\"", "\"original\":\"movie low.mp4\""), Id));
        Throws<InvalidDataException>(() => InternetArchiveProvider.ParseMetadata(new string(' ', 8 * 1024 * 1024 + 1), Id));
        Throws<InvalidOperationException>(() => InternetArchiveProvider.ParseMetadata(Multiple, Id));
        Throws<InvalidDataException>(() => InternetArchiveProvider.ParseMetadata(Multiple, Id, true, 1));
    }
    private static async Task Single()
    {
        using var files = new Files();
        var requests = new List<string>();
        var result = await Engine(Json, requests).DownloadAsync(Request(files.Directory) with { FormatId = "movie low.mp4", WriteInfoJson = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "movie low.mp4" && requests.Count == 2 && requests[0] == "/metadata/" + Id, "Native archive transfer was delegated or selected the wrong file.");
        using var info = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(result.OutputPath!, "info.json")));
        Assert(info.RootElement.GetProperty("id").GetString() == Id && info.RootElement.GetProperty("uploader").GetString() == "Creator one; Creator two", "Archive sidecar metadata lost.");
    }
    private static async Task Playlist()
    {
        using var files = new Files();
        var requests = new List<string>();
        var completed = new List<string>();
        var result = await Engine(Multiple, requests).DownloadAsync(Request(files.Directory) with { MaximumHeight = 360, PlaylistItems = "2", WriteInfoJson = true }, null, null, default, completed.Add);
        Assert(result.Succeeded && result.OutputPaths.SequenceEqual(completed) && result.OutputPaths.Count == 1 &&
            File.ReadAllText(result.OutputPaths[0]) == "episode2 low.mp4", "Resolved per-entry quality or playlist selection was lost.");
        Assert(requests.Count == 2 && requests[1].Contains("episode2%20low.mp4") && Path.GetFileName(result.OutputPaths[0]).StartsWith("002 - "), "The item was re-extracted or its original position was changed.");
        using var info = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(result.OutputPaths[0], "info.json")));
        Assert(info.RootElement.GetProperty("id").GetString() == Id + "/episode2.mp4" && info.RootElement.GetProperty("uploader").GetString() == "Creator one; Creator two", "Resolved playlist metadata was dropped.");
    }
    private static string Multiple => Json.Replace("{\"name\":\"description.txt\"", "{\"name\":\"episode2.mp4\",\"source\":\"original\",\"height\":1080},\n{\"name\":\"episode2 low.mp4\",\"source\":\"derivative\",\"original\":\"episode2.mp4\",\"height\":360},\n{\"name\":\"description.txt\"");
    private static NativeDownloadRequest Request(string directory) => new() { Url = new Uri("https://archive.org/details/" + Id), OutputDirectory = directory, Retries = 0 };
    private static NativeEngine Engine(string json, List<string> requests) => new(httpFactory: _ => new MediaHttp(new Handler(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        requests.Add(path);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(path.StartsWith("/metadata/") ? json : Uri.UnescapeDataString(Path.GetFileName(path))) };
    })));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request)); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
