using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.MicrosoftLearn;

internal static partial class FfmpegChecks
{
    private static async Task MicrosoftLearnMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var dash = mode is "dash" or "incomplete-dash"; var audio = mode == "audio"; var separate = mode == "split"; var source = Path.Combine(files.Directory, audio ? "audio.m4a" : "high.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y" };
        if (!audio) args.AddRange(["-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2"]);
        if (!separate) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=2"]);
        if (!audio) args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "24"]);
        if (!separate) args.AddRange(["-c:a", "aac"]); args.Add(source); await Run(ffmpeg, args);
        var sourceBytes = await File.ReadAllBytesAsync(source); var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? separateAudio = null;
        if (separate)
        {
            var audioFile = Path.Combine(files.Directory, "separate.m4a");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=2", "-c:a", "aac", audioFile]);
            separateAudio = await File.ReadAllBytesAsync(audioFile);
        }
        if (mode == "hls")
        {
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(files.Directory, "master.m3u8")]);
        }
        if (dash)
        {
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "dash", "-seg_duration", "1", "-use_template", "1", "-use_timeline", "1", Path.Combine(files.Directory, "source.mpd")]);
        }
        foreach (var path in System.IO.Directory.EnumerateFiles(files.Directory)) assets[Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        var entry = MicrosoftLearnChecks.Entry(duration: mode == "incomplete-dash" ? 20 : 2);
        if (audio) entry["publicVideo"]!["audioUrl"] = MicrosoftLearnChecks.Origin + "audio.mp4" + MicrosoftLearnChecks.Query;
        if (separate)
        {
            entry["publicVideo"]!["highQualityVideoUrl"] = MicrosoftLearnChecks.Origin + "fixture__160x96_AACAudio_100_1.mp4" + MicrosoftLearnChecks.Query;
            entry["publicVideo"]!["audioUrl"] = MicrosoftLearnChecks.Origin + "fixture__AACAudio_2Ch_192kbps.mp4" + MicrosoftLearnChecks.Query;
        }
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        const string captions = "WEBVTT\n\n00:00:00.100 --> 00:00:01.800\nNative Microsoft Learn captions.\n";
        var engine = new NativeEngine([new MicrosoftLearnProvider()], _ => MicrosoftLearnChecks.Http(calls, mode == "show" ? MicrosoftLearnChecks.Html("") : null, entry,
            first: MicrosoftLearnChecks.Listing(2, null, MicrosoftLearnChecks.Row("hidden", MicrosoftLearnChecks.SecondId, true), MicrosoftLearnChecks.Row()), media: r =>
            {
                if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Caller credentials reached Microsoft playback.");
                var uri = r.RequestUri!; var name = Path.GetFileName(uri.AbsolutePath);
                if (!uri.AbsolutePath.StartsWith("/public-" + MicrosoftLearnChecks.Id + "/")) throw new InvalidOperationException("An unselected Microsoft episode transferred media.");
                if (name == "captions.vtt") return new(HttpStatusCode.OK) { Content = new StringContent(captions) };
                if (separate && name.Contains("__AACAudio_2Ch_")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(separateAudio!) };
                if (separate && name.Contains("__160x96_")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(sourceBytes) };
                if (name is "high.mp4" or "audio.mp4") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(sourceBytes) };
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(assets[name]) };
            }, pages: mode == "show" ? uri => uri.AbsolutePath.TrimEnd('/') == "/en-us/shows/fixture" ? MicrosoftLearnChecks.Html("") : MicrosoftLearnChecks.Html() : null));
        var request = MicrosoftLearnChecks.Request(files.Directory) with { Url = mode == "show" ? new("https://learn.microsoft.com/en-us/shows/fixture/") : MicrosoftLearnChecks.Request(files.Directory).Url,
            FfmpegPath = ffmpeg, FormatId = audio ? "audio" : dash ? "dash" : mode == "hls" ? "hls" : "high", AudioOnly = audio, AudioFormat = "best",
            KeepOriginal = true, AddMetadata = true, WriteSubs = true, SubLanguages = "en-us", EmbedSubs = !audio, WriteInfoJson = true, DownloadArchiveFile = archive, PlaylistItems = mode == "show" ? "2" : "" };
        if (mode == "incomplete-dash")
        {
            var prior = Path.Combine(files.Directory, "learn.mp4"); await File.WriteAllTextAsync(prior, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Microsoft DASH media was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete movie") throw new InvalidOperationException("DASH verification changed previous output or archive state."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count < 2 || callbacks.Count != result.OutputPaths.Count) throw new InvalidOperationException("Microsoft media/original publication was incomplete or repeated: " + string.Join("; ", result.Failures.Select(failure => failure.Message)));
        var output = result.OutputPath ?? result.OutputPaths[0];
        if (separate)
        {
            using var pair = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type", "-of", "json", output], rejectDiagnostics: true));
            if (result.OutputPaths.Count != 4 || !pair.RootElement.GetProperty("streams").EnumerateArray().Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))
                throw new InvalidOperationException("Microsoft progressive source tracks were not paired completely.");
        }
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
            if (audio && video.ValueKind != JsonValueKind.Undefined || video.ValueKind != JsonValueKind.Undefined && video.GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Microsoft playback lost video frames or audio-only roles.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "microsoftlearn " + MicrosoftLearnChecks.Id) throw new InvalidOperationException("Microsoft archive lost its original video identity.");
        if (await File.ReadAllTextAsync(result.SidecarPaths.Single(p => p.EndsWith(".vtt"))) != captions) throw new InvalidOperationException("Microsoft original captions changed.");
        if (!audio)
        {
            var embedded = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "webvtt", "-"], rejectDiagnostics: true);
            if (!embedded.Contains("Native Microsoft Learn captions.")) throw new InvalidOperationException("Microsoft embedded caption text disappeared.");
        }
        if (mode == "show") { using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(p => p.EndsWith(".info.json")))); if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("Microsoft selected episode lost its source position."); }
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(u => u.Host.Contains("azurefd"))) throw new InvalidOperationException("Microsoft archive retry transferred media or notified old files.");
    }
}
