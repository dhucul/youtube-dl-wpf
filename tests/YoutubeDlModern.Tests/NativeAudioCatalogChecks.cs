using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeExtractionTimingArguments()
    {
        var service = new SettingsService(Path.Combine(TestRoot, "owned-extraction-timing")); var settings = new AppSettings(); settings.General.ExtraArgs = "--sleep-requests 0.25"; service.Save(settings); var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { requests.Add(request); return Task.FromResult(AdapterVideo()); }), service);
        await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert(requests.Last().RequestSleepSeconds == .25, "WPF analysis dropped metadata request pacing.");
        await adapter.DownloadAsync("https://media.example/watch", new() { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate --sleep-requests 0.5" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests.Last().RequestSleepSeconds == .5 && settings.General.ExtraArgs == "--sleep-requests 0.25", "WPF extraction pacing lost job precedence or mutated saved settings.");
        var count = requests.Count;
        try { await adapter.DownloadAsync("https://media.example/watch", new() { OutputDirectory = TestRoot, ExtraArgs = "--simulate --sleep-requests -1", AddMetadata = false, EmbedChapters = false }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new Exception("Invalid request pacing reached extraction."); }
        catch (Exception ex) when (ex is NativeEngineUnsupportedException or ArgumentException) { Assert(requests.Count == count, "Invalid request pacing reached provider discovery."); }
    }
    private static Task NativeArdSoundsRoutes()
    {
        foreach (var host in new[] { "www.ardsounds.de", "www.ardaudiothek.de" })
        {
            Assert(NativeEngine.CanHandle(new("https://" + host + "/episode/urn:ard:episode:1111111111111111/")), "ARD Sounds episode routing was lost.");
            Assert(NativeEngine.CanHandle(new("https://" + host + "/sendung/owned/urn:ard:show:1111111111111111/")), "ARD Sounds show routing was lost.");
        }
        return Task.CompletedTask;
    }
}
