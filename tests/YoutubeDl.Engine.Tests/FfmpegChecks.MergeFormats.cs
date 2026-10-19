using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task MergePreferenceMedia(string ffmpeg, string target, bool unknown = false, bool failure = false)
    {
        using var files = new Files(); var extension = target == "mkv" ? "mp4" : target;
        var audioExtension = target switch { "mkv" => "m4a", "ogg" => "ogg", _ => "webm" };
        var video = Path.Combine(files.Directory, "video." + extension); var audio = Path.Combine(files.Directory, "audio." + audioExtension);
        var videoEncoder = target switch { "webm" => "libvpx-vp9", "ogg" => "libtheora", _ => "libx264" };
        var audioEncoder = target switch { "webm" => "libopus", "ogg" => "libvorbis", _ => "aac" };
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=1", "-an", "-c:v", videoEncoder, "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=1", "-vn", "-c:a", audioEncoder, audio]);
        var sources = new Dictionary<string, byte[]> { ["video"] = await File.ReadAllBytesAsync(video), ["audio"] = await File.ReadAllBytesAsync(audio) };
        var page = new PageMetadata { Id = "merged", Title = "Merged fixture", Media =
            [MergeFormatChecks.Candidate("video", extension, codec: unknown ? "unknown" : target switch { "webm" => "vp9", "ogg" => "theora", _ => "h264" }),
             MergeFormatChecks.Candidate("audio", audioExtension, true, codec: unknown ? "unknown" : target switch { "webm" => "opus", "ogg" => "vorbis", _ => "aac" })] };
        var engine = new NativeEngine([new MergeProvider(page)], _ => new MediaHttp(new Handler(request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(sources[request.RequestUri!.AbsolutePath.Trim('/')]) })));
        var archive = Path.Combine(files.Directory, "archive.txt"); var callbacks = new List<string>();
        var trap = Path.Combine(files.Directory, "result.webm"); if (unknown) await File.WriteAllTextAsync(trap, "previous unrelated webm");
        var request = MergeFormatChecks.Request with { OutputDirectory = files.Directory, FileName = "result", FfmpegPath = ffmpeg,
            MergeOutputFormat = failure ? "webm" : target == "mkv" ? "webm/mkv" : target + "/mkv", KeepOriginal = true, DownloadArchiveFile = archive, NoPostOverwrites = unknown && !failure };
        if (failure)
        {
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incompatible output was accepted."); }
            catch (NotSupportedException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(trap) != "previous unrelated webm") throw new InvalidOperationException("Failed merging changed published data.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (unknown && await File.ReadAllTextAsync(trap) != "previous unrelated webm") throw new InvalidOperationException("Codec resolution changed an unrelated output.");
        if (!result.Succeeded || result.OutputPath is null || Path.GetExtension(result.OutputPath) != "." + target || callbacks.Count != 3 || result.OutputPaths.Count != 3)
            throw new InvalidOperationException("The merge preference lost a file, its type or its callback.");
        if (!sources.Values.All(bytes => result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))) throw new InvalidOperationException("A retained merge input changed bytes.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        async Task<string[]> PacketHashes(string path, string role)
        {
            using var json = JsonDocument.Parse(await Run(probe, ["-v", "error", "-select_streams", role, "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", path]));
            return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();
        }
        var originalVideo = await PacketHashes(video, "v:0"); var mergedVideo = await PacketHashes(result.OutputPath, "v:0");
        var originalAudio = await PacketHashes(audio, "a:0"); var mergedAudio = await PacketHashes(result.OutputPath, "a:0");
        if (!originalVideo.SequenceEqual(mergedVideo) || !originalAudio.SequenceEqual(mergedAudio)) throw new InvalidOperationException("Merging changed source encoded packets.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);
        if ((await File.ReadAllTextAsync(archive)).Trim() != "generic merged") throw new InvalidOperationException("A successful merge did not reach the archive.");
    }

    private static async Task MergeUnpairedMedia(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=1", "-c:v", "libx264", source]);
        var bytes = await File.ReadAllBytesAsync(source);
        var result = await RecodeChecks.Engine(bytes, hasAudio: false).DownloadAsync(RecodeChecks.Request(files.Directory) with { MergeOutputFormat = "webm" }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null || Path.GetExtension(result.OutputPath) != ".mp4" || !(await File.ReadAllBytesAsync(result.OutputPath)).SequenceEqual(bytes))
            throw new InvalidOperationException("A merge preference changed an unpaired file or unnecessarily required ffmpeg.");
    }
    private sealed class MergeProvider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("merge-fixture", "Merge fixture", 1);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
}
