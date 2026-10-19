using System.Buffers.Binary;
using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task SoundCloudHls(string ffmpeg, bool master)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "audio.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=800:sample_rate=44100:duration=0.4", "-vn", "-c:a", "aac", "-movflags", "frag_keyframe+empty_moov+default_base_moof", source]);
        var bytes = await File.ReadAllBytesAsync(source); var boundary = 0;
        while (boundary + 8 <= bytes.Length && !bytes.AsSpan(boundary + 4, 4).SequenceEqual("moof"u8))
            boundary += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary, 4)));
        if (boundary == 0 || boundary >= bytes.Length) throw new InvalidOperationException("The generated audio has no fragment.");
        var track = SoundCloudChecks.Track(); track["duration"] = 400; track["full_duration"] = 400;
        var sources = track["media"]!["transcodings"]!.AsArray(); sources.RemoveAt(0); sources[0]!["duration"] = 400;
        const string manifest = "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:0.4,\nsegment.m4s\n#EXT-X-ENDLIST";
        var engine = SoundCloudChecks.Engine(track, [], request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/playlist.m3u8") return SoundCloudChecks.Response(master ? "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=160000,CODECS=\"mp4a.40.2\"\naudio.m3u8" : manifest);
            if (path == "/audio.m3u8") return SoundCloudChecks.Response(manifest);
            return path is "/init.mp4" or "/segment.m4s" ? new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(path == "/init.mp4" ? bytes[..boundary] : bytes[boundary..]) } : null;
        });
        var result = await engine.DownloadAsync(SoundCloudChecks.Request(files.Directory) with { MaximumHeight = 240, MaximumFrameRate = 24, VideoCodec = "h264" }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null || Path.GetExtension(result.OutputPath) != ".m4a" || !File.ReadAllBytes(result.OutputPath).AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("Native SoundCloud AAC transfer changed bytes or audio format.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_name,codec_type,nb_read_frames", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var stream = document.RootElement.GetProperty("streams").EnumerateArray().Single();
        if (stream.GetProperty("codec_name").GetString() != "aac" || stream.GetProperty("codec_type").GetString() != "audio" || int.Parse(stream.GetProperty("nb_read_frames").GetString()!) < 17)
            throw new InvalidOperationException("The native audio HLS output did not retain complete AAC frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-f", "null", "-"]);
    }
}
