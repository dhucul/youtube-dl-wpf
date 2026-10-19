using YoutubeDl.Engine.Discovery;
using System.Globalization;
using System.Security.Cryptography;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task VerifyDownloadedMediaAsync(string ffmpeg, string input, MediaCandidate candidate, CancellationToken ct)
    {
        if (candidate.VerificationDemuxer is null) return;
        if (candidate.VerificationFileSize is { } size && (size <= 0 || new FileInfo(input).Length != size))
            throw new InvalidDataException("The downloaded file size does not match the provider's original blob.");
        if (candidate.VerificationSha1 is { } digest)
        {
            if (digest.Length != 40 || !digest.All(char.IsAsciiHexDigit)) throw new InvalidDataException("The provider's original-object digest is invalid.");
            await using var stream = new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
            var actual = await SHA1.HashDataAsync(stream,ct).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(digest))) throw new InvalidDataException("The downloaded object does not match the provider's original-object digest.");
        }
        if (candidate.VerificationDemuxer is not ("mov" or "flv" or "matroska" or "mpegts" or "ogg" or "mp3" or "aac" or "wav" or "flac")) throw new NotSupportedException("The native provider requested an unsupported verification demuxer.");
        if (candidate.HasVideo != true && candidate.HasAudio != true) throw new InvalidDataException("Media verification requires an identified audio or video role.");
        var minimumTolerance = candidate.VerificationMinimumDurationToleranceSeconds ?? 1.1;
        if (!double.IsFinite(minimumTolerance) || minimumTolerance < 0) throw new InvalidDataException("The provider's decoded-duration tolerance is invalid.");
        var args = new List<string> { "-v", "error", "-nostats", "-xerror", "-progress", "pipe:1", "-abort_on", "empty_output+empty_output_stream",
            "-protocol_whitelist", "file,pipe", "-err_detect", "explode", "-f", candidate.VerificationDemuxer };
        if (candidate.VerificationDemuxer == "mov") args.AddRange(["-enable_drefs", "0", "-use_absolute_path", "0"]);
        args.AddRange(["-i", input]);
        if (candidate.HasVideo == true) args.AddRange(["-map", "0:v"]);
        if (candidate.HasAudio != false) args.AddRange(["-map", candidate.HasAudio == true ? "0:a" : "0:a?"]);
        if (candidate.HasVideo == true) args.AddRange(["-fps_mode:v", "passthrough", "-enc_time_base:v", "demux"]);
        args.AddRange(["-f", "null", "-"]);
        await RunAsync(ffmpeg, args, ct, rejectDiagnostics: true, inspectOutput: output =>
        {
            var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var lastTime = lines.LastOrDefault(line => line.StartsWith("out_time_us=", StringComparison.Ordinal));
            if (lines.LastOrDefault() != "progress=end" || lastTime is null ||
                !long.TryParse(lastTime[12..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var microseconds) || microseconds <= 0)
                throw new InvalidDataException("The media verifier did not complete a nonempty decoded timeline.");
            if (candidate.VerificationDurationSeconds is > 0 and var expected && Math.Abs(microseconds / 1_000_000d - expected) > 1.1)
                throw new InvalidDataException("The decoded media duration does not match the provider's complete playback duration.");
            if (candidate.VerificationMinimumDurationSeconds is > 0 and var minimum && microseconds / 1_000_000d + minimumTolerance < minimum)
                throw new InvalidDataException("The decoded audio is shorter than the provider's complete program duration.");
        }).ConfigureAwait(false);
    }
}
