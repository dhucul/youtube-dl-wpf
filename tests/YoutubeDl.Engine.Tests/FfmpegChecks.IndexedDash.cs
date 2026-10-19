using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task IndexedDashMedia(string ffmpeg, int concurrentFragments = 1, string httpChunkSize = "")
    {
        using var files = new Files();
        var video = Path.Combine(files.Directory, "video.mp4"); var audio = Path.Combine(files.Directory, "audio.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.6", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "2", "-bf", "0", "-an", "-movflags", "+frag_keyframe+empty_moov+default_base_moof+global_sidx", "-frag_duration", "200000", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.6", "-c:a", "aac", "-vn", "-movflags", "+frag_keyframe+empty_moov+default_base_moof+global_sidx", "-frag_duration", "200000", audio]);
        var assets = new Dictionary<string, byte[]> { ["video.mp4"] = await File.ReadAllBytesAsync(video), ["audio.mp4"] = await File.ReadAllBytesAsync(audio) };
        string Representation(string name, string mime, string id, string attributes)
        {
            var bytes = assets[name]; var offset = 0;
            while (offset + 8 <= bytes.Length)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
                if (length < 8 || length > bytes.Length - offset) throw new InvalidDataException("Invalid generated MP4 box.");
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("sidx"u8))
                    return $"<AdaptationSet mimeType='{mime}'><Representation id='{id}' {attributes}><BaseURL>{name}</BaseURL><SegmentBase indexRange='{offset}-{offset + length - 1}'><Initialization range='0-{offset - 1}'/></SegmentBase></Representation></AdaptationSet>";
                offset += length;
            }
            throw new InvalidDataException("ffmpeg did not generate a global segment index.");
        }
        var manifest = "<MPD mediaPresentationDuration='PT0.63S'><Period>" + Representation("video.mp4", "video/mp4", "video", "height='96' codecs='avc1.42c00a'") + Representation("audio.mp4", "audio/mp4", "audio", "codecs='mp4a.40.2'") + "</Period></MPD>";
        var ranges = 0;
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            if (name == "source.mpd") return new(HttpStatusCode.OK) { Content = new StringContent(manifest) };
            if (request.Headers.Range is null) throw new InvalidOperationException("Indexed DASH fetched an unbounded whole track.");
            if (httpChunkSize.Length > 0 && request.Headers.Range.Ranges.Single() is { From: { } from, To: { } to } && to - from + 1 > 128)
                throw new InvalidOperationException("Indexed DASH ignored the configured subrange limit.");
            Interlocked.Increment(ref ranges); return DashIndexChecks.Range(request, assets[name]);
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new Uri("https://media.example/source.mpd"), OutputDirectory = files.Directory, FileName = "indexed-result", FfmpegPath = ffmpeg, Retries = 0, ConcurrentFragments = concurrentFragments, HttpChunkSize = httpChunkSize }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null || ranges < 6) throw new InvalidOperationException("Indexed DASH did not complete both ranged tracks.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(item => item.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "6" || !streams.Any(item => item.GetProperty("codec_type").GetString() == "audio"))
            throw new InvalidOperationException("Indexed DASH lost frames or an audio track.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
    }
}
