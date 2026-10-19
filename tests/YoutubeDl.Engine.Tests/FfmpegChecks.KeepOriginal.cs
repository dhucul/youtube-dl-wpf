using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task KeepOriginal(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "input.mp4");
        var audioSource = mode == "same-audio";
        var generate = new List<string> { "-nostdin", "-v", "error", "-y" };
        if (!audioSource) generate.AddRange(["-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3"]);
        generate.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.3"]);
        if (!audioSource) generate.AddRange(["-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        generate.AddRange(["-c:a", "aac", source]); await Run(ffmpeg, generate);
        var bytes = await File.ReadAllBytesAsync(source);
        var originalExtension = audioSource ? "m4a" : "mp4";
        var request = new NativeDownloadRequest { Url = new("https://media.example/source." + originalExtension), OutputDirectory = files.Directory, FileName = "kept",
            KeepOriginal = true, AudioOnly = mode is not ("video" or "noop"), VideoOnly = mode == "video", AudioFormat = audioSource ? "m4a" : "mp3",
            OutputTemplate = mode == "folders" ? "%(ext)s/%(title)s.%(ext)s" : "", WriteInfoJson = mode != "noop", FfmpegPath = ffmpeg, Retries = 0 };
        var resultExtension = mode is "video" or "noop" ? "mp4" : audioSource ? "m4a" : "mp3";
        var primary = mode == "folders" ? Path.Combine(files.Directory, resultExtension, "Keep fixture." + resultExtension) : Path.Combine(files.Directory, "kept." + resultExtension);
        var original = mode == "folders" ? Path.Combine(files.Directory, originalExtension, "Keep fixture." + originalExtension) :
            Path.Combine(files.Directory, "kept" + (originalExtension == resultExtension ? ".original" : "") + "." + originalExtension);
        var callbacks = new List<string>();
        var engine = KeepEngine(bytes, originalExtension, audioSource);
        var result = await engine.DownloadAsync(request, null, null, default, path =>
        {
            callbacks.Add(path);
            if (!File.Exists(primary) || mode != "noop" && !File.Exists(original)) throw new InvalidOperationException("A completion callback observed an incomplete output set.");
            using var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        });
        if (!result.Succeeded || result.OutputPath != primary || result.OutputPaths.Count != (mode == "noop" ? 1 : 2) || callbacks.Count != result.OutputPaths.Count)
            throw new InvalidOperationException("Keep-original output paths or completion callbacks were incomplete.");
        if (mode == "noop")
        {
            if (!File.ReadAllBytes(primary).AsSpan().SequenceEqual(bytes) || File.Exists(original)) throw new InvalidOperationException("An unchanged download created an unnecessary original copy.");
        }
        else if (!File.ReadAllBytes(original).AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException("The retained original's bytes changed.");
        var streams = await AudioStreams(ffmpeg, primary);
        if (request.AudioOnly && streams.Any(stream => stream.GetProperty("codec_type").GetString() == "video") || request.VideoOnly && streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))
            throw new InvalidOperationException("The processed file kept an unwanted media stream.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", primary, "-f", "null", "-"]);
        AssertNoPublicationFiles(files.Directory);
    }

    private static async Task KeepPair(string ffmpeg, bool convert)
    {
        using var files = new Files(); var video = Path.Combine(files.Directory, "input-video.mp4"); var audio = Path.Combine(files.Directory, "input-audio.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=500:duration=0.3", "-c:a", "aac", audio]);
        var videoBytes = await File.ReadAllBytesAsync(video); var audioBytes = await File.ReadAllBytesAsync(audio);
        var request = new NativeDownloadRequest { Url = new("https://media.example/watch"), OutputDirectory = files.Directory, FileName = "pair", KeepOriginal = true,
            FormatSelector = "v+a", AudioOnly = convert, AudioFormat = "mp3", FfmpegPath = ffmpeg, Retries = 0 };
        var completed = new List<string>();
        var result = await KeepEngine(videoBytes, "mp4", false, audioBytes).DownloadAsync(request, null, null, default, completed.Add);
        var keptVideo = Path.Combine(files.Directory, "pair.video.mp4"); var keptAudio = Path.Combine(files.Directory, "pair.audio.m4a");
        if (!result.Succeeded || result.OutputPaths.Count != (convert ? 4 : 3) || completed.Count != result.OutputPaths.Count ||
            !File.ReadAllBytes(keptVideo).AsSpan().SequenceEqual(videoBytes) || !File.ReadAllBytes(keptAudio).AsSpan().SequenceEqual(audioBytes))
            throw new InvalidOperationException("An adaptive merge lost its complete original components.");
        foreach (var output in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);
        AssertNoPublicationFiles(files.Directory);
    }

    private static async Task KeepExisting(string ffmpeg, bool primaryExists)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "input.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var primary = Path.Combine(files.Directory, "kept.mp3"); var original = Path.Combine(files.Directory, "kept.mp4");
        var prior = primaryExists ? primary : original; await File.WriteAllTextAsync(prior, "existing user file");
        var callbacks = new List<string>();
        var result = await KeepEngine(bytes, "mp4", true).DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.mp4"), OutputDirectory = files.Directory,
            FileName = "kept", KeepOriginal = true, AudioOnly = true, AudioFormat = "mp3", NoOverwrites = true, WriteInfoJson = true,
            AddMetadata = primaryExists, FfmpegPath = primaryExists ? null : ffmpeg, Retries = 0 }, null, null, default, callbacks.Add);
        var created = primaryExists ? original : primary;
        if (!result.Succeeded || result.Skipped || result.OutputPaths.Count != 2 || callbacks.Count != 1 || callbacks[0] != created ||
            result.NewOutputPaths?.Single() != created || File.ReadAllText(prior) != "existing user file")
            throw new InvalidOperationException("Partial no-overwrite changed an existing output or reported it as newly saved.");
        if (primaryExists && (File.Exists(Path.ChangeExtension(primary, ".info.json")) || !File.ReadAllBytes(original).AsSpan().SequenceEqual(bytes)))
            throw new InvalidOperationException("Filling a missing original processed or changed the existing primary output.");
        AssertNoPublicationFiles(files.Directory);
    }

    private static async Task KeepRollback(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "input.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var audioDirectory = Path.Combine(files.Directory, "mp3"); var videoDirectory = Path.Combine(files.Directory, "mp4"); Directory.CreateDirectory(audioDirectory); Directory.CreateDirectory(videoDirectory);
        var audio = Path.Combine(audioDirectory, "Keep fixture.mp3"); var original = Path.Combine(videoDirectory, "Keep fixture.mp4"); var info = Path.ChangeExtension(audio, ".info.json"); var archive = Path.Combine(files.Directory, "archive.txt");
        foreach (var path in new[] { audio, original, info }) await File.WriteAllTextAsync(path, "prior " + Path.GetExtension(path));
        await File.WriteAllTextAsync(archive, "fixture prior\n");
        var callbacks = 0;
        using (var locked = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                await KeepEngine(await File.ReadAllBytesAsync(source), "mp4", true).DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.mp4"),
                    OutputDirectory = files.Directory, OutputTemplate = "%(ext)s/%(title)s.%(ext)s", KeepOriginal = true, AudioOnly = true, AudioFormat = "mp3", WriteInfoJson = true,
                    DownloadArchiveFile = archive, FfmpegPath = ffmpeg, Retries = 0 }, null, null, default, _ => callbacks++);
                throw new InvalidOperationException("A locked original was overwritten.");
            }
            catch (IOException) { }
        }
        if (callbacks != 0 || new[] { audio, original, info }.Any(path => File.ReadAllText(path) != "prior " + Path.GetExtension(path)) || File.ReadAllText(archive) != "fixture prior\n")
            throw new InvalidOperationException("A failure saving the original did not restore the complete previous output set.");
        AssertNoPublicationFiles(files.Directory);
    }

    private static async Task KeepConcurrent(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "input.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var engine = KeepEngine(bytes, "mp4", true);
        var request = new NativeDownloadRequest { Url = new("https://media.example/source.mp4"), OutputDirectory = files.Directory, OutputTemplate = "%(ext)s/%(title)s.%(ext)s",
            KeepOriginal = true, AudioOnly = true, FfmpegPath = ffmpeg, Retries = 0 };
        var results = await Task.WhenAll(engine.DownloadAsync(request with { AudioFormat = "mp3" }, null, null, default), engine.DownloadAsync(request with { AudioFormat = "m4a" }, null, null, default));
        if (results.Any(result => !result.Succeeded || result.OutputPaths.Count != 2) || !File.ReadAllBytes(Path.Combine(files.Directory, "mp4", "Keep fixture.mp4")).AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("Concurrent jobs sharing a retained original did not publish complete outputs.");
        foreach (var result in results) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
        AssertNoPublicationFiles(files.Directory);
    }
    private static async Task KeepArchiveFailure(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "input.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(archive, "fixture prior\n");
        var engine = new NativeEngine([new KeepProvider("mp4", true, false)], _ => new MediaHttp(new Handler(_ =>
        {
            File.WriteAllBytes(archive, [0xff]);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        })));
        var callbacks = new List<string>();
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/source.mp4"), OutputDirectory = files.Directory, FileName = "archive-failure",
            KeepOriginal = true, AudioOnly = true, AudioFormat = "mp3", FfmpegPath = ffmpeg, DownloadArchiveFile = archive, Retries = 0 }, null, null, default, callbacks.Add);
        if (result.Succeeded || result.OutputPaths.Count != 2 || result.NewOutputPaths?.Count != 2 || callbacks.Count != 2 || result.OutputPaths.Any(path => !File.Exists(path)) ||
            !File.ReadAllBytes(archive).AsSpan().SequenceEqual(new byte[] { 0xff }))
            throw new InvalidOperationException("An archive failure lost or misreported the already published media set.");
        AssertNoPublicationFiles(files.Directory);
    }
    private static void AssertNoPublicationFiles(string directory)
    {
        if (Directory.EnumerateFiles(directory, ".native-save-*", SearchOption.AllDirectories).Any() || Directory.EnumerateFiles(directory, ".native-backup-*", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Sibling staging or backup files remained after publication/rollback.");
    }
    private static async Task VerifyKeptComponents(string ffmpeg, NativeDownloadResult result, string directory)
    {
        if (result.OutputPaths.Count != 3 || result.NewOutputPaths?.Count != 3) throw new InvalidOperationException("An adaptive transport did not retain both complete components.");
        foreach (var role in new[] { "video", "audio" })
        {
            var path = result.OutputPaths.Single(path => Path.GetFileNameWithoutExtension(path).EndsWith("." + role));
            var streams = await AudioStreams(ffmpeg, path);
            if (streams.Length != 1 || streams[0].GetProperty("codec_type").GetString() != role) throw new InvalidOperationException("A retained component contains the wrong tracks.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-f", "null", "-"]);
        }
        AssertNoPublicationFiles(directory);
    }
    private static NativeEngine KeepEngine(byte[] primary, string extension, bool audioOnly, byte[]? audio = null) => new([new KeepProvider(extension, audioOnly, audio is not null)], _ => new MediaHttp(new Handler(request =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/audio.m4a" ? audio! : primary) })));
    private sealed class KeepProvider(string extension, bool audioOnly, bool pair) : IMediaProvider
    {
        public MediaProviderInfo Info => new("keep-fixture", "Keep fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        {
            Id = "keep-id", Title = "Keep fixture", Extractor = "fixture", Media = pair ?
                [new() { Id = "v", Url = new("https://media.example/video.mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = false },
                 new() { Id = "a", Url = new("https://media.example/audio.m4a"), Kind = MediaKind.Progressive, Extension = "m4a", HasVideo = false, HasAudio = true }] :
                [new() { Id = "source", Url = request.Url, Kind = MediaKind.Progressive, Extension = extension, HasVideo = !audioOnly, HasAudio = true }]
        });
    }
}
