using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeProcessingOptionsMapping(string mode)
    {
        var folder = Path.Combine(TestRoot, "processing-" + mode); Directory.CreateDirectory(folder);
        var settings = new AppSettings(); settings.PostProcess.Fixup = "warn"; settings.PostProcess.PostprocessorArgs = "Metadata:-metadata title=saved";
        var captured = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = AdapterEngine(async (request, _) => { captured.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo(); });
        if (mode == "analysis")
        {
            settings.General.ExtraArgs = "--fixup force --ppa \"ffmpeg:-metadata title=ignored\"";
            var service = new SettingsService(Path.Combine(folder, "settings")); service.Save(settings);
            await new NativeEngineAdapter(engine, service).GetVideoInfoAsync("https://fixture.example/watch", default);
            Assert(captured.Single().Fixup is null && captured[0].PostprocessorArguments is null, "Analysis applied processing configuration."); return;
        }
        if (mode == "invalid") settings.PostProcess.PostprocessorArgs = "ffmpeg:-t 1";
        var options = new DownloadOptions { OutputDirectory = folder, ExtraArgs = mode == "overrides" ? "--simulate --fixup never --ppa \"Metadata:-metadata title=job\"" : "--simulate" };
        var task = new NativeEngineAdapter(engine).DownloadAsync("https://fixture.example/watch", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        if (mode == "immutable") { await entered.Task; settings.PostProcess.Fixup = "force"; settings.PostProcess.PostprocessorArgs = "Metadata:-metadata title=changed"; release.SetResult(); }
        if (mode == "invalid") { try { await task; throw new Exception("Invalid processing configuration reached native extraction."); } catch (NativeEngineUnsupportedException ex) { Assert(captured.Count == 0 && ex.Message.Contains("PostProcess.PostprocessorArgs", StringComparison.Ordinal) && !ex.Message.Contains("-t 1", StringComparison.Ordinal), "Processing preflight lost setting identity or echoed its value."); } return; }
        var result = await task; var request = captured.Single(); Assert(result.ExitCode == 0 && request.Fixup == (mode == "overrides" ? "never" : "warn"), "Processing policy mapping/precedence changed.");
        Assert(request.PostprocessorArguments!.Count == (mode == "overrides" ? 2 : 1) && request.PostprocessorArguments[0].Contains("title=saved", StringComparison.Ordinal), "Saved processing arguments or immutable request capture changed.");
    }
}
