using System.IO;
using YoutubeDl.Engine;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static async Task NativeDynamicDashArguments()
    {
        var service = new SettingsService(Path.Combine(TestRoot, "owned-dynamic-dash-options")); var settings = new AppSettings(); var requests = new List<NativeDownloadRequest>(); var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { requests.Add(request); return Task.FromResult(AdapterVideo()); }), service);
        foreach (var (arguments, disabled) in new[] { ("--ignore-dynamic-mpd", true), ("--no-allow-dynamic-mpd", true), ("--ignore-dynamic-mpd --allow-dynamic-mpd", false), ("--ignore-dynamic-mpd --no-ignore-dynamic-mpd", false) })
        {
            settings.General.ExtraArgs = arguments; service.Save(settings); await adapter.GetVideoInfoAsync("https://media.example/watch", default); Assert((requests.Last().AllowDynamicDash == false) == disabled, "WPF analysis lost dynamic DASH aliases or precedence.");
        }
        settings.General.ExtraArgs = "--ignore-dynamic-mpd=true"; service.Save(settings); var before = requests.Count;
        try { await adapter.GetVideoInfoAsync("https://media.example/watch", default); throw new Exception("WPF accepted a value for a dynamic policy flag."); } catch (Exception ex) when (ex is NativeEngineUnsupportedException or ArgumentException) { Assert(requests.Count == before, "Invalid dynamic DASH syntax reached discovery."); }
    }
}
