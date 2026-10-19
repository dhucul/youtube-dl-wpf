using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] CatalogSessionModes = ["nico-direct", "nico-flac", "nico-renewal", "nico-segment-renewal", "nico-no-overwrite", "nico-interrupted", "nico-grant-denied", "rf-profile", "rf-schedule", "rf-missing-link", "rf-unavailable", "rf-replaced"];
    internal static IEnumerable<(string Name, Func<Task> Test)> CatalogSessionMediaChecks(string ffmpeg) => CatalogSessionModes.Select(mode => ("Catalog session media " + mode, (Func<Task>)(() => CatalogSessionMedia(ffmpeg, mode))));
    internal static async Task CatalogSessionMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var nico = mode.StartsWith("nico-", StringComparison.Ordinal); var source = Path.Combine(files.Directory, nico ? "source.mp4" : "source.m4a");
        NativeEngine engine; NativeDownloadRequest request; Func<int> transfers; Func<int> apis;
        if (nico)
        {
            await MixedSource(ffmpeg, source); var manifest = Path.Combine(files.Directory, "variant.m3u8");
            await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%03d.ts"), manifest]);
            var f = new CatalogSessionChecks.NicoFixture { Mode = mode[5..], Master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=128x72,CODECS=\"avc1.42e01e,mp4a.40.2\"\nvariant.m3u8\n"u8.ToArray(), Files = Directory.GetFiles(files.Directory).Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith("variant.m3u8", StringComparison.Ordinal)).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes) };
            engine = f.Engine; request = f.Request(Path.Combine(files.Directory, "output")); transfers = () => f.Transfers; apis = () => f.Grants;
        }
        else
        {
            await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=730:sample_rate=48000:duration=3", "-c:a", "aac", source]);
            var f = new CatalogSessionChecks.RfFixture { Mode = mode[3..], Data = await File.ReadAllBytesAsync(source) }; engine = f.Engine; request = f.Request(Path.Combine(files.Directory, "output")) with { PlaylistItems = mode == "rf-missing-link" || mode == "rf-unavailable" ? "3" : "1" }; transfers = () => f.Transfers; apis = () => f.ApiCalls;
            var info = await engine.InspectAsync(request with { ExpandPlaylist = true }, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
            if (mode == "rf-replaced") { f.Replaced = true; var refused = await engine.DownloadAsync(request, null, null, default); CatalogSessionChecks.Assert(!refused.Succeeded && f.Transfers == 0, "A replaced RF profile member transferred media."); return; }
        }
        request = request with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), AudioOnly = mode == "nico-flac", AudioFormat = "flac" };
        var callbacks = new List<string>(); var logs = new List<string>();
        if (mode == "nico-grant-denied") { await ProviderFamiliesChecks.Expect<System.Net.Http.HttpRequestException>(() => engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); CatalogSessionChecks.Assert(callbacks.Count == 0 && transfers() == 0 && !File.Exists(request.DownloadArchiveFile), "Denied Nico grant published success."); return; }
        if (mode == "nico-interrupted")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned Nico processing interruption") : Task.CompletedTask;
            try { await ProviderFamiliesChecks.Expect<IOException>(() => engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add)); } finally { PublicationFaults.Observer = null; }
            CatalogSessionChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted Nico playback recorded completion.");
        }
        var result = await engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); CatalogSessionChecks.Assert(result.Succeeded && result.OutputPaths.Count > 0 && callbacks.Count > 0, "Catalog session media failed: " + result.Error + " " + string.Join(" | ", logs.TakeLast(8)));
        var output = result.OutputPath ?? result.OutputPaths.First(path => !path.Contains(".original.", StringComparison.Ordinal)); await Run(ffmpeg, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);
        var count = transfers();
        if (mode == "nico-no-overwrite") { var skip = await engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, logs.Add, default); CatalogSessionChecks.Assert(skip.Succeeded && skip.Skipped && transfers() == count, "Nico no-overwrite retransferred existing media."); }
        var completed = callbacks.Count; var repeat = await engine.DownloadAsync(request, null, logs.Add, default, callbacks.Add); CatalogSessionChecks.Assert(repeat.Succeeded && repeat.Skipped && callbacks.Count == completed && transfers() == count, "An archived catalog item retransferred media or repeated callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, OutputPath = output, result.OutputPaths, Transfers = count, ApiCalls = apis(), Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
