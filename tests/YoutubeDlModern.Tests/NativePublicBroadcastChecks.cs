using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeSubtitleTimingArguments()
    {
        var service = new SettingsService(Path.Combine(TestRoot, "owned-subtitle-timing")); var settings = new AppSettings(); settings.General.ExtraArgs = "--sleep-subtitles 0.25"; service.Save(settings); var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { requests.Add(request); return Task.FromResult(AdapterVideo()); }), service);
        await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert(requests.Last().SubtitleSleepSeconds is null, "Caption-download timing leaked into read-only analysis.");
        await adapter.DownloadAsync("https://media.example/watch", new() { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate --sleep-subtitles 0.5" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests.Last().SubtitleSleepSeconds == .5 && settings.General.ExtraArgs == "--sleep-subtitles 0.25", "WPF subtitle delay precedence mutated saved settings or lost the job value.");
        var count = requests.Count;
        try { await adapter.DownloadAsync("https://media.example/watch", new() { OutputDirectory = TestRoot, ExtraArgs = "--simulate --sleep-subtitles -1", AddMetadata = false, EmbedChapters = false }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new Exception("An invalid subtitle delay reached extraction."); }
        catch (Exception ex) when (ex is NativeEngineUnsupportedException or ArgumentException) { Assert(requests.Count == count, "Invalid subtitle timing reached provider discovery."); }
    }
    private static async Task NativeNrkOpaqueMapping()
    {
        Assert(NativeEngine.CanHandle(new("nrk:TEST00000001")), "The native registry dropped NRK's captured address scheme.");
        NativeDownloadRequest? observed = null; var adapter = new NativeEngineAdapter(new NativeEngine([new NativeNrkAppProvider(request => observed = request)])); await adapter.GetVideoInfoAsync("nrk:TEST00000001", default);
        Assert(observed?.Url.AbsoluteUri == "nrk:TEST00000001", "WPF changed the selected NRK programme identifier.");
    }
    private sealed class NativeNrkAppProvider(Action<NativeDownloadRequest> observe) : YoutubeDl.Engine.Providers.IMediaProvider
    {
        public YoutubeDl.Engine.Providers.MediaProviderInfo Info => new("nrk", "Owned NRK mapping fixture", 1000);
        public bool CanHandle(Uri url) => url.Scheme == "nrk";
        public Task<YoutubeDl.Engine.Discovery.PageMetadata> ExtractAsync(NativeDownloadRequest request, YoutubeDl.Engine.Http.MediaHttp http, CancellationToken ct) { observe(request); return Task.FromResult(AdapterVideo()); }
    }
}
