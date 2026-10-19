using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeExternalDownloaderMapping(string mode)
    {
        var folder = Path.Combine(TestRoot, "external-downloader-" + mode); Directory.CreateDirectory(folder);
        var settings = new AppSettings(); settings.Network.ExternalDownloader = "curl"; settings.Network.ExternalDownloaderArgs = "--max-time 12 --tcp-nodelay";
        if (mode == "invalid") settings.Network.ExternalDownloaderArgs = "--header secret-configured-value";
        var captured = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = AdapterEngine(async (request, _) => { captured.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo(); });
        if (mode == "analysis")
        {
            settings.General.ExtraArgs = "--downloader ignored-backend --downloader-args ignored-arguments";
            var service = new SettingsService(Path.Combine(folder, "settings")); service.Save(settings);
            await new NativeEngineAdapter(engine, service).GetVideoInfoAsync("https://fixture.example/watch", default);
            Assert(captured.Single().ExternalDownloader is null && captured[0].ExternalDownloaderArguments is null, "Analysis applied an external downloader."); return;
        }
        var options = new DownloadOptions { OutputDirectory = folder, AddMetadata = false, EmbedChapters = false,
            ExtraArgs = mode == "overrides" ? "--simulate --downloader curl.exe --downloader-args \"curl:--speed-time 3\"" : "--simulate" };
        var task = new NativeEngineAdapter(engine).DownloadAsync("https://fixture.example/watch", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        if (mode == "immutable") { await entered.Task; settings.Network.ExternalDownloader = "changed"; settings.Network.ExternalDownloaderArgs = "changed"; release.SetResult(); }
        if (mode == "invalid")
        {
            try { await task; throw new Exception("Unsupported downloader arguments reached extraction."); }
            catch (NativeEngineUnsupportedException ex) { Assert(captured.Count == 0 && ex.Message.Contains("Network.ExternalDownloaderArgs", StringComparison.Ordinal) && !ex.Message.Contains("secret-configured-value", StringComparison.Ordinal), "Downloader validation exposed configured values or lost its setting identity."); }
            return;
        }
        var result = await task; var request = captured.Single();
        Assert(result.ExitCode == 0 && request.ExternalDownloader == (mode == "overrides" ? "curl.exe" : "curl") && request.ExternalDownloaderArguments![0] == "--max-time 12 --tcp-nodelay" && request.ExternalDownloaderArguments.Count == (mode == "overrides" ? 2 : 1), "Saved/job downloader settings or immutable capture changed.");
    }
}
