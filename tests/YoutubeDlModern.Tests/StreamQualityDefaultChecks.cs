using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static IEnumerable<(string Name, Func<Task> Test)> StreamQualityDefaultChecks =>
    [
        ("Stream quality fresh app and queue defaults preserve source streams", StreamQualityDefaults),
        ("Stream quality legacy MP3 defaults migrate once and persist", StreamQualityMigration),
        ("Stream quality explicit audio conversion settings survive loading", StreamQualityExplicit),
        ("Stream quality native adapter and command arguments preserve source defaults", StreamQualityMapping)
    ];

    private static async Task StreamQualityDefaults()
    {
        var settings = SettingsService.CreateDefault();
        var options = new DownloadOptions();
        Assert(settings.Formats.DefaultQuality == "best" && settings.Formats.AudioFormat == "best" && settings.Formats.RecodeVideo == "" &&
            settings.Formats.MergeOutputFormat == "" && !settings.Formats.PreferFreeFormats && !settings.Formats.LiveNormalizeTransitions &&
            options.AudioFormat == "best" && options.QualityPreset == "best" && options.VideoCodec == "any" && options.MaxFps == 0 && options.Container == "auto", "Fresh defaults constrain or re-encode source media.");
        await using var fixture = new Fixture();
        using var vm = fixture.ViewModel();
        vm.UrlInput = "https://example.com/video";
        await vm.FetchCommand.ExecuteAsync(null);
        Assert(vm.AddAudioFormat == "best" && vm.SelectedQualityKey == "best" && vm.AddVideoCodec == "any" && vm.AddMaxFps == "0" && vm.AddContainer == "auto", "The add-download card replaced the original-stream defaults.");
    }

    private static async Task StreamQualityMigration()
    {
        await using var fixture = new Fixture();
        var path = Path.Combine(fixture.Directory, "settings.json");
        await File.WriteAllTextAsync(path, "{\"Formats\":{\"AudioFormat\":\"mp3\",\"AudioQuality\":\"192K\"}}");
        fixture.Settings.Load();
        Assert(fixture.Settings.Data.Formats.AudioFormat == "best" && fixture.Settings.Data.Formats.AudioQuality == "0", "Legacy MP3 defaults were not upgraded.");
        var draft = fixture.Settings.CreateDraft();
        draft.Formats.AudioFormat = "mp3"; draft.Formats.AudioQuality = "192K";
        fixture.Settings.Save(draft);
        fixture.Settings.Load();
        Assert(fixture.Settings.Data.Formats.AudioFormat == "mp3" && fixture.Settings.Data.Formats.AudioQuality == "192K", "Migration repeatedly overrode an explicit conversion choice.");
    }

    private static async Task StreamQualityExplicit()
    {
        await using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Directory, "settings.json"), "{\"Formats\":{\"DefaultQuality\":\"720\",\"AudioFormat\":\"mp3\",\"AudioQuality\":\"320K\",\"RecodeVideo\":\"mp4\"}}");
        fixture.Settings.Load();
        var saved = fixture.Settings.Data.Formats;
        Assert(saved.DefaultQuality == "720" && saved.AudioFormat == "mp3" && saved.AudioQuality == "320K" && saved.RecodeVideo == "mp4", "Custom saved preferences were replaced by defaults.");
    }

    private static async Task StreamQualityMapping()
    {
        NativeDownloadRequest? captured = null;
        var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { captured = request; return Task.FromResult(AdapterVideo()); }));
        await adapter.DownloadAsync("https://media.example/video", new() { OutputDirectory = TestRoot, DownloadMode = "audio-only", ExtraArgs = "--simulate" }, new(), null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(captured is { AudioFormat: "best", RecodeVideo: "", VideoCodec: "any", MaximumFrameRate: 0, MaximumHeight: null }, "The adapter changed default preservation options.");
        var args = ArgsBuilder.BuildDownloadArgs(new(), new() { DownloadMode = "audio-only" }, "https://media.example/video", null).ToArray();
        var format = Array.IndexOf(args, "--audio-format");
        Assert(format >= 0 && args[format + 1] == "best" && !args.Contains("--recode-video"), "The external engine default converts the audio.");
    }
}
