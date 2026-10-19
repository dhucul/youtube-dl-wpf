using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

internal static class HttpSequenceDownloader
{
    internal static async Task DownloadAsync(MediaHttp http, MediaCandidate candidate, string destination, NativeDownloadRequest request,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        ValidateInventory(candidate);
        var parts = candidate.Parts;
        var directory = Path.GetFullPath(destination + ".sequence");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The ordered media workspace cannot be a link.");
        var paths = parts.Select((part, index) => Path.Combine(directory, "part-" + index.ToString("D6") + "." + part.Extension)).ToArray();
        foreach (var path in paths)
            foreach (var owned in new[] { path, path + ".part", path + ".part.json", path + ".native-resume.json" })
                if (File.Exists(owned) && (File.GetAttributes(owned) & FileAttributes.ReparsePoint) != 0) throw new IOException("An ordered media file cannot be a link.");
        var limiter = new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit));
        var files = new FileDownloader(http) { ExternalDownloader = request.ExternalDownloader, ExternalDownloaderArguments = request.ExternalDownloaderArguments, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NoMtime = request.NoMtime };
        var transferred = new long[parts.Count]; var gate = new object();
        var total = parts.All(part => part.FileSize is > 0) ? parts.Sum(part => (decimal)part.FileSize!.Value) : (decimal?)null;
        await OrderedFragmentTransfer.RunAsync(parts, request.ConcurrentFragments, async (part, index, token) =>
        {
            await files.DownloadAsync(part.Url, paths[index], limiter, progress is null ? null : new InlineProgress(value =>
            {
                lock (gate)
                {
                    transferred[index] = Math.Max(transferred[index], value.DownloadedBytes);
                    progress.Report(new() { DownloadedBytes = transferred.Sum(), TotalBytes = total is > 0 and <= long.MaxValue ? (long)total.Value : null });
                }
            }), token, request.FragmentRetries ?? request.Retries).ConfigureAwait(false);
            if (part.FileSize is { } expected && new FileInfo(paths[index]).Length != expected)
                throw new InvalidDataException("An ordered media file does not match its declared size.");
        }, async (part, index, token) =>
        {
            if (candidate.NormalizeMp3ClipBoundary)
            {
                if (request.RetainedMedia is { } source) await source.CaptureAsync(paths[index], "part", part.Extension, null, token).ConfigureAwait(false);
                await Mp3ClipBoundary.NormalizeAsync(paths[index], token).ConfigureAwait(false);
            }
            var descriptor = new MediaCandidate { Url = part.Url, Kind = MediaKind.Progressive, Extension = part.Extension, HasVideo = candidate.HasVideo,
                HasAudio = candidate.HasAudio, VerificationDemuxer = Demuxer(part.Extension), VerificationDurationSeconds = part.DurationSeconds };
            await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, paths[index], descriptor, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        var joined = Path.Combine(directory, "joined." + candidate.Extension);
        if (File.Exists(joined) && (File.GetAttributes(joined) & FileAttributes.ReparsePoint) != 0) throw new IOException("The joined media staging file cannot be a link.");
        try
        {
            await FfmpegTool.JoinMediaFilesAsync(request.FfmpegPath!, parts.Select((part, index) => (paths[index], part.DurationSeconds)).ToArray(), joined, ct).ConfigureAwait(false);
            await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, joined, new MediaCandidate { Url = candidate.Url, Kind = MediaKind.Progressive,
                HasVideo = candidate.HasVideo, HasAudio = candidate.HasAudio, VerificationDemuxer = Demuxer(candidate.Extension!),
                VerificationDurationSeconds = parts.Sum(part => part.DurationSeconds) }, ct).ConfigureAwait(false);
            if (!candidate.NormalizeMp3ClipBoundary && request.RetainedMedia is { } retained)
                for (var index = 0; index < paths.Length; index++)
                    await retained.CaptureAsync(paths[index], "part", parts[index].Extension, null, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(joined, destination, overwrite: true);
        }
        finally { if (File.Exists(joined)) File.Delete(joined); }
    }
    internal static void ValidateInventory(MediaCandidate candidate)
    {
        var parts=candidate.Parts;
        if(candidate.NormalizeMp3ClipBoundary&&(candidate.Extension!="mp3"||candidate.HasAudio!=true||candidate.HasVideo!=false))throw new InvalidDataException("MP3 clipping normalization requires declared audio-only MP3 parts.");
        if(parts.Count is <2 or >4096||candidate.Extension is not ("mp4" or "flv" or "mp3")||candidate.Extension=="mp3"&&(candidate.HasAudio!=true||candidate.HasVideo!=false)||parts.Any(part=>!part.Url.IsAbsoluteUri||part.Url.Scheme is not ("http" or "https")||part.Extension!=candidate.Extension||
            !double.IsFinite(part.DurationSeconds)||part.DurationSeconds<=0||part.FileSize is <=0))throw new InvalidDataException("The ordered media files have an invalid inventory or container.");
    }
    private static string Demuxer(string extension) => extension switch { "mp4" => "mov", "mp3" => "mp3", _ => "flv" };
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => report(value); }
}
