using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task RumbleCollection(string ffmpeg, bool unavailable)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=128x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var completed = new List<string>();
        var first = RumbleCollectionChecks.Item("v1", 101); if (unavailable) first["is_processing"] = true;
        var engine = RumbleCollectionChecks.Engine(calls, _ => RumbleCollectionChecks.Page([first, RumbleCollectionChecks.Item("v71gwwi", 422061706)]),
            media: _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = RumbleCollectionChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, PlaylistItems = unavailable ? "1,2" : "2", IgnoreErrors = true,
            AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, OutputTemplate = "%(playlist_index)03d-%(playlist_autonumber)02d-%(id)s.%(ext)s" };
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (result.Succeeded == unavailable || result.OutputPaths.Count != 1 || completed.Count != 1 || result.Failures.Count != (unavailable ? 1 : 0) ||
            calls.Count != 4 || calls.Any(url => url.AbsolutePath.StartsWith("/v1-"))) throw new InvalidOperationException("Collection selection fetched an unselected/unavailable video or lost completed output.");
        var output = result.OutputPaths[0];
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(output, ".info.json")));
        if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != (unavailable ? 2 : 1) ||
            info.RootElement.GetProperty("playlist_id").GetString() != "channel:99" || info.RootElement.GetProperty("id").GetString() != RumbleChecks.Id)
            throw new InvalidOperationException("Channel positions, selected order or resolved video identity changed.");
        async Task<string[]> Frames(string path)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return probe.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()).ToArray();
        }
        var before = await Frames(source); var after = await Frames(output);
        if (!before.SequenceEqual(after)) throw new InvalidOperationException("Selected channel playback lost media frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0", "-f", "null", "-"]);
        if ((await File.ReadAllTextAsync(archive)).Trim() != "rumble " + RumbleChecks.Id) throw new InvalidOperationException("The channel was archived instead of the resolved video.");
        calls.Clear(); completed.Clear();
        var repeat = await engine.DownloadAsync(request with { PlaylistItems = "2" }, null, null, default, completed.Add);
        if (!repeat.Succeeded || !repeat.Skipped || completed.Count != 0 || calls.Count != 3) throw new InvalidOperationException("An archived selected channel video was downloaded again.");
    }
}
