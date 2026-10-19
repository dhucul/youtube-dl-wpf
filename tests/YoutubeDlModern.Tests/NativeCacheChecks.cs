using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativePublicCache(string mode)
    {
        var folder = Path.Combine(TestRoot, "public-cache-" + mode); Directory.CreateDirectory(folder);
        var cache = Path.Combine(folder, "cache"); var settings = new AppSettings(); settings.General.CacheDir = cache;
        settings.General.ExtraArgs = mode == "disabled" ? "--no-cache-dir" : "--simulate";
        var requests = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = AdapterEngine(async (request, _) => { requests.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo(); });
        if (mode == "analysis")
        {
            var service = new SettingsService(Path.Combine(folder, "settings")); settings.General.ExtraArgs = "--cache-dir \"" + cache + "\" --no-cache-dir --simulate"; service.Save(settings);
            var adapter = new NativeEngineAdapter(engine, service); await adapter.GetVideoInfoAsync("https://media.example/watch", default);
            Assert(requests.Single().CacheDirectory == cache && requests[0].NoCache && !requests[0].Simulate, "Analysis discarded cache options or retained download-only simulation."); return;
        }
        if (mode == "invalid") settings.General.CacheDir = "invalid\0path";
        var job = new DownloadOptions { OutputDirectory = folder, AddMetadata = false, EmbedChapters = false, ExtraArgs = mode == "override" ? "--cache-dir \"" + Path.Combine(folder, "job-cache") + "\" --simulate" : "--simulate" };
        var download = new NativeEngineAdapter(engine).DownloadAsync("https://media.example/watch", job, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        if (mode == "immutable") { await entered.Task; settings.General.CacheDir = Path.Combine(folder, "changed"); release.SetResult(); }
        if (mode == "invalid")
        {
            try { await download; throw new Exception("Invalid cache path reached discovery."); } catch (Exception ex) when (ex is ArgumentException or NativeEngineUnsupportedException) { Assert(requests.Count == 0, "Invalid cache configuration reached the provider."); }
            return;
        }
        var result = await download; Assert(result.ExitCode == 0 && requests.Count == 1, "Saved cache configuration remained unsupported.");
        Assert(requests[0].CacheDirectory == (mode == "override" ? Path.Combine(folder, "job-cache") : cache) && requests[0].NoCache == (mode == "disabled"), "Cache precedence or immutable job mapping changed.");
        Assert(!Directory.Exists(cache), "A provider without public player source created cache files.");
    }
}
