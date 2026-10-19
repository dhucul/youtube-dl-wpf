using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task FileTimeMedia(string ffmpeg, string mode, bool noPart = false)
    {
        using var files = new Files(); var pair = mode == "pair"; var source = Path.Combine(files.Directory, pair ? "source.m4a" : "source.wav");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=610:sample_rate=44100:duration=2", "-c:a", pair ? "aac" : "pcm_s16le", source]);
        var audio = await File.ReadAllBytesAsync(source); var video = Array.Empty<byte>();
        if (pair)
        {
            var path = Path.Combine(files.Directory, "source.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=12:duration=2", "-an", "-c:v", "libx264", path]); video = await File.ReadAllBytesAsync(path);
        }
        var candidates = pair ? new MediaCandidate[] { new() { Id = "video", Url = new("https://time-media.example/video.mp4"), Extension = "mp4", HasVideo = true, HasAudio = false },
            new() { Id = "audio", Url = new("https://time-media.example/audio.m4a"), Extension = "m4a", HasVideo = false, HasAudio = true } } :
            [new() { Id = "audio", Url = new("https://time-media.example/audio.wav"), Extension = "wav", HasVideo = false, HasAudio = true }];
        var page = new PageMetadata { Id = "time", Extractor = "time-media", ArchiveIdentity = new("time-media", "time"), Title = "Timed media", Description = "Description", Media = candidates };
        var engine = new NativeEngine([new FileTimeProvider(page)], _ => new MediaHttp(new Handler(request =>
        {
            var movie = request.RequestUri!.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal); var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(movie ? video : audio) };
            response.Content.Headers.LastModified = movie ? FileTimeChecks.Modified.AddDays(1) : FileTimeChecks.Modified; return response;
        })));
        var request = new NativeDownloadRequest { Url = new("https://time-media.example/watch"), OutputDirectory = files.Directory, OutputTemplate = "timed.%(ext)s", FfmpegPath = ffmpeg,
            KeepOriginal = true, AddMetadata = true, AudioOnly = !pair, AudioFormat = "mp3", AudioQuality = "128K", NoMtime = mode == "disabled", NoPart = noPart, WriteDescription = mode == "failure", DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") };
        var callbacks = 0;
        if (mode == "failure")
        {
            var prior = Path.Combine(files.Directory, "timed.mp3"); await File.WriteAllTextAsync(prior, "prior complete"); var oldTime = FileTimeChecks.Modified.AddDays(3).UtcDateTime; File.SetLastWriteTimeUtc(prior, oldTime);
            Directory.CreateDirectory(Path.Combine(files.Directory, "timed.description")); await File.WriteAllTextAsync(request.DownloadArchiveFile, "other preserved\n");
            try { await engine.DownloadAsync(request, null, null, default, _ => callbacks++); throw new InvalidOperationException("Conflicting sidecar was published."); } catch (IOException) { }
            if (await File.ReadAllTextAsync(prior) != "prior complete" || File.GetLastWriteTimeUtc(prior) != oldTime || callbacks != 0 || await File.ReadAllTextAsync(request.DownloadArchiveFile) != "other preserved\n")
                throw new InvalidOperationException("Failed timestamp publication changed prior data or time."); return;
        }
        var before = DateTime.UtcNow.AddSeconds(-2); var result = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!result.Succeeded || result.OutputPaths.Count != (pair ? 4 : 2) || callbacks != result.OutputPaths.Count) throw new InvalidOperationException("Timed media publication did not complete every output.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-f", "null", "-"], true);
        if (pair)
        {
            var retainedVideo = result.OutputPaths.Single(path => File.ReadAllBytes(path).SequenceEqual(video)); var retainedAudio = result.OutputPaths.Single(path => File.ReadAllBytes(path).SequenceEqual(audio));
            if (File.GetLastWriteTimeUtc(retainedVideo) != FileTimeChecks.Modified.AddDays(1).UtcDateTime || File.GetLastWriteTimeUtc(retainedAudio) != FileTimeChecks.Modified.UtcDateTime)
                throw new InvalidOperationException("Retained components lost their distinct remote entity times.");
        }
        else if (request.NoMtime)
        {
            if (result.OutputPaths.Any(path => File.GetLastWriteTimeUtc(path) < before)) throw new InvalidOperationException("No-mtime preserved an old server time.");
        }
        else if (result.OutputPaths.Any(path => File.GetLastWriteTimeUtc(path) != FileTimeChecks.Modified.UtcDateTime)) throw new InvalidOperationException("Conversion/original publication lost the HTTP time.");
        var times = result.OutputPaths.ToDictionary(path => path, File.GetLastWriteTimeUtc); var repeat = await engine.DownloadAsync(request, null, null, default, _ => callbacks++);
        if (!repeat.Skipped || callbacks != result.OutputPaths.Count || times.Any(item => File.GetLastWriteTimeUtc(item.Key) != item.Value)) throw new InvalidOperationException("Archive repeat changed published timestamps.");
    }
    private sealed class FileTimeProvider(PageMetadata page) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("time-media", "Timestamp media fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => page.ArchiveIdentity;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
}
