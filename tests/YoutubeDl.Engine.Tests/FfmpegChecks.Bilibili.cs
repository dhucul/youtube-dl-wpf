using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task BilibiliMedia(string ffmpeg, string mode)
    {
        using var files = new Files();
        var video = Path.Combine(files.Directory, "source-video.mp4"); var audio = Path.Combine(files.Directory, "source-audio.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=" + (mode == "video" ? "64x48" : "128x96") + ":r=24:d=2", "-an", "-c:v", "libx264", "-g", "24", "-movflags", "frag_keyframe+empty_moov+default_base_moof", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=2", "-vn", "-c:a", "aac", "-movflags", "frag_keyframe+empty_moov+default_base_moof", audio]);
        var videoBytes = await File.ReadAllBytesAsync(video); var audioBytes = await File.ReadAllBytesAsync(audio);
        var calls = new List<Uri>(); var completed = new List<string>();
        if (mode is "corrupt" or "corrupt-video" or "corrupt-audio" or "wrong-role" or "empty-video" or "short-video")
        {
            if (mode is "corrupt" or "corrupt-video" or "corrupt-audio")
            {
                var damagedBytes = mode == "corrupt-audio" ? audioBytes : videoBytes;
                var marker = damagedBytes.AsSpan().IndexOf("mdat"u8);
                if (marker < 0) throw new InvalidOperationException("The generated video has no media payload.");
                damagedBytes[marker + 4] = 0x7f; damagedBytes[marker + 5] = 0xff; damagedBytes[marker + 6] = 0xff; damagedBytes[marker + 7] = 0xff;
                if (mode == "corrupt")
                {
                    var damaged = Path.Combine(files.Directory, "damaged.mp4"); await File.WriteAllBytesAsync(damaged, videoBytes);
                    try
                    {
                        await YoutubeDl.Engine.Media.FfmpegTool.MergeAsync(ffmpeg, damaged, audio, Path.Combine(files.Directory, "rejected-merge.mp4"), default);
                        throw new InvalidOperationException("Stream-copy errors were accepted by the shared merger.");
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException && ex.Message.StartsWith("ffmpeg failed:")) { }
                }
            }
            else if (mode == "wrong-role") videoBytes = audioBytes;
            else if (mode == "empty-video")
            {
                var fragment = videoBytes.AsSpan().IndexOf("moof"u8);
                if (fragment < 4) throw new InvalidOperationException("The generated video has no fragment.");
                videoBytes = videoBytes[..(fragment - 4)];
            }
            var prior = Path.Combine(files.Directory, "bilibili.mp4"); var archive = Path.Combine(files.Directory, "archive.txt");
            await File.WriteAllTextAsync(prior, "previous complete download");
            var metadata = BilibiliChecks.Metadata();
            if (mode == "short-video") metadata["data"]!["pages"]![0]!["duration"] = 20;
            try
            {
                await BilibiliChecks.Engine(calls, metadata, playback: cid => { var data = BilibiliChecks.Playback(cid); if (mode == "short-video") data["data"]!["timelength"] = 20000; return data; },
                        media: url => url.AbsolutePath.Contains("30280") ? audioBytes : videoBytes)
                    .DownloadAsync(BilibiliChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = archive,
                        VideoOnly = mode is not ("corrupt" or "corrupt-audio"), AudioOnly = mode == "corrupt-audio", AudioFormat = "best" }, null, null, default, completed.Add);
                throw new InvalidOperationException("Corrupt media was accepted despite ffmpeg diagnostics.");
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException && ex.Message.StartsWith("ffmpeg failed:")) { }
            if (completed.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "previous complete download" || Directory.GetFiles(files.Directory, "bilibili.*").Length != 1)
                throw new InvalidOperationException("Corrupt merging changed published files, archive or completion callbacks.");
            if (mode != "corrupt-audio" && calls.Any(url => url.AbsolutePath.Contains("30280"))) throw new InvalidOperationException("Invalid video triggered a companion audio transfer.");
            return;
        }
        var result = await BilibiliChecks.Engine(calls, media: url => url.AbsolutePath.Contains("30280") ? audioBytes : videoBytes, observe: request =>
            { if (request.Headers.UserAgent.ToString() != "NativeTest/1.0" || request.Headers.Referrer?.AbsoluteUri != "https://embedding.example/page") throw new InvalidOperationException("A media request lost the caller headers."); })
            .DownloadAsync(BilibiliChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = mode == "pair", AddMetadata = mode == "pair",
                WriteInfoJson = true, VideoOnly = mode == "video", MaximumHeight = mode == "video" ? 48 : null, AudioOnly = mode == "audio", AudioFormat = "best",
                UserAgent = "NativeTest/1.0", Referer = "https://embedding.example/page" }, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPath is null || completed.Count != result.NewOutputPaths?.Count) throw new InvalidOperationException("Bilibili failed to publish complete generated media.");
        var requestedMedia = calls.Where(url => url.Host != "api.bilibili.com").ToArray();
        if (requestedMedia.Length != (mode == "pair" ? 2 : 1)) throw new InvalidOperationException("Bilibili downloaded the wrong media roles.");
        if (mode == "pair" && requestedMedia.Single(url => url.AbsolutePath.Contains("30032")).Query != "?token=a%2Bb%2Fc&x=1&x=2")
            throw new InvalidOperationException("Bilibili altered signed representation query bytes.");
        if (mode == "video" && !requestedMedia.Single().AbsolutePath.Contains("30016")) throw new InvalidOperationException("Bilibili ignored the maximum-height selection.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        async Task<string[]> Counts(string path)
        {
            using var json = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return json.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()).Order().ToArray();
        }
        var expected = (mode == "audio" ? Array.Empty<string>() : await Counts(video)).Concat(mode == "video" ? Array.Empty<string>() : await Counts(audio)).Order().ToArray();
        if (!(await Counts(result.OutputPath)).SequenceEqual(expected)) throw new InvalidOperationException("Bilibili output lost or added media frames.");
        if (mode == "pair")
        {
            if (result.OutputPaths.Count != 4 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(videoBytes)) || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(audioBytes)))
                throw new InvalidOperationException("Bilibili did not retain exact adaptive originals.");
            async Task<decimal> Start(string path, string kind)
            {
                using var json = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-select_streams", kind, "-show_entries", "stream=start_time", "-of", "json", path]));
                return decimal.Parse(json.RootElement.GetProperty("streams")[0].GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            }
            var originalOffset = await Start(video, "v") - await Start(audio, "a");
            var mergedOffset = await Start(result.OutputPath, "v") - await Start(result.OutputPath, "a");
            if (originalOffset <= 0 || Math.Abs(originalOffset - mergedOffset) > .000001m) throw new InvalidOperationException("Bilibili merging changed the shared source audio/video timing.");
        }
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0", "-f", "null", "-"]);
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(result.OutputPath, ".info.json")));
        if (info.RootElement.GetProperty("id").GetString() != BilibiliChecks.Bvid + "_1445374") throw new InvalidOperationException("Sidecar lost the part identity.");
    }
}
