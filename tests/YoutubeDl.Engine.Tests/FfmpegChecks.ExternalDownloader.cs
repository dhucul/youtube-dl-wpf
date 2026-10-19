using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static readonly string[] ExternalDownloaderModes = ["progressive", "renewal", "merge", "audio", "sequence", "interrupted-publication"];
    internal static IEnumerable<(string Name, Func<Task> Test)> ExternalDownloaderMediaChecks(string ffmpeg) => ExternalDownloaderModes.Select(mode => ("External downloader media " + mode, (Func<Task>)(() => ExternalDownloaderMedia(ffmpeg, mode))));
    internal static async Task ExternalDownloaderMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null);
        var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        var video = Path.Combine(files.Directory, "video.mp4"); var audio = Path.Combine(files.Directory, "audio.m4a");
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v:0", "-c", "copy", video]);
        await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:a:0", "-c", "copy", audio]);
        var bytes = new Dictionary<string, byte[]> { ["/source.mp4"] = await File.ReadAllBytesAsync(source), ["/video.mp4"] = await File.ReadAllBytesAsync(video), ["/audio.m4a"] = await File.ReadAllBytesAsync(audio) };
        var grants = 0;
        await using var server = new ExternalDownloaderChecks.Server((request, stream, ct) =>
        {
            if (request.Target == "/old") { grants++; return ExternalDownloaderChecks.Reply(stream, [], ct, 403); }
            var data = bytes[request.Target]; return ExternalDownloaderChecks.Reply(stream, data, ct, 200, "Content-Type: video/mp4\r\nETag: \"media\"\r\n");
        });
        var authorization = mode == "renewal" ? new CurlMediaLease(server.Url) : null;
        var engine = new NativeEngine([new CurlMediaProvider(server.Url, mode, bytes, authorization)]);
        var request = new NativeDownloadRequest { Url = new("https://fixture.example/selected"), OutputDirectory = Path.Combine(files.Directory, "output"), FileName = "recording", ExternalDownloader = "curl", ExternalDownloaderArguments = ["curl:--max-time 30 --no-buffer"], FfmpegPath = ffmpeg, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), Retries = 0, KeepOriginal = true, NoMtime = true, FormatSelector = mode == "merge" ? "v+a" : "", AudioOnly = mode == "audio", AudioFormat = "flac" };
        var callbacks = new List<string>();
        if (mode == "interrupted-publication")
        {
            PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned publication interruption") : Task.CompletedTask;
            try { await ExternalDownloaderChecks.Expect<IOException>(() => engine.DownloadAsync(request, null, null, default, callbacks.Add)); }
            finally { PublicationFaults.Observer = null; }
            ExternalDownloaderChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Interrupted processing announced completion.");
            request = request with { ExternalDownloader = "native", ExternalDownloaderArguments = null };
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        ExternalDownloaderChecks.Assert(result.Succeeded && result.OutputPath is not null && callbacks.Count > 0, "A delegated media job did not publish successfully.");
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
        if (mode is "progressive" or "renewal" or "interrupted-publication") ExternalDownloaderChecks.Assert(SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath!)).SequenceEqual(SHA256.HashData(bytes["/source.mp4"])), "Delegated progressive bytes changed.");
        if (mode == "merge")
        {
            ExternalDownloaderChecks.Assert((await AdaptivePacketHashes(ffmpeg, video, "v:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, result.OutputPath!, "v:0")), "Delegated video packets changed during merging.");
            ExternalDownloaderChecks.Assert((await AdaptivePacketHashes(ffmpeg, audio, "a:0")).AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, result.OutputPath!, "a:0")), "Delegated audio packets changed during merging.");
        }
        if (mode == "renewal") ExternalDownloaderChecks.Assert(grants == 1 && authorization!.Renewals == 1, "The media job lost its protected playback renewal.");
        var count = server.Requests.Count;
        var skipped = await engine.DownloadAsync(request, null, null, default);
        ExternalDownloaderChecks.Assert(skipped.Succeeded && skipped.Skipped && server.Requests.Count == count, "An archived delegated media job transferred again.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, result.OutputPath, result.OutputPaths, result.NewOutputPaths, Requests = server.Requests.ToArray(), Source = source, Video = video, Audio = audio, Grants = grants }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class CurlMediaLease(Uri url) : IPlaybackAuthorization
    {
        internal int Renewals;
        public bool AppliesTo(Uri value) => value.Host == url.Host;
        public Task<PlaybackLease> AuthorizeAsync(Uri value, CancellationToken ct) => Task.FromResult(new PlaybackLease(new Uri(url, Renewals == 0 ? "/old" : "/source.mp4"), Renewals));
        public Task<PlaybackLease> RenewAsync(Uri value, long generation, CancellationToken ct) { Renewals++; return AuthorizeAsync(value, ct); }
        public Uri Canonicalize(Uri value) => new(url, "/source.mp4");
    }
    private sealed class CurlMediaProvider(Uri url, string mode, IReadOnlyDictionary<string, byte[]> bytes, IPlaybackAuthorization? authorization) : IMediaProvider
    {
        public MediaProviderInfo Info => new("curl-media-fixture", "Owned delegated media fixture", 999);
        public bool CanHandle(Uri value) => true;
        public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("curl-media-fixture", "selected");
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            MediaCandidate Candidate(string path, string id, bool hasVideo, bool hasAudio) => new() { Url = new(url, path), Id = id, Kind = MediaKind.Progressive, Extension = hasVideo ? "mp4" : "m4a", HasVideo = hasVideo, HasAudio = hasAudio, VerificationDemuxer = "mov", VerificationDurationSeconds = 3, VerificationFileSize = bytes[path].Length, Height = hasVideo ? 72 : null };
            var media = mode == "merge" ? new[] { Candidate("/video.mp4", "v", true, false), Candidate("/audio.m4a", "a", false, true) } : mode == "sequence" ?
                new[] { new MediaCandidate { Url = new(url, "/source.mp4"), Kind = MediaKind.HttpSequence, Extension = "mp4", HasVideo = true, HasAudio = true, Parts = [new(new(url, "/source.mp4"), "mp4", 3, bytes["/source.mp4"].Length), new(new(url, "/source.mp4"), "mp4", 3, bytes["/source.mp4"].Length)] } } : new[] { Candidate("/source.mp4", "combined", true, true) };
            return Task.FromResult(new PageMetadata { Id = "selected", Extractor = "curl-media-fixture", Title = "selected", DurationSeconds = mode == "sequence" ? 6 : 3, Media = media, PlaybackAuthorization = authorization });
        }
    }
}
