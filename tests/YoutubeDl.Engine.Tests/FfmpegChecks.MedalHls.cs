using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Medal;

internal static partial class FfmpegChecks
{
    private static async Task MedalHlsMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var silent = mode == "silent"; var source = Path.Combine(files.Directory, "source.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2" };
        if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "24"]); if (!silent) args.AddRange(["-c:a", "aac"]); args.Add(source); await Run(ffmpeg, args);
        var hlsArgs = new List<string> { "-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a?", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(files.Directory, "media.m3u8") };
        await Run(ffmpeg, hlsArgs); var assets = new Dictionary<string, byte[]>();
        foreach (var path in System.IO.Directory.EnumerateFiles(files.Directory)) assets[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        var lines = await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"));
        var playlist = string.Join('\n', lines.Select(line => line.Length > 0 && !line.StartsWith('#') ? "https://cdn.medal.tv/ugcc/content/fixture/" + line + "?auth=" + MedalChecks.Auth() : line));
        var tokenQuery = new Uri(MedalChecks.Hls()).Query; var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=160x96,FRAME-RATE=24,CODECS=\"avc1.42e00a" + (silent ? "" : ",mp4a.40.2") + "\"\nsource.m3u8" + tokenQuery + "\n";
        var clip = MedalChecks.Clip(mode == "incomplete" ? 20 : 2); clip["contentUrlHls"] = MedalChecks.Hls(); if (silent) clip["hasSound"] = 0;
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new MedalProvider()], _ => MedalChecks.Http(calls, MedalChecks.Html(clip), media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Caller credentials reached Medal HLS.");
            var uri = r.RequestUri!;
            if (uri.Host == "medal.tv")
            {
                if (uri.Query != tokenQuery || !uri.AbsolutePath.StartsWith("/api/hls/" + MedalChecks.Id + "/")) throw new InvalidOperationException("HLS clip/service token changed.");
                if (mode == "denied") return new(HttpStatusCode.Forbidden) { Content = new StringContent("Unavailable") };
                return new(HttpStatusCode.OK) { Content = new StringContent(uri.AbsolutePath.EndsWith("master.m3u8") ? master : playlist) };
            }
            if (uri.Host != "cdn.medal.tv" || uri.Query != "?auth=" + MedalChecks.Auth() || !uri.AbsolutePath.StartsWith("/ugcc/content/fixture/")) throw new InvalidOperationException("An undeclared/fallback Medal resource was transferred.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(assets[Path.GetFileName(uri.AbsolutePath)]) };
        }));
        var request = MedalChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, FormatId = "hls", MaximumHeight = 96, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            VideoOnly = mode == "video", AudioOnly = mode == "audio", AudioFormat = "best", Retries = 0 };
        if (mode is "incomplete" or "denied")
        {
            var prior = Path.Combine(files.Directory, "medal.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Unverified Medal HLS was published."); }
            catch (InvalidDataException) when (mode == "incomplete") { }
            catch (HttpRequestException) when (mode == "denied") { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed HLS access/verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || callbacks.Count != result.OutputPaths.Count || callbacks.Count < 2) throw new InvalidOperationException("Medal HLS outputs/originals were not published once.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (video.ValueKind != JsonValueKind.Undefined && video.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Medal HLS lost complete video frames.");
            if (path == result.OutputPath && (mode == "audio" && video.ValueKind != JsonValueKind.Undefined || (mode == "video" || silent) && streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))) throw new InvalidOperationException("Medal HLS selected stream roles changed.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "medaltv " + MedalChecks.Id) throw new InvalidOperationException("HLS archive lost the clip identity.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || calls.Count != 0 || callbacks.Count != 0) throw new InvalidOperationException("Archived Medal HLS requested metadata/media or repeated callbacks.");
    }
}
