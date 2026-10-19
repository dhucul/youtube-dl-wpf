using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Panopto;

internal static partial class FfmpegChecks
{
    private static async Task PanoptoMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var duration = mode == "timescale" ? "12" : "2"; var sampleRate = mode == "timescale" ? "44100" : "48000";
        var sourceArgs = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=" + duration, "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=" + sampleRate + ":duration=" + duration };
        if (mode == "timescale")
        {
            var oldCaption = Path.Combine(files.Directory, "old.srt"); await File.WriteAllTextAsync(oldCaption, "1\n00:00:00,100 --> 00:00:11,000\nOld caption replaced by the requested language.\n");
            sourceArgs.AddRange(["-i", oldCaption, "-c:s", "mov_text"]);
        }
        sourceArgs.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "12", "-c:a", "aac", "-video_track_timescale", "90000", source]);
        await Run(ffmpeg, sourceArgs);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "segment%d.ts"), Path.Combine(files.Directory, "index.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var data = PanoptoChecks.Data(mode == "incomplete" ? 4 : mode == "timescale" ? 12 : 2);
        if (mode == "timescale") data["Delivery"]!["Timestamps"]![1]!["Time"] = 10;
        var prefix = "https://cdn.example/sessions/" + PanoptoChecks.SessionId + "/" + PanoptoChecks.DeliveryId + "-" + PanoptoChecks.SourceId + ".hls/";
        if (mode == "hls") data["Delivery"]!["PodcastStreams"] = new System.Text.Json.Nodes.JsonArray();
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24,CODECS=\"avc1.4d400c,mp4a.40.2\"\n" + prefix + "index.m3u8" + PanoptoChecks.Query + "\n";
        var lines = await File.ReadAllLinesAsync(Path.Combine(files.Directory, "index.m3u8"));
        var playlist = string.Join('\n', lines.Select(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line) ? line : prefix + line + PanoptoChecks.Query)) + "\n";
        var requests = new List<(HttpRequestMessage Request, string Body)>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var httpOptions = new YoutubeDl.Engine.Http.MediaHttpOptions(); httpOptions.Headers.Add("Authorization", "Bearer fixture-secret");
        var engine = new NativeEngine([new PanoptoProvider()], _ => PanoptoChecks.Http(data, requests, request =>
        {
            if (request.Headers.Contains("Authorization") || request.Headers.Contains("Cookie")) throw new InvalidOperationException("Discovery credentials reached anonymous Panopto CDN playback.");
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : name == "index.m3u8" ? new StringContent(playlist) :
                new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }, options: httpOptions));
        var request = PanoptoChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, WriteSubs = true, EmbedSubs = true, SubLanguages = "en.*", EmbedChapters = true,
            AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive, HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-secret" } };
        if (mode == "incomplete")
        {
            var previous = Path.Combine(files.Directory, "panopto.mp4"); await File.WriteAllTextAsync(previous, "previous complete file");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Truncated playback was accepted."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete file") throw new InvalidOperationException("Failed Panopto verification changed published data.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!requests.Any(call => call.Request.RequestUri!.Host == "campus.example" && call.Request.Headers.Contains("Authorization")))
            throw new InvalidOperationException("The credential-isolation check did not exercise an authenticated discovery context.");
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count != 2 || callbacks.Count != 2 || result.SidecarPaths.Count != 2 ||
            !result.NewSidecarPaths.SequenceEqual(result.SidecarPaths) || result.SidecarPaths.Any(path => !File.Exists(path))) throw new InvalidOperationException("Panopto media, its original or its caption/info files were not reported after publication.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        using var json = JsonDocument.Parse(await Run(probe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_name,codec_type,nb_read_frames:stream_tags=language:chapter=start_time,end_time:chapter_tags=title", "-of", "json", result.OutputPath], rejectDiagnostics: true));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != (mode == "timescale" ? "288" : "48") ||
            !streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio" && stream.GetProperty("codec_name").GetString() == "aac") || json.RootElement.GetProperty("chapters").GetArrayLength() != 2)
            throw new InvalidOperationException("Panopto processing lost frames, audio or chapter markers.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);
        var subtitle = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "srt", "-"]);
        if (!subtitle.Contains("Literal <b> & caption --> text") || !subtitle.Contains("00:00:00,100 --> 00:00:00,900")) throw new InvalidOperationException("Panopto embedded captions lost literal text or timing.");
        if (mode != "hls" && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(assets["source.mp4"]))) throw new InvalidOperationException("Panopto changed the requested original MP4 bytes.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "panopto:delivery campus.example/" + PanoptoChecks.DeliveryId || !File.Exists(Path.ChangeExtension(result.OutputPath, ".info.json")))
            throw new InvalidOperationException("Panopto sidecars or recording archive were lost.");
    }
}
