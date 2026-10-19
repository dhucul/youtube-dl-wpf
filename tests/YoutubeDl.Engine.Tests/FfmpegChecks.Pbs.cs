using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Pbs;

internal static partial class FfmpegChecks
{
    private static async Task PbsMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "24", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var assets = new Dictionary<string, byte[]>(); var hls = mode == "hls";
        if (hls) await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(files.Directory, "media.m3u8")]);
        foreach (var path in System.IO.Directory.EnumerateFiles(files.Directory)) assets[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        if (hls)
        {
            var playlist = await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"));
            assets["media.m3u8"] = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', playlist.Select(line => line.Length > 0 && !line.StartsWith('#') ? line + PbsChecks.Signed : line)));
            assets["master.m3u8"] = System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=160x96,FRAME-RATE=24,CODECS=\"avc1.42e00a,mp4a.40.2\"\nmedia.m3u8" + PbsChecks.Signed + "\n");
        }
        var video = PbsChecks.Video(hls ? "hls" : "mp4", mode == "incomplete" ? 20 : 2);
        const string vtt = "WEBVTT\n\n00:00:00.100 --> 00:00:01.800\nNative PBS captions.\n";
        const string srt = "1\n00:00:00,100 --> 00:00:01,800\nNative PBS captions.\n";
        const string xml = "<tt xmlns='http://www.w3.org/ns/ttml'><body><div><p begin='0.1s' end='1.8s'>Native PBS captions.</p></div></body></tt>";
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new PbsProvider()], _ => PbsChecks.Http(calls, video: video, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Caller credentials reached PBS playback.");
            var uri = r.RequestUri!; if (uri.Query != PbsChecks.Signed) throw new InvalidOperationException("PBS literal resource query changed.");
            var name = Path.GetFileName(uri.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = name == "captions.vtt" ? new StringContent(vtt) : name == "captions.srt" ? new StringContent(srt) : name == "captions.dfxp" ? new StringContent(xml) :
                name.EndsWith(".mp4") ? new ByteArrayContent(bytes) : new ByteArrayContent(assets[name]) };
        }));
        var request = PbsChecks.Request(files.Directory) with { Url = mode == "numeric" ? new("https://video.pbs.org/video/" + PbsChecks.Id + "/") : PbsChecks.Request(files.Directory).Url,
            FfmpegPath = ffmpeg, FormatId = hls ? "hls" : "mp4-96", KeepOriginal = true, AddMetadata = true, MaximumHeight = hls ? 96 : null,
            WriteSubs = true, SubLanguages = "en", SubFormat = mode == "srt" ? "srt" : mode == "ttml" ? "ttml" : "vtt", EmbedSubs = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "pbs.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete PBS source was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed PBS verification changed prior output or archives."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count < 2 || callbacks.Count != result.OutputPaths.Count) throw new InvalidOperationException("PBS originals and processed media were not published once.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var videoStream = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (videoStream.ValueKind != JsonValueKind.Undefined && videoStream.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("PBS changed the complete video frame count.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!hls && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("PBS progressive original bytes changed.");
        var embedded = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "webvtt", "-"], rejectDiagnostics: true);
        if (!embedded.Contains("Native PBS captions.")) throw new InvalidOperationException("PBS caption selection/conversion lost text.");
        var captionPath = result.SidecarPaths.Single(path => path.EndsWith(mode == "srt" ? ".srt" : mode == "ttml" ? ".ttml" : ".vtt"));
        if (await File.ReadAllTextAsync(captionPath) != (mode == "srt" ? srt : mode == "ttml" ? xml : vtt)) throw new InvalidOperationException("PBS original caption bytes changed.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "pbs " + PbsChecks.Id || calls.Any(uri => uri.AbsolutePath.Contains("333333"))) throw new InvalidOperationException("PBS archive identity or requested-media scope changed.");
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host.EndsWith(".pbs-video.pbs.org"))) throw new InvalidOperationException("PBS archive repeat transferred media or notified old files.");
        if (mode == "numeric" && calls.Count != 0) throw new InvalidOperationException("PBS numeric archive repeat performed unnecessary discovery.");
    }
}
