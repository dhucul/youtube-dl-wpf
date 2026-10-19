using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task BlueskyMedia(string ffmpeg, bool hls, bool silent = false, bool wrongSize = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=160x96:r=24:d=2" };
        if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", "libx264", "-g", "24", "-pix_fmt", "yuv420p"]); if (!silent) args.AddRange(["-c:a", "aac"]);
        args.Add(source); await Run(ffmpeg, args.ToArray()); var bytes = await File.ReadAllBytesAsync(source);
        var data = BlueskyChecks.Post(); data["thread"]!["post"]!["record"]!["embed"]!["video"]!["size"] = bytes.Length + (wrongSize ? 1 : 0);
        const string caption = "WEBVTT\n\n00:00:00.100 --> 00:00:01.800\nNative Bluesky captions.\n";
        var calls = new List<Uri>(); var completed = new List<string>(); byte[] original = bytes;
        if (hls)
        {
            var ts = Path.Combine(files.Directory, "segment.ts");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-f", "mpegts", ts]);
            original = await File.ReadAllBytesAsync(ts);
        }
        var engine = BlueskyChecks.Engine(calls, data, credentials: true, media: message =>
        {
            if (message.Headers.Contains("Authorization") || message.Headers.Contains("Cookie")) throw new InvalidOperationException("Credentials reached Bluesky playback or captions.");
            var uri = message.RequestUri!;
            if (uri.Host == "pds.example.test")
            {
                if (!Uri.UnescapeDataString(uri.Query).Contains("did=" + BlueskyChecks.Did)) throw new InvalidOperationException("Bluesky changed the blob account.");
                if (uri.Query.Contains("cid=" + BlueskyChecks.CaptionCid)) return new(HttpStatusCode.OK) { Content = new StringContent(caption) };
                if (!hls && uri.Query.Contains("cid=" + BlueskyChecks.Cid)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            if (hls && uri.Host == "video.bsky.app")
            {
                if (uri.AbsolutePath.EndsWith("playlist.m3u8"))
                {
                    if (uri.Query != "?Key=A%2Bb&case=C%2Fd") throw new InvalidOperationException("Bluesky changed literal HLS access parameters.");
                    return new(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nsegment.ts\n#EXT-X-ENDLIST\n") };
                }
                if (uri.AbsolutePath.EndsWith("segment.ts")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(original) };
            }
            throw new InvalidOperationException("Unexpected or unselected Bluesky resource.");
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = BlueskyChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, FormatSelector = hls ? "hls" : "original", Container = "mp4", AddMetadata = true, KeepOriginal = true,
            WriteSubs = true, EmbedSubs = true, SubLanguages = "en", DownloadArchiveFile = archive };
        if (wrongSize)
        {
            var previous = Path.Combine(files.Directory, "bluesky.mp4"); await File.WriteAllTextAsync(previous, "previous complete file");
            try { await engine.DownloadAsync(request, null, null, default, completed.Add); throw new InvalidOperationException("An inconsistent Bluesky original was accepted."); }
            catch (InvalidDataException ex) when (ex.Message.Contains("file size")) { }
            if (completed.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete file") throw new InvalidOperationException("Failed blob verification changed existing outputs.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || completed.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(original))) throw new InvalidOperationException("Bluesky lost full media or exact originals.");
        foreach (var path in result.OutputPaths)
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"]);
        var output = result.OutputPath!;
        using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", output]));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48" || streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio") == silent)
            throw new InvalidOperationException("Bluesky processing lost video frames or changed the sound state.");
        var embedded = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "webvtt", "-"]);
        if (!embedded.Contains("Native Bluesky captions.") || !Directory.EnumerateFiles(files.Directory, "*.vtt").Any(path => File.ReadAllText(path) == caption)) throw new InvalidOperationException("Bluesky captions were not saved exactly and embedded.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "bluesky:post " + BlueskyChecks.Did + "/first") throw new InvalidOperationException("Bluesky used an unstable archive identity.");
        calls.Clear(); completed.Clear(); var repeated = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!repeated.Succeeded || !repeated.Skipped || calls.Count != 3 || completed.Count != 0) throw new InvalidOperationException("An archived Bluesky post transferred media again.");
    }
}
