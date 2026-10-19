using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeFileTimeSettings()
    {
        var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.General.NoMtime = true; var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/time.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].NoMtime, "Saved no-mtime was dropped."); options.ExtraArgs = "--simulate --mtime";
        await adapter.DownloadAsync("https://media.example/time.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(!requests[^1].NoMtime && settings.General.NoMtime, "Job mtime override changed saved settings or lost precedence.");
    }
}
