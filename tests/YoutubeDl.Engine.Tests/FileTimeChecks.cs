using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class FileTimeChecks
{
    internal static readonly DateTimeOffset Modified = new(2019, 4, 5, 6, 7, 8, TimeSpan.Zero);
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("File timestamps use a valid HTTP Last-Modified after complete transfer", Preserve),
        ("File timestamps no-mtime retains the current completed file time", Disabled),
        ("File timestamps missing malformed and unsupported dates do not fail downloads", Unknown),
        ("File timestamps resumed responses preserve the validated remote entity time", Resume),
        ("File timestamps failed transfers preserve prior bytes and modification time", Failure),
        ("File timestamps transactional publication preserves media times without changing sidecars", Publication),
        ("File timestamps protected existing outputs retain their original time", Existing),
        ("File timestamps explicit argument overrides preserve native option precedence", Arguments)
    ];
    private static async Task Preserve()
    {
        using var files = new Files(); using var http = Http(); var path = Path.Combine(files.Path, "media.mp4");
        await new FileDownloader(http).DownloadAsync(new("https://time.example/media"), path, null, null, default, 0);
        Assert(File.GetLastWriteTimeUtc(path) == Modified.UtcDateTime && await File.ReadAllTextAsync(path) == "complete media", "Remote modification time or complete bytes changed.");
    }
    private static async Task Disabled()
    {
        using var files = new Files(); using var http = Http(); var before = DateTime.UtcNow.AddSeconds(-2); var path = Path.Combine(files.Path, "media.mp4");
        await new FileDownloader(http) { NoMtime = true }.DownloadAsync(new("https://time.example/media"), path, null, null, default, 0);
        Assert(File.GetLastWriteTimeUtc(path) >= before && File.GetLastWriteTimeUtc(path) != Modified.UtcDateTime, "No-mtime applied the server date.");
    }
    private static async Task Unknown()
    {
        using var files = new Files();
        foreach (var header in new[] { "", "invalid", "Mon, 01 Jan 0001 00:00:00 GMT" })
        {
            using var http = new MediaHttp(new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("complete media") }; if (header.Length > 0) response.Content.Headers.TryAddWithoutValidation("Last-Modified", header); return response; }));
            var path = Path.Combine(files.Path, Guid.NewGuid() + ".mp4"); await new FileDownloader(http).DownloadAsync(new("https://time.example/media"), path, null, null, default, 0);
            Assert(File.Exists(path) && File.GetLastWriteTimeUtc(path).Year > 2000, "Unsupported server time failed or changed a complete download.");
        }
        Assert(!NativeFileTime.TrySet(Path.Combine(files.Path, "missing"), Modified.UtcDateTime), "Missing file time was treated as updated.");
    }
    private static async Task Resume()
    {
        using var files = new Files(); var path = Path.Combine(files.Path, "media.mp4"); var uri = new Uri("https://time.example/media"); await File.WriteAllTextAsync(path + ".part", "abc");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        await File.WriteAllTextAsync(path + ".part.json", JsonSerializer.Serialize(new { ResourceKey = key, ETag = "\"same\"", LastModified = Modified, Length = 6 }));
        using var http = new MediaHttp(new Handler(request =>
        {
            Assert(request.Headers.Range?.Ranges.Single().From == 3, "Validated timestamp resume did not preserve its range.");
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent("def") }; response.Headers.ETag = new EntityTagHeaderValue("\"same\"");
            response.Content.Headers.ContentRange = new(3, 5, 6); response.Content.Headers.LastModified = Modified; return response;
        }));
        await new FileDownloader(http).DownloadAsync(uri, path, null, null, default, 0);
        Assert(await File.ReadAllTextAsync(path) == "abcdef" && File.GetLastWriteTimeUtc(path) == Modified.UtcDateTime, "Resumed entity time was lost.");
    }
    private static async Task Failure()
    {
        using var files = new Files(); var path = Path.Combine(files.Path, "media.mp4"); await File.WriteAllTextAsync(path, "prior complete"); File.SetLastWriteTimeUtc(path, Modified.UtcDateTime);
        using var http = new MediaHttp(new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("short") }; response.Content.Headers.ContentLength = 100; response.Content.Headers.LastModified = Modified.AddDays(1); return response; }));
        try { await new FileDownloader(http).DownloadAsync(new("https://time.example/media"), path, null, null, default, 0); throw new InvalidOperationException("Incomplete entity was accepted."); } catch (IOException) { }
        Assert(await File.ReadAllTextAsync(path) == "prior complete" && File.GetLastWriteTimeUtc(path) == Modified.UtcDateTime, "Failed transfer changed prior output time.");
    }
    private static async Task Publication()
    {
        using var files = new Files(); var target = Path.Combine(files.Path, "media.mp4"); using var workspace = new DownloadWorkspace(target, new("https://time.example/watch"));
        var raw = workspace.RawPath("source.mp4"); await File.WriteAllTextAsync(raw, "complete media"); var sidecar = workspace.RawPath("source.description"); await File.WriteAllTextAsync(sidecar, "description");
        var result = await workspace.PublishAsync([new(raw, target, [sidecar], ModifiedUtc: Modified.UtcDateTime)], false, default);
        Assert(result.NewPaths.Count == 1 && File.GetLastWriteTimeUtc(target) == Modified.UtcDateTime && File.GetLastWriteTimeUtc(Path.Combine(files.Path, "media.description")).Year > 2000,
            "Transactional publication lost the media time or incorrectly dated related files.");
    }
    private static async Task Existing()
    {
        using var files = new Files(); var target = Path.Combine(files.Path, "media.mp4"); await File.WriteAllTextAsync(target, "prior complete"); File.SetLastWriteTimeUtc(target, Modified.UtcDateTime);
        using var workspace = new DownloadWorkspace(target, new("https://time.example/watch")); var raw = workspace.RawPath("source.mp4"); await File.WriteAllTextAsync(raw, "replacement");
        var result = await workspace.PublishAsync([new(raw, target, [], ModifiedUtc: Modified.AddDays(1).UtcDateTime)], true, default);
        Assert(result.NewPaths.Count == 0 && File.GetLastWriteTimeUtc(target) == Modified.UtcDateTime && await File.ReadAllTextAsync(target) == "prior complete", "Protected output was retimed.");
    }
    private static Task Arguments()
    {
        var request = new NativeDownloadRequest { Url = new("https://time.example/media"), OutputDirectory = Path.GetTempPath() };
        var disabled = NativeExtraArguments.Apply(request, ["--no-mtime"]); var enabled = NativeExtraArguments.Apply(disabled, ["--mtime"]);
        Assert(disabled.NoMtime && !enabled.NoMtime && !request.NoMtime, "Mtime flags lost precedence or modified input settings."); return Task.CompletedTask;
    }
    private static MediaHttp Http() => new(new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("complete media") }; response.Content.Headers.LastModified = Modified; return response; }));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(request)); } }
    private sealed class Files : IDisposable { internal string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-data", "mtime-" + Guid.NewGuid().ToString("N")); internal Files() => Directory.CreateDirectory(Path); public void Dispose() => Directory.Delete(Path, true); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
