using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Medal;

internal static partial class FfmpegChecks
{
    private static async Task MedalMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); var silent = mode == "silent";
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2" };
        if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]); if (!silent) args.AddRange(["-c:a", "aac"]); args.Add(source); await Run(ffmpeg, args);
        var bytes = await File.ReadAllBytesAsync(source); var clip = MedalChecks.Clip(mode == "incomplete" ? 20 : 2); if (silent) clip["hasSound"] = 0;
        if (mode == "rendition") { clip["contentUrl720p"] = MedalChecks.Media("720.mp4") + "&t=720p"; }
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new MedalProvider()], _ => MedalChecks.Http(calls, MedalChecks.Html(clip), media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Caller credentials reached Medal playback.");
            if (!r.RequestUri!.Query.StartsWith("?auth=" + MedalChecks.Auth(), StringComparison.Ordinal)) throw new InvalidOperationException("Medal signed resource query changed.");
            if (mode == "rendition" && !r.RequestUri.AbsolutePath.EndsWith("720.mp4")) throw new InvalidOperationException("The selected supplied rendition was replaced by the original source.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var request = MedalChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            FormatId = mode == "rendition" ? "720p" : "source", VideoOnly = mode == "video", AudioOnly = mode == "audio", AudioFormat = "best" };
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "medal.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Medal source was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed Medal verification changed prior output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Medal processed/original output was not published once.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (video.ValueKind != JsonValueKind.Undefined && video.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Medal lost complete video frames.");
            if (path == result.OutputPath && (mode == "audio" && video.ValueKind != JsonValueKind.Undefined || (silent || mode == "video") && streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))) throw new InvalidOperationException("Medal selected source roles changed.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Medal source original bytes changed.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "medaltv " + MedalChecks.Id) throw new InvalidOperationException("Medal archive lost its clip identity.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || calls.Count != 0 || callbacks.Count != 0) throw new InvalidOperationException("Medal archived clip requested metadata/media or repeated callbacks.");
    }
}
