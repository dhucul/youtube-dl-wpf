using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;

internal static partial class FfmpegChecks
{
    private static async Task RecodeMedia(string ffmpeg, string target, bool auxiliary = false, bool odd = false, bool corrupt = false, bool multipleAudio = false, bool silent = false, bool rotated = false, bool same = false)
    {
        using var files = new Files(); var extension = rotated ? "mov" : same ? target : target == "mp4" || auxiliary ? "mkv" : "mp4";
        var source = Path.Combine(files.Directory, "source." + extension); var subtitle = Path.Combine(files.Directory, "source.srt");
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,100 --> 00:00:01,800\nPreserved source caption.\n");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", odd ? "testsrc=s=161x97:r=24:d=2" : "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2" };
        if (multipleAudio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000:duration=2"]);
        if (auxiliary) args.AddRange(["-i", subtitle]);
        args.AddRange(["-map", "0:v:0"]); if (!silent) args.AddRange(["-map", "1:a:0"]);
        if (multipleAudio) args.AddRange(["-map", "2:a:0", "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=fra"]);
        if (auxiliary) args.AddRange(["-map", (multipleAudio ? "3" : "2") + ":s:0", "-c:s", "srt", "-metadata:s:s:0", "language=eng"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", odd ? "yuv444p" : "yuv420p", "-c:a", "aac", source]); await Run(ffmpeg, args.ToArray());
        if (rotated)
        {
            var oriented = Path.Combine(files.Directory, "rotated.mov");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-display_rotation:v:0", "90", "-i", source, "-map", "0", "-c", "copy", oriented]); File.Move(oriented, source, true);
        }
        var bytes = await File.ReadAllBytesAsync(source); var archive = Path.Combine(files.Directory, "archive.txt"); var completed = new List<string>();
        var engine = RecodeChecks.Engine(corrupt ? "not a media file"u8.ToArray() : bytes, extension: extension, hasAudio: !silent);
        var request = RecodeChecks.Request(files.Directory) with { RecodeVideo = target, FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (corrupt)
        {
            var previous = Path.Combine(files.Directory, "converted." + target); await File.WriteAllTextAsync(previous, "previous complete conversion");
            try { await engine.DownloadAsync(request, null, null, default, completed.Add); throw new InvalidOperationException("Corrupt recode input was accepted."); }
            catch (InvalidDataException) { }
            if (completed.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete conversion") throw new InvalidOperationException("Failed recoding published output or changed an existing file.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPath is null || !result.OutputPath.EndsWith("." + target) || result.OutputPaths.Count != 2 || completed.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))
            throw new InvalidOperationException("Native recoding lost its output type, callbacks or exact original.");
        using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_name,codec_type,width,height,nb_read_frames:stream_tags=language", "-of", "json", result.OutputPath]));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        var expectedVideo = target switch { "webm" => "vp9", "avi" => "mpeg4", "ogg" => "theora", _ => "h264" };
        if (video.GetProperty("codec_name").GetString() != expectedVideo || video.GetProperty("width").GetInt32() != (rotated ? 96 : odd ? 161 : 160) || video.GetProperty("height").GetInt32() != (rotated ? 160 : odd ? 97 : 96) || video.GetProperty("nb_read_frames").GetString() != "48")
            throw new InvalidOperationException("Video conversion changed dimensions, lost frames or ignored its codec profile.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"]);
        var audio = streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        if (audio.Length != (silent ? 0 : multipleAudio ? 2 : 1)) throw new InvalidOperationException("Video conversion changed the number of audio tracks.");
        if (multipleAudio && !audio.Select(stream => stream.GetProperty("tags").GetProperty("language").GetString()).SequenceEqual(["eng", "fra"])) throw new InvalidOperationException("Audio track languages were lost.");
        if (same)
        {
            async Task<string> Packets(string path) => await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", path]);
            if (await Packets(source) != await Packets(result.OutputPath)) throw new InvalidOperationException("An already-target video was unnecessarily re-encoded.");
        }
        if (auxiliary)
        {
            var text = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "srt", "-"]);
            var before = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", source, "-map", "0:s:0", "-f", "srt", "-"]);
            if (!text.Contains("Preserved source caption.") || text.Trim() != before.Trim()) throw new InvalidOperationException("Video conversion changed its embedded source subtitle text or timing.");
        }
        if (!File.Exists(Path.ChangeExtension(result.OutputPath, ".info.json")) || (await File.ReadAllTextAsync(archive)).Trim() != "generic conversion") throw new InvalidOperationException("Converted sidecars or archive records were lost.");
    }
    private static async Task CancelRecode(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=24:d=6", "-c:v", "libx264", "-preset", "ultrafast", source]);
        var prior = Path.Combine(files.Directory, "converted.webm"); await File.WriteAllTextAsync(prior, "previous complete output"); var archive = Path.Combine(files.Directory, "archive.txt"); var callbacks = 0;
        using var stop = new CancellationTokenSource();
        var task = RecodeChecks.Engine(await File.ReadAllBytesAsync(source), hasAudio: false).DownloadAsync(RecodeChecks.Request(files.Directory) with { RecodeVideo = "webm", FfmpegPath = ffmpeg, DownloadArchiveFile = archive }, null, null, stop.Token, _ => callbacks++);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && !Directory.EnumerateFiles(files.Directory, ".process-*.webm", SearchOption.AllDirectories).Any() && DateTime.UtcNow < deadline) await Task.Delay(10);
        if (task.IsCompleted || DateTime.UtcNow >= deadline) throw new InvalidOperationException("Could not observe the active video encoder before cancellation.");
        stop.Cancel();
        try { await task; throw new InvalidOperationException("Canceled recoding returned success."); } catch (OperationCanceledException) { }
        if (callbacks != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete output" || Directory.EnumerateFiles(files.Directory, ".process-*.webm", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Canceled recoding left a processed file or changed published data.");
    }
}
