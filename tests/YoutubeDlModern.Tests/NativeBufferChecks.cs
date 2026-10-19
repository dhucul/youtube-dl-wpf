using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeBufferSettings()
    {
        var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Network.BufferSize = "4K"; settings.Network.NoResizeBuffer = true;
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/buffer.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].BufferSize == "4K" && requests[^1].NoResizeBuffer, "Saved buffers were dropped."); options.ExtraArgs = "--simulate --buffer-size 16K --resize-buffer";
        await adapter.DownloadAsync("https://media.example/buffer.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].BufferSize == "16K" && !requests[^1].NoResizeBuffer && settings.Network.BufferSize == "4K" && settings.Network.NoResizeBuffer, "Job buffer overrides changed saved settings or lost precedence.");
        var count = requests.Count; settings.Network.BufferSize = "0"; options.ExtraArgs = "--simulate";
        try { await adapter.DownloadAsync("https://media.example/buffer.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("Invalid saved buffer was accepted."); }
        catch (YoutubeDl.Engine.Download.NativeSelectionException) { Assert(requests.Count == count, "Invalid saved buffer reached discovery."); }
    }
}
