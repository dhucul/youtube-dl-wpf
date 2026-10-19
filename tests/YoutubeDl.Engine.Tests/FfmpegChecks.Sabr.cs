using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static partial class FfmpegChecks
{
    private static async Task SabrMedia(string ffmpeg, string mode, bool keep = false, bool observed = false)
    {
        using var files = new Files();
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var video = Path.Combine(files.Directory, "source-video.mp4"); var audio = Path.Combine(files.Directory, "source-audio.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=0.4", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "frag_keyframe+empty_moov+default_base_moof", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=550:sample_rate=44100:duration=0.4", "-vn", "-c:a", "aac", "-movflags", "frag_keyframe+empty_moov+default_base_moof", audio]);
        var tracks = new Dictionary<SabrFormatId, byte[]> { [SabrFixture.Video] = await File.ReadAllBytesAsync(video), [SabrFixture.Audio] = await File.ReadAllBytesAsync(audio) };
        var requests = 0;
        var engine = new NativeEngine(providers: observed ? [new ObservedYouTubeChecks.Provider()] : null, httpFactory: _ => new MediaHttp(new SabrFixture.Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/watch") return new(HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Watch) };
            if (request.RequestUri.AbsolutePath == "/youtubei/v1/player") return new(HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Player().ToJsonString()) };
            if (request.RequestUri != SabrFixture.Endpoint) throw new InvalidOperationException("Unexpected media request.");
            requests++;
            var fields = ProtobufReader.Read(await request.Content!.ReadAsByteArrayAsync());
            if (observed)
            {
                var context = ProtobufReader.Read(fields.Single(field => field.Number == 19).Bytes);
                if (!context.Single(field => field.Number == 1).Bytes.Span.SequenceEqual(ObservedYouTubeChecks.ClientInfo()) ||
                    !context.Single(field => field.Number == 2).Bytes.Span.SequenceEqual(ObservedYouTubeChecks.Proof)) throw new InvalidOperationException("The download dropped its observed playback context.");
            }
            var selected = fields.Where(field => field.Number is 16 or 17).Select(field => ProtobufReader.Read(field.Bytes).Single(part => part.Number == 1).Integer).ToArray();
            var expected = mode == "pair" ? new ulong[] { 137, 140 } : mode == "audio" ? [140UL] : [137UL];
            if (!selected.Order().SequenceEqual(expected.Order())) throw new InvalidOperationException("The native SABR selection included an unwanted track.");
            using var response = new MemoryStream(); SabrFixture.Status(response, 2);
            byte id = 0;
            foreach (var (format, bytes) in tracks.Where(pair => selected.Contains(pair.Key.Itag)))
            {
                var boundary = 0;
                while (boundary + 8 <= bytes.Length && !bytes.AsSpan(boundary + 4, 4).SequenceEqual("moof"u8))
                    boundary += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary, 4)));
                if (boundary == 0 || boundary >= bytes.Length) throw new InvalidOperationException("The generated fixture has no fragments.");
                SabrFixture.Metadata(response, format, 1, 400);
                SabrFixture.Segment(response, format, ++id, 0, bytes[..boundary]);
                SabrFixture.Segment(response, format, ++id, 1, bytes[boundary..], new(0, 400, 1000));
            }
            return SabrFixture.Response(response.ToArray());
        }), new MediaHttpOptions { AllowAutoRedirect = false }));
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://youtu.be/" + SabrFixture.VideoId), OutputDirectory = files.Directory,
            FileName = "completed", FormatSelector = mode == "pair" ? "137+140:en.0" : mode == "audio" ? "140:en.0" : "137", FfmpegPath = ffmpeg, Retries = 0, KeepOriginal = keep }, null, null, default);
        if (!result.Succeeded || requests != 1 || result.OutputPath is null) throw new InvalidOperationException("The native SABR media did not finish.");
        var actual = await Counts(result.OutputPath);
        var sourceVideo = await Counts(video); var sourceAudio = await Counts(audio);
        if (actual.Count != (mode == "pair" ? 2 : 1) || mode != "audio" && actual.GetValueOrDefault("video") != sourceVideo["video"] ||
            mode != "video" && actual.GetValueOrDefault("audio") != sourceAudio["audio"])
            throw new InvalidOperationException("SABR processing lost media frames or included unwanted tracks.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-f", "null", "-"]);
        if (keep) await VerifyKeptComponents(ffmpeg, result, files.Directory);

        async Task<Dictionary<string, string?>> Counts(string path)
        {
            using var document = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return document.RootElement.GetProperty("streams").EnumerateArray().ToDictionary(stream => stream.GetProperty("codec_type").GetString()!, stream => stream.GetProperty("nb_read_frames").GetString());
        }
    }
}
