using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeNoPartSettings()
    {
        var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.General.NoPart = true; var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/direct.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].NoPart, "Saved direct-transfer mode was dropped."); options.ExtraArgs = "--simulate --part";
        await adapter.DownloadAsync("https://media.example/direct.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(!requests[^1].NoPart && settings.General.NoPart, "Job part override changed saved settings or lost precedence.");
    }
}
