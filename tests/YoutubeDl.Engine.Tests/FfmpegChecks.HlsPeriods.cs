using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> HlsPeriodChecks =>
    [("HLS discontinuity markers retain separate timeline identities and sequence numbers", HlsPeriodParsing)];
    private static Task HlsPeriodParsing()
    {
        var playlist = HlsPlaylistParser.Parse("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:10\n#EXT-X-DISCONTINUITY-SEQUENCE:7\n#EXTINF:1,\na.ts\n#EXT-X-DISCONTINUITY\n#EXTINF:2,\nb.ts\n#EXT-X-ENDLIST", new("https://media.example/list.m3u8"));
        if (!playlist.MediaSegments.Select(segment => segment.DiscontinuitySequence).SequenceEqual(new long[] { 7, 8 }) ||
            !playlist.MediaSegments.Select(segment => segment.Sequence).SequenceEqual(new ulong[] { 10, 11 }) || playlist.UnsupportedFeatures.Count != 0)
            throw new InvalidOperationException("HLS timeline identity or media numbering was lost.");
        foreach (var declarations in new[] { "#EXT-X-DISCONTINUITY-SEQUENCE:7\n#EXT-X-DISCONTINUITY-SEQUENCE:8", "#EXT-X-DISCONTINUITY\n#EXT-X-DISCONTINUITY-SEQUENCE:7", "#EXT-X-DISCONTINUITY-SEQUENCE:9223372036854775807\n#EXT-X-DISCONTINUITY" })
        {
            try { HlsPlaylistParser.Parse("#EXTM3U\n" + declarations + "\n#EXTINF:1,\na.ts\n#EXT-X-ENDLIST", new("https://media.example/list.m3u8")); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("An invalid HLS period declaration was accepted.");
        }
        return Task.CompletedTask;
    }
    private static async Task HlsPeriods(string ffmpeg, bool incompatible, bool variableRate = false)
    {
        using var files = new Files(); var parts = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            var path = Path.Combine(files.Directory, "part" + index + ".ts"); parts.Add(path);
            var size = incompatible && index == 1 ? "96x64" : "128x96";
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=" + (index == 0 ? "red" : "blue") + ":s=" + size + ":r=" + (variableRate && index == 1 ? "20" : "10") + ":d=0.4",
                "-f", "lavfi", "-i", "sine=frequency=" + (index == 0 ? "440" : "880") + ":sample_rate=44100:duration=0.4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "4", "-c:a", "aac", "-f", "mpegts", path]);
        }
        var assets = parts.ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        const string manifest = "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:0.4,\npart0.ts\n#EXT-X-DISCONTINUITY\n#EXTINF:0.4,\npart1.ts\n#EXT-X-ENDLIST";
        var target = Path.Combine(files.Directory, "joined.mp4"); await File.WriteAllTextAsync(target, "previous output");
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK)
        { Content = request.RequestUri!.AbsolutePath.EndsWith(".m3u8") ? new StringContent(manifest) : new ByteArrayContent(assets[Path.GetFileName(request.RequestUri.AbsolutePath)]!) })));
        NativeDownloadResult result;
        try
        {
            result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.m3u8"), OutputDirectory = files.Directory,
                FileName = "joined", Container = "mp4", FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
            if (incompatible) throw new InvalidOperationException("A changing HLS stream layout was silently joined.");
        }
        catch (NotSupportedException) when (incompatible)
        {
            if (File.ReadAllText(target) != "previous output") throw new InvalidOperationException("An incompatible timeline replaced previous media.");
            return;
        }
        var expected = new List<byte>();
        foreach (var part in parts) expected.AddRange(await Pixels(part));
        if (!result.Succeeded || !(await Pixels(result.OutputPath!)).AsSpan().SequenceEqual(expected.ToArray()))
            throw new InvalidOperationException("Joining timeline periods dropped, duplicated or changed video frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-"]);

        async Task<byte[]> Pixels(string path)
        {
            var output = Path.Combine(files.Directory, Guid.NewGuid().ToString("N") + ".yuv");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough", "-pix_fmt", "yuv420p", "-f", "rawvideo", output]);
            return await File.ReadAllBytesAsync(output);
        }
    }
    private static async Task HlsPeriodOffset(string ffmpeg, bool fragmented = false)
    {
        using var files = new Files();
        var extension = fragmented ? ".mp4" : ".ts";
        string[] container = fragmented ? ["-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-f", "mp4"] : ["-f", "mpegts"];
        for (var index = 0; index < 2; index++)
        {
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.4", "-an", "-c:v", "libx264", "-bf", "0", "-muxdelay", "0", "-output_ts_offset", "0.2", "-video_track_timescale", "1000", .. container, Path.Combine(files.Directory, "v" + index + extension)]);
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.4", "-c:a", "aac", "-muxdelay", "0", .. container, Path.Combine(files.Directory, "a" + index + extension)]);
        }
        var assets = Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        if (fragmented)
            foreach (var name in assets.Keys.ToArray())
            {
                var bytes = assets[name]; var boundary = FragmentedInitializationLength(bytes);
                // empty_moov normalizes the generated first decode time to zero; model a real 200 ms fragment clock offset explicitly.
                if (name.StartsWith('v')) ShiftFragmentClock(bytes, 200);
                assets[name + ".init"] = bytes[..boundary]; assets[name] = bytes[boundary..];
            }
        const string master = "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"a\",DEFAULT=YES,URI=\"a.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=128x96,AUDIO=\"a\"\nv.m3u8";
        string Map(string name) => fragmented ? "#EXT-X-MAP:URI=\"" + name + ".init\"\n" : "";
        string Media(string type) => "#EXTM3U\n#EXT-X-TARGETDURATION:1\n" + Map(type + "0" + extension) + "#EXTINF:0.4,\n" + type + "0" + extension +
            "\n#EXT-X-DISCONTINUITY\n" + Map(type + "1" + extension) + "#EXTINF:0.4,\n" + type + "1" + extension + "\n#EXT-X-ENDLIST";
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : name.EndsWith(".m3u8") ? new StringContent(Media(name[..1])) : new ByteArrayContent(assets[name]) };
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/master.m3u8"), OutputDirectory = files.Directory, FileName = "offset", KeepOriginal = fragmented, FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
        using var document = System.Text.Json.JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_type,start_time", "-of", "json", result.OutputPath!]));
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        decimal Start(string kind) => decimal.Parse(streams.Single(stream => stream.GetProperty("codec_type").GetString() == kind).GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (!result.Succeeded || Math.Abs(Start("video") - Start("audio") - 0.2m) > 0.002m) throw new InvalidOperationException("The separate HLS clocks lost their declared relative offset.");
        if (fragmented && (!result.OutputPaths.Any(path => path.EndsWith(".video.mp4")) || !result.OutputPaths.Any(path => path.EndsWith(".audio.m4a"))))
            throw new InvalidOperationException("Joined fragmented HLS did not retain correctly labeled components.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-"]);
    }
}
