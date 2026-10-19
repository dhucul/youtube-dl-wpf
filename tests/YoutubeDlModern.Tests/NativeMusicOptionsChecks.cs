using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeMusicOptions(string mode)
    {
        var folder = Path.Combine(TestRoot, "music-options-" + mode); var root = Path.Combine(folder, "native-public-player-v1"); Directory.CreateDirectory(root);
        var owned = Path.Combine(root, new string('A', 64) + ".json"); var user = Path.Combine(folder, "user-file.json"); await File.WriteAllTextAsync(owned, "cache"); await File.WriteAllTextAsync(user, "user data");
        var settings = new AppSettings(); settings.General.CacheDir = folder; settings.General.ExtraArgs = "--quiet --no-progress --no-warnings --rm-cache-dir";
        var requests = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = AdapterEngine(async (request, _) => { requests.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo() with { Warnings = ["fixture-warning"] }; });
        if (mode == "analysis")
        {
            var service = new SettingsService(Path.Combine(folder, "settings")); service.Save(settings); await new NativeEngineAdapter(engine, service).GetVideoInfoAsync("https://fixture.example/watch", default);
            Assert(!requests.Single().Quiet && !requests[0].NoProgress && !requests[0].NoWarnings && !requests[0].RemoveCache && File.Exists(owned), "Read-only analysis applied output controls or cache deletion."); return;
        }
        if (mode == "invalid") settings.General.ExtraArgs = "--quiet=true";
        var options = new DownloadOptions { OutputDirectory = folder, AddMetadata = false, EmbedChapters = false, ExtraArgs = mode == "overrides" ? "--no-quiet --progress --simulate" : "--simulate" };
        var callbacks = new List<string>(); var logs = new List<string>(); var task = new NativeEngineAdapter(engine).DownloadAsync("https://fixture.example/watch", options, settings, null, new Progress<ProgressUpdate>(), logs.Add, default, callbacks.Add);
        if (mode == "immutable") { await entered.Task; settings.General.ExtraArgs = "--no-quiet --progress"; release.SetResult(); }
        if (mode == "invalid")
        {
            try { await task; throw new Exception("An invalid output-control argument reached discovery."); } catch (Exception ex) when (ex is NativeEngineUnsupportedException or ArgumentException) { Assert(requests.Count == 0 && File.Exists(owned), "Invalid flags caused extraction or cache deletion."); }
            return;
        }
        var result = await task; var request = requests.Single(); Assert(result.ExitCode == 0 && callbacks.Count == 0, "Native output controls changed simulation or emitted files.");
        Assert(request.Quiet == (mode != "overrides") && request.NoProgress == (mode != "overrides") && request.NoWarnings && request.RemoveCache, "Saved/job output-control precedence or immutable capture changed.");
        Assert(!File.Exists(owned) && await File.ReadAllTextAsync(user) == "user data", "Native cache cleanup affected non-cache files or failed to run.");
        Assert(!logs.Any(line => line.Contains("fixture-warning", StringComparison.Ordinal)), "Suppressed native warnings reached the app logger.");
    }
}
