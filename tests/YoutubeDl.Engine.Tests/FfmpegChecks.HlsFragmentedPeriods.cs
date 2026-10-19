using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task HlsFragmentedPeriods(string ffmpeg, string mode)
    {
        using var files = new Files();
        var assets = new Dictionary<string, byte[]>(); var parts = new List<string>();
        var manifest = new StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-VERSION:7\n");
        for (var index = 0; index < 2; index++)
        {
            var path = Path.Combine(files.Directory, "source" + index + ".mp4"); parts.Add(path);
            var arguments = new List<string> { "-nostdin", "-v", "error", "-y" };
            if (mode != "audio") arguments.AddRange(["-f", "lavfi", "-i", "color=c=" + (index == 0 ? "red" : "blue") + ":s=128x96:r=10:d=0.8"]);
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=" + (index == 0 ? "440" : "880") + ":sample_rate=44100:duration=0.8"]);
            if (mode != "audio") arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-profile:v", mode == "config" && index == 1 ? "high" : "baseline", "-bf", "0", "-g", "4"]);
            arguments.AddRange(["-c:a", mode == "missing-config" ? "libmp3lame" : "aac", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", path]);
            await Run(ffmpeg, arguments.ToArray());
            var bytes = await File.ReadAllBytesAsync(path);
            var boundary = FragmentedInitializationLength(bytes);
            if (index > 0) manifest.Append("#EXT-X-DISCONTINUITY\n");
            if (mode == "ranges")
            {
                assets["part" + index + ".mp4"] = bytes;
                manifest.Append("#EXT-X-MAP:URI=\"part").Append(index).Append(".mp4\",BYTERANGE=\"").Append(boundary).Append("@0\"\n#EXTINF:0.8,\n#EXT-X-BYTERANGE:")
                    .Append(bytes.Length - boundary).Append('@').Append(boundary).Append("\npart").Append(index).Append(".mp4\n");
            }
            else
            {
                assets["init" + index + ".mp4"] = bytes[..boundary]; assets["part" + index + ".m4s"] = bytes[boundary..];
                manifest.Append("#EXT-X-MAP:URI=\"init").Append(index).Append(".mp4\"\n#EXTINF:0.8,\npart").Append(index).Append(".m4s\n");
            }
        }
        manifest.Append("#EXT-X-ENDLIST\n");
        var calls = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath); calls.Add(name);
            if (name == "source.m3u8") return new(HttpStatusCode.OK) { Content = new StringContent(manifest.ToString()) };
            var bytes = assets[name]; var range = request.Headers.Range?.Ranges.Single();
            if (range is null) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            var from = checked((int)range.From!.Value); var to = checked((int)range.To!.Value);
            var content = new ByteArrayContent(bytes[from..(to + 1)]); content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            return new(HttpStatusCode.PartialContent) { Content = content };
        })));
        var destination = Path.Combine(files.Directory, "joined.mp4");
        await File.WriteAllTextAsync(destination, "previous output");
        NativeDownloadResult result;
        try
        {
            result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.m3u8"), OutputDirectory = files.Directory, FileName = "joined",
                AudioOnly = mode == "audio", AudioFormat = "best", FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
            if (mode is "config" or "missing-config") throw new InvalidOperationException("An unverified fragmented stream decoder was silently joined.");
        }
        catch (NotSupportedException) when (mode is "config" or "missing-config")
        {
            if (File.ReadAllText(destination) != "previous output") throw new InvalidOperationException("A changed decoder replaced the existing output.");
            return;
        }
        if (!result.Succeeded) throw new InvalidOperationException("The fragmented timeline failed: " + result.Error);
        var expectedPackets = new List<string>();
        foreach (var part in parts) expectedPackets.AddRange(await AudioPackets(part));
        if (!(await AudioPackets(result.OutputPath!)).SequenceEqual(expectedPackets)) throw new InvalidOperationException("Fragmented timeline joining dropped or changed encoded audio packets.");
        if (mode != "audio")
        {
            var expectedPixels = new List<byte>();
            foreach (var part in parts) expectedPixels.AddRange(await Pixels(part));
            if (!(await Pixels(result.OutputPath!)).AsSpan().SequenceEqual(expectedPixels.ToArray())) throw new InvalidOperationException("Fragmented timeline joining changed or lost video frames.");
        }
        else if (Path.GetExtension(result.OutputPath) != ".m4a") throw new InvalidOperationException("Fragmented audio was saved in the wrong container.");
        if (calls.Count != 5) throw new InvalidOperationException("The periods did not request exactly their initialization and media resources.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-"]);

        async Task<byte[]> Pixels(string path)
        {
            var output = Path.Combine(files.Directory, Guid.NewGuid().ToString("N") + ".yuv");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough", "-pix_fmt", "yuv420p", "-f", "rawvideo", output]);
            return await File.ReadAllBytesAsync(output);
        }
        async Task<string[]> AudioPackets(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", path]));
            return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();
        }
    }
    private static int FragmentedInitializationLength(byte[] bytes)
    {
        var boundary = 0;
        while (boundary + 8 <= bytes.Length && Encoding.ASCII.GetString(bytes, boundary + 4, 4) != "moof")
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary));
            if (length < 8 || length > bytes.Length - boundary) throw new InvalidDataException("The generated MP4 has an invalid box.");
            boundary += checked((int)length);
        }
        if (boundary + 8 > bytes.Length) throw new InvalidDataException("The generated MP4 has no media fragment.");
        return boundary;
    }
    private static void ShiftFragmentClock(byte[] bytes, ulong ticks)
    {
        var changed = 0;
        Visit(0, bytes.Length);
        if (changed == 0) throw new InvalidDataException("The generated fragmented video has no decode clock.");
        void Visit(int start, int end)
        {
            for (var offset = start; offset + 8 <= end;)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
                if (length < 8 || length > end - offset) throw new InvalidDataException("Invalid generated MP4 box.");
                var kind = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                if (kind is "moof" or "traf") Visit(offset + 8, offset + length);
                else if (kind == "tfdt")
                {
                    if (bytes[offset + 8] == 1) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(offset + 12), checked(BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 12)) + ticks));
                    else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 12), checked(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12)) + (uint)ticks));
                    changed++;
                }
                offset += length;
            }
        }
    }
}
