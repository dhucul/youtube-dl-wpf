using System.IO;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static void NativeExecHelper(string marker, string[] fields)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(marker).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("The exec helper marker is outside its test root.");
        File.AppendAllText(marker, JsonSerializer.Serialize(new { fields, Exists = File.Exists(fields[0]) }) + "\n");
    }
    private static async Task NativeExecSponsorMapping(string mode)
    {
        var folder = Path.Combine(TestRoot, "exec-sponsor-" + mode); Directory.CreateDirectory(folder); var marker = Path.Combine(folder, "exec.jsonl");
        var command = "dotnet \"" + typeof(Program).Assembly.Location + "\" --native-exec-helper \"" + marker + "\" {}";
        var settings = new AppSettings(); settings.PostProcess.ExecCommand = command; settings.PostProcess.SponsorBlock = mode == "execution" ? "none" : mode == "case" ? "NONE" : "mark";
        if (mode == "invalid") settings.PostProcess.ExecCommand = "before_dl:secret-configured-value";
        var captured = new List<NativeDownloadRequest>(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var engine = AdapterEngine(async (request, _) => { captured.Add(request); if (mode == "immutable") { entered.SetResult(); await release.Task; } return AdapterVideo(); });
        if (mode == "analysis")
        {
            settings.General.ExtraArgs = "--exec \"before_dl:ignored\" --sponsorblock-remove all";
            var service = new SettingsService(Path.Combine(folder, "settings")); service.Save(settings); await new NativeEngineAdapter(engine, service).GetVideoInfoAsync("https://fixture.example/watch", default);
            Assert(captured.Single().ExecCommands is null && captured[0].SponsorBlockMark is null && captured[0].SponsorBlockRemove is null && !File.Exists(marker), "Analysis applied command or SponsorBlock side effects."); return;
        }
        var options = new DownloadOptions { OutputDirectory = folder, AddMetadata = false, EmbedChapters = false, ExtraArgs = mode == "execution" ? "" : mode == "overrides" ? "--simulate --no-exec --no-sponsorblock --sponsorblock-remove sponsor" : "--simulate" };
        var callbacks = new List<string>(); var task = new NativeEngineAdapter(engine).DownloadAsync("https://fixture.example/watch", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default, callbacks.Add);
        if (mode == "immutable") { await entered.Task; settings.PostProcess.ExecCommand = "changed"; settings.PostProcess.SponsorBlock = "remove"; release.SetResult(); }
        if (mode == "invalid") { try { await task; throw new Exception("Unsupported exec stage reached extraction."); } catch (NativeEngineUnsupportedException ex) { Assert(captured.Count == 0 && ex.Message.Contains("PostProcess.ExecCommand", StringComparison.Ordinal) && !ex.Message.Contains("secret-configured-value", StringComparison.Ordinal), "Exec validation lost setting identity or echoed its value."); } return; }
        var result = await task; var request = captured.Single(); Assert(result.ExitCode == 0, "The native exec/SponsorBlock adapter job failed.");
        Assert(mode == "overrides" ? request.ExecCommands is null && request.SponsorBlockMark is null && request.SponsorBlockRemove == "sponsor" : request.ExecCommands!.Single() == command && request.SponsorBlockMark == (mode is "execution" or "case" ? null : "all"), "Saved/job mapping or immutable capture changed.");
        if (mode == "execution") { using var json = JsonDocument.Parse(File.ReadAllLines(marker).Single()); Assert(json.RootElement.GetProperty("Exists").GetBoolean() && callbacks.Count == 1 && json.RootElement.GetProperty("fields")[0].GetString() == result.FinalFilePaths.Single(), "WPF exec did not see the final published media path before successful completion."); }
        else Assert(!File.Exists(marker) && callbacks.Count == 0, "A simulated/disabled command ran or emitted callbacks.");
    }
}
