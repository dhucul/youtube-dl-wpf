using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.MicrosoftEmbed;

internal static partial class FfmpegChecks
{
    private static async Task MicrosoftEmbedMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "high.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "24", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var assets = new Dictionary<string, byte[]>();
        if (mode == "hls") await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(files.Directory, "master.m3u8")]);
        if (mode == "dash") await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "dash", "-seg_duration", "1", "-use_template", "1", "-use_timeline", "1", Path.Combine(files.Directory, "source.mpd")]);
        foreach (var path in System.IO.Directory.EnumerateFiles(files.Directory)) assets[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        var mapped = mode is "mapped" or "incomplete"; var playback = MicrosoftEmbedChecks.Playback(); if (mode == "incomplete") playback["duration"] = 20;
        var options = MicrosoftEmbedChecks.Options(mode is "hls" or "dash" ? mode : "mp4");
        const string caption = "WEBVTT\n\n00:00:00.100 --> 00:00:01.800\nNative Microsoft Embed captions.\n";
        const string ttml = "<tt xmlns='http://www.w3.org/ns/ttml'><body><div><p begin='0.1s' end='1.8s'>Native Microsoft Embed captions.</p></div></body></tt>";
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new MicrosoftEmbedProvider()], _ => MicrosoftEmbedChecks.Http(calls, playback, media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Credentials reached Microsoft Embed playback.");
            var uri = r.RequestUri!; var name = Path.GetFileName(uri.AbsolutePath);
            if (name == "captions.vtt") return new(HttpStatusCode.OK) { Content = new StringContent(caption) };
            if (name == "captions.ttml") return new(HttpStatusCode.OK) { Content = new StringContent(ttml) };
            if (name is "high.mp4" or "low.mp4") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(assets[name]) };
        }));
        var request = MicrosoftEmbedChecks.Request(files.Directory, options) with { Url = mapped ? new("https://www.microsoft.com/videoplayer/embed/" + MicrosoftEmbedChecks.Id) : MicrosoftEmbedChecks.Url(options),
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteSubs = true, SubLanguages = "en-us", EmbedSubs = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "flat-ttml") request = request with { Url = new("https://www.microsoft.com/videoplayer/embed?mp4=" + Uri.EscapeDataString("LO,https://media.example/low.mp4" + MicrosoftEmbedChecks.Signed) +
            "&ttml=" + Uri.EscapeDataString("en-us,https://media.example/captions.ttml" + MicrosoftEmbedChecks.Signed)) };
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "embed.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Microsoft Cascade source was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("Failed Microsoft verification changed prior output or archives."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPath is null || callbacks.Count != result.OutputPaths.Count || result.OutputPaths.Count < 2) throw new InvalidOperationException("Microsoft Embed media/original outputs were not published once.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (video.ValueKind != JsonValueKind.Undefined && video.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Microsoft Embed lost complete video frames.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        var embedded = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "webvtt", "-"], rejectDiagnostics: true);
        if (!embedded.Contains("Native Microsoft Embed captions.")) throw new InvalidOperationException("Microsoft caption conversion/embedding lost text.");
        var originalCaption = result.SidecarPaths.Single(path => path.EndsWith(mode == "flat-ttml" ? ".ttml" : ".vtt"));
        if (await File.ReadAllTextAsync(originalCaption) != (mode == "flat-ttml" ? ttml : caption)) throw new InvalidOperationException("Original Microsoft captions changed bytes.");
        if (mapped && (await File.ReadAllTextAsync(archive)).Trim() != "microsoftembed " + MicrosoftEmbedChecks.Id) throw new InvalidOperationException("Mapped video archive identity changed.");
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(uri => uri.Host == "media.example")) throw new InvalidOperationException("Microsoft Embed archive retry transferred old media or repeated callbacks.");
    }
}
