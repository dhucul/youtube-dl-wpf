using System.Diagnostics;
using System.Text;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal const int MaximumSubtitleBytes = 2 * 1024 * 1024;

    internal static async Task<byte[]> ConvertSubtitleAsync(string ffmpeg, byte[] source, string sourceFormat,
        string targetFormat, string directory, CancellationToken ct)
    {
        var inputFormat = sourceFormat switch
        {
            "vtt" => "webvtt", "srt" => "srt", "ass" or "ssa" => "ass", "lrc" => "lrc",
            _ => throw new NotSupportedException("Native conversion is not available for subtitle format " + sourceFormat + ".")
        };
        var (outputFormat, encoder) = targetFormat switch
        {
            "vtt" => ("webvtt", "webvtt"), "srt" => ("srt", "srt"), "ass" => ("ass", "ass"), "lrc" => ("lrc", "text"),
            _ => throw new NotSupportedException("The requested subtitle conversion format is unavailable.")
        };
        if (source.Length > MaximumSubtitleBytes) throw new InvalidDataException("The source caption exceeds its size limit.");
        ct.ThrowIfCancellationRequested();
        var input = Path.Combine(directory, ".caption-source-" + Guid.NewGuid().ToString("N") + "." + sourceFormat);
        try
        {
            await File.WriteAllBytesAsync(input, source, ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var start = new ProcessStartInfo(ffmpeg)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            // Force a subtitle parser and local protocols: the downloaded text cannot
            // become a playlist that makes ffmpeg fetch additional network resources.
            foreach (var argument in new[] { "-nostdin", "-v", "error", "-xerror", "-protocol_whitelist", "file,pipe",
                "-f", inputFormat, "-i", input, "-map", "0:s:0", "-c:s", encoder, "-f", outputFormat, "pipe:1" })
                start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            timeout.Token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InvalidOperationException("Could not start ffmpeg for subtitle conversion.");
            var error = ReadTailAsync(process.StandardError);
            try
            {
                using var converted = new MemoryStream();
                var buffer = new byte[8192];
                while (true)
                {
                    var count = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (converted.Length + count > MaximumSubtitleBytes) throw new InvalidDataException("The converted caption exceeds its size limit.");
                    converted.Write(buffer, 0, count);
                }
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var diagnostic = await error.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException("Subtitle conversion failed: " + Trim(diagnostic));
                if (converted.Length == 0) throw new InvalidDataException("Subtitle conversion produced an empty document.");
                var bytes = converted.ToArray();
                SubtitleFormats.ValidateConverted(new UTF8Encoding(false, true).GetString(bytes), targetFormat);
                return bytes;
            }
            catch (Exception failure)
            {
                await ProcessLifetime.KillAndDrainAsync(process, null, error).ConfigureAwait(false);
                if (failure is OperationCanceledException && !ct.IsCancellationRequested && timeout.IsCancellationRequested)
                    throw new TimeoutException("Subtitle conversion exceeded its time limit.", failure);
                throw;
            }
        }
        finally { if (File.Exists(input)) File.Delete(input); }
    }
}
