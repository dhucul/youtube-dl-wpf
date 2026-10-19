using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task RumbleMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var webm = mode == "webm"; var hls = mode.StartsWith("hls", StringComparison.Ordinal);
        var source = Path.Combine(files.Directory, "source." + (webm ? "webm" : "mp4"));
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=128x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2" };
        args.AddRange(webm ? ["-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus"] : ["-c:v", "libx264", "-g", "24", "-pix_fmt", "yuv420p", "-c:a", "aac"]);
        args.Add(source); await Run(ffmpeg, args);
        var sourceBytes = await File.ReadAllBytesAsync(source); var data = RumbleChecks.Playback();
        data["ua"]!.AsObject().Remove("mp4"); data["ua"]!.AsObject().Remove("hls");
        var mediaFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal); var expectedSource = source; string? playlist = null;
        if (hls)
        {
            var playlistPath = Path.Combine(files.Directory, "index.m3u8");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part-%02d.ts"), playlistPath]);
            var lines = await File.ReadAllLinesAsync(playlistPath); var transformed = new List<string>(); using var complete = new MemoryStream();
            foreach (var line in lines)
            {
                if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) { transformed.Add(line); continue; }
                var bytes = await File.ReadAllBytesAsync(Path.Combine(files.Directory, line)); mediaFiles.Add(line, bytes); complete.Write(bytes);
                transformed.Add("https://test.cdn.rumble.cloud/video.tar?r_file=" + line + "&r_type=video%2Fmp2t&r_range=42-4200&token=a%2Bb");
            }
            playlist = string.Join('\n', transformed); expectedSource = Path.Combine(files.Directory, "served.ts"); await File.WriteAllBytesAsync(expectedSource, complete.ToArray());
            sourceBytes = complete.ToArray(); data["ua"]!["hls"] = new JsonObject { ["auto"] = new JsonObject { ["url"] = "https://rumble.com/hls-vod/fixture/playlist.m3u8?token=literal%2Bvalue" } };
            if (mode == "hls-short") data["duration"] = 20;
        }
        else
        {
            var type = webm ? "webm" : "mp4";
            data["ua"]![type] = new JsonObject { ["360"] = new JsonObject { ["url"] = "https://test.cdn.rumble.cloud/video." + type + "?token=a%2Bb%2Fc&x=1&x=2",
                ["meta"] = new JsonObject { ["w"] = 128, ["h"] = 96, ["fps"] = 24, ["size"] = sourceBytes.Length, ["bitrate"] = 300 } } };
        }
        var calls = new List<Uri>(); var completed = new List<string>();
        var engine = RumbleChecks.Engine(calls, data, media: message =>
        {
            var url = message.RequestUri!; HttpContent content;
            if (url.AbsolutePath.StartsWith("/hls-vod/")) content = new StringContent("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=128x96,FRAME-RATE=24\nhttps://test.cdn.rumble.cloud/video.tar?r_file=index.m3u8&r_type=application%2Fvnd.apple.mpegurl&r_range=0-41&token=a%2Bb\n");
            else if (url.Query.Contains("r_file=index.m3u8")) content = new StringContent(playlist!);
            else if (hls)
            {
                if (message.Headers.Range is not null || !url.Query.Contains("r_range=42-4200&token=a%2Bb")) throw new InvalidOperationException("The server's archive extraction parameters were converted to HTTP ranges or changed.");
                var name = Uri.UnescapeDataString(url.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("r_file=")).Split('=', 2)[1]);
                content = new ByteArrayContent(mediaFiles[name]); content.Headers.ContentType = new MediaTypeHeaderValue("video/mp2t");
            }
            else content = new ByteArrayContent(sourceBytes);
            return new(HttpStatusCode.OK) { Content = content };
        }, observe: message =>
        {
            if (message.Headers.UserAgent.ToString() != "NativeTest/1.0" || message.Headers.Referrer?.AbsoluteUri != "https://embedding.example/")
                throw new InvalidOperationException("A Rumble media request lost the explicit session headers.");
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = RumbleChecks.Request(files.Directory) with { Url = new(RumbleChecks.WatchUrl), FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true,
            Container = hls && mode != "hls-audio" ? "mp4" : "", ConcurrentFragments = 2, DownloadArchiveFile = archive, MaximumHeight = 96,
            AudioOnly = mode == "hls-audio", VideoOnly = mode == "hls-video", AudioFormat = "best", UserAgent = "NativeTest/1.0", Referer = "https://embedding.example/" };
        if (mode == "hls-short")
        {
            var prior = Path.Combine(files.Directory, "rumble.mp4"); await File.WriteAllTextAsync(prior, "previous complete media");
            try { await engine.DownloadAsync(request, null, null, default, completed.Add); throw new InvalidOperationException("A shortened HLS presentation was accepted."); }
            catch (InvalidDataException) { }
            if (completed.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete media") throw new InvalidOperationException("Short HLS playback changed published data.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count != 2 || completed.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(sourceBytes)))
            throw new InvalidOperationException("Rumble publication lost complete media, original bytes or completion callbacks.");
        if (calls.Any(url => url.AbsolutePath is "/raw.tar" or "/preview.mp4")) throw new InvalidOperationException("Rumble downloaded a preview or raw archive instead of the playback resource.");
        if (!hls && calls.Last().Query != "?token=a%2Bb%2Fc&x=1&x=2") throw new InvalidOperationException("Rumble changed the progressive access query.");
        var expected = await Counts(expectedSource); var actual = await Counts(result.OutputPath);
        if (request.AudioOnly) expected.Remove("video"); if (request.VideoOnly) expected.Remove("audio");
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var count) || count != pair.Value)) throw new InvalidOperationException("Rumble output changed the complete selected media frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0", "-f", "null", "-"]);
        if ((await File.ReadAllTextAsync(archive)).Trim() != "rumble " + RumbleChecks.Id) throw new InvalidOperationException("Rumble wrote another archive identity.");
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(result.OutputPath, ".info.json")));
        if (info.RootElement.GetProperty("id").GetString() != RumbleChecks.Id) throw new InvalidOperationException("Rumble sidecars lost the player identity.");
        async Task<Dictionary<string, long>> Counts(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return json.RootElement.GetProperty("streams").EnumerateArray().ToDictionary(stream => stream.GetProperty("codec_type").GetString()!, stream => long.Parse(stream.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture));
        }
    }
}
