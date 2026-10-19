using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> StreamQualityMediaChecks(string ffmpeg) =>
        new[] { "aac", "libopus", "flac" }.Select(codec => ("Stream quality media default audio copies " + codec, (Func<Task>)(() => StreamQualityAudio(ffmpeg, codec))))
        .Append(("Stream quality media default video and best audio preserve original packets", () => StreamQualityPair(ffmpeg)));

    private static async Task StreamQualityAudio(string ffmpeg, string codec)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.4", "-c:a", codec, source]);
        var result = await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(new()
        { Url = new("https://media.example/source.mkv"), OutputDirectory = Path.Combine(files.Directory, "out"), AudioOnly = true, FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
        var before = await PacketHashes(ffmpeg, source); var after = await PacketHashes(ffmpeg, result.OutputPath!);
        if (!result.Succeeded || !before.SequenceEqual(after) ||
            await AudioHash(ffmpeg, source) != await AudioHash(ffmpeg, result.OutputPath!))
            throw new InvalidOperationException("Default audio output changed the original compressed packets or decoded samples.");
    }

    private static async Task StreamQualityPair(string ffmpeg)
    {
        using var files = new Files();
        var video = Path.Combine(files.Directory, "video.mp4"); var audio = Path.Combine(files.Directory, "audio.opus");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=30:d=0.4", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.4", "-c:a", "libopus", "-b:a", "256k", audio]);
        var engine = new NativeEngine([new StreamQualityProvider()], _ => new MediaHttp(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/aac") throw new InvalidOperationException("Selected lower-quality audio for container compatibility.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(request.RequestUri.AbsolutePath == "/video" ? video : audio)) };
        })));
        var result = await engine.DownloadAsync(new() { Url = new("https://media.example/watch"), OutputDirectory = Path.Combine(files.Directory, "out"), FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
        var beforeVideo = await StreamQualityPackets(ffmpeg, video, "v:0"); var afterVideo = await StreamQualityPackets(ffmpeg, result.OutputPath!, "v:0");
        var beforeAudio = await PacketHashes(ffmpeg, audio); var afterAudio = await PacketHashes(ffmpeg, result.OutputPath!);
        if (!result.Succeeded || Path.GetExtension(result.OutputPath) != ".mkv" ||
            !beforeVideo.SequenceEqual(afterVideo) || !beforeAudio.SequenceEqual(afterAudio))
            throw new InvalidOperationException("Default merge did not preserve original video and best-audio packets.");
    }
    private static async Task<string[]> StreamQualityPackets(string ffmpeg, string path, string stream)
    {
        using var json = JsonDocument.Parse(await Run(Probe(ffmpeg), ["-v", "error", "-select_streams", stream, "-show_packets", "-show_entries", "packet=data_hash", "-show_data_hash", "sha256", "-of", "json", path]));
        return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();
    }
    private sealed class StreamQualityProvider : IMediaProvider
    {
        public MediaProviderInfo Info => new("stream-quality-fixture", "Stream quality fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "stream-quality", Title = "Stream quality", Extractor = "fixture", Media =
        [
            new() { Id = "video", Url = new("https://media.example/video"), Extension = "mp4", HasVideo = true, HasAudio = false, VideoCodec = "h264", Height = 96, FramesPerSecond = 30 },
            new() { Id = "aac", Url = new("https://media.example/aac"), Extension = "m4a", HasVideo = false, HasAudio = true, AudioCodec = "aac", Bandwidth = 128000 },
            new() { Id = "opus", Url = new("https://media.example/opus"), Extension = "opus", HasVideo = false, HasAudio = true, AudioCodec = "opus", Bandwidth = 256000 }
        ] });
    }
}
