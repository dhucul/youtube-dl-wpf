using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.PbsKids;

internal static partial class FfmpegChecks
{
    private static async Task PbsKidsMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); var hls = mode is "hls" or "selected-hls";
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "24", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var assets = new Dictionary<string, byte[]>();
        if (hls) await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(files.Directory, "media.m3u8")]);
        foreach (var path in System.IO.Directory.EnumerateFiles(files.Directory)) assets[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        if (hls)
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"));
            assets["media.m3u8"] = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => line.Length > 0 && !line.StartsWith('#') ? line + PbsKidsChecks.Signed : line)));
            assets["master.m3u8"] = System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=160x96,FRAME-RATE=24,CODECS=\"avc1.42e00a,mp4a.40.2\"\nmedia.m3u8" + PbsKidsChecks.Signed + "\n");
        }
        var selected = mode.StartsWith("selected", StringComparison.Ordinal); var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var record = PbsKidsChecks.Video(duration: mode == "incomplete" ? 20 : 2);
        var engine = new NativeEngine([new PbsKidsProvider()], _ => PbsKidsChecks.Http(calls, html: !selected ? PbsKidsChecks.Html(record) : null, denied: mode == "denied", media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Credentials reached PBS Kids playback.");
            var uri = r.RequestUri!; if (uri.Query != PbsKidsChecks.Signed) throw new InvalidOperationException("PBS Kids literal query changed.");
            if (selected && !uri.AbsolutePath.Contains(PbsKidsChecks.OtherCid)) throw new InvalidOperationException("An unselected PBS Kids asset transferred media.");
            return new(HttpStatusCode.OK) { Content = uri.AbsolutePath.EndsWith("source.mp4") ? new ByteArrayContent(bytes) : new ByteArrayContent(assets[Path.GetFileName(uri.AbsolutePath)]) };
        }));
        var request = PbsKidsChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, FormatId = hls ? "hls-16x9-96p" : "mp4-16x9-96p", KeepOriginal = true, AddMetadata = true, WriteInfoJson = true,
            DownloadArchiveFile = archive, MaximumHeight = 96, Url = selected ? new("https://pbskids.org/videos/playlist/fixture-collection/1567131") : PbsKidsChecks.Request(files.Directory).Url, PlaylistItems = selected ? "2" : "" };
        if (selected) { var preview = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = preview.PlaylistSnapshot }; }
        if (mode is "incomplete" or "denied")
        {
            var prior = Path.Combine(files.Directory, "kids.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Unverified PBS Kids playback was published."); }
            catch (InvalidDataException) when (mode == "incomplete") { }
            catch (NotSupportedException) when (mode == "denied") { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed PBS Kids access/verification changed existing output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count < 2 || callbacks.Count != result.OutputPaths.Count) throw new InvalidOperationException("PBS Kids media/original publication failed: " + string.Join("; ", result.Failures.Select(f => f.Message)));
        var output = result.OutputPath ?? result.OutputPaths[0];
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var video = probe.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (video.ValueKind != JsonValueKind.Undefined && video.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("PBS Kids playback lost video frames.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!hls && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("PBS Kids original progressive bytes changed.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "pbskids " + (selected ? PbsKidsChecks.OtherCid : PbsKidsChecks.Cid)) throw new InvalidOperationException("PBS Kids archive did not retain its original asset identity.");
        if (selected) { using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("PBS Kids selected source position changed."); }
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host.Contains("cdn")) || selected && calls.Count != 0) throw new InvalidOperationException("PBS Kids archive repeat transferred old media or repeated completions.");
    }
}
