using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeHttpChunkSettings()
    {
        var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Network.HttpChunkSize = "256K"; var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/chunks.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].HttpChunkSize == "256K", "Saved HTTP chunk size was dropped."); options.ExtraArgs = "--simulate --http-chunk-size 0";
        await adapter.DownloadAsync("https://media.example/chunks.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].HttpChunkSize == "0" && settings.Network.HttpChunkSize == "256K", "Job chunk override changed saved settings or lost precedence.");
        var count = requests.Count; settings.Network.HttpChunkSize = "-1"; options.ExtraArgs = "--simulate";
        try { await adapter.DownloadAsync("https://media.example/chunks.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("Invalid HTTP chunk size was accepted."); }
        catch (YoutubeDl.Engine.Download.NativeSelectionException) { Assert(requests.Count == count, "Invalid HTTP chunk size reached discovery."); }
    }
}
