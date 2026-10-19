using System.Text.Json;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static Task<int> RunConfiguredAsync(string ffmpeg, IReadOnlyList<string> arguments, CancellationToken ct, string[] stages, bool rejectDiagnostics = false) =>
        RunAsync(ffmpeg, NativeProcessingOptions.Apply(arguments, stages), ct, rejectDiagnostics: rejectDiagnostics);

    internal static async Task<bool> NeedsContainerFixupAsync(string ffmpeg, string input, CancellationToken ct) =>
        await FixupDemuxerAsync(ffmpeg, input, ct).ConfigureAwait(false) is not null;

    private static async Task<string?> FixupDemuxerAsync(string ffmpeg, string input, CancellationToken ct)
    {
        string[] format = [];
        await ProbeStreamsAsync(ffmpeg, input, ct, "detect the source container", localMediaOnly: true, inspectDocument: document =>
        {
            format = document.GetProperty("format").GetProperty("format_name").GetString()?.Split(',') ?? [];
        }).ConfigureAwait(false);
        return Path.GetExtension(input).ToLowerInvariant() switch
        {
            ".mp4" or ".m4a" or ".m4v" or ".mov" => format.Contains("aac") ? "aac" : format.Contains("mpegts") ? "ts" : null,
            ".mkv" or ".webm" => format.Contains("mpegts") ? "ts" : null,
            _ => null
        };
    }

    internal static async Task FixContainerAsync(string ffmpeg, string input, string output, CancellationToken ct)
    {
        var demuxer = await FixupDemuxerAsync(ffmpeg, input, ct).ConfigureAwait(false);
        string? alias = null;
        try
        {
            if (demuxer is not null)
            {
                // Existing ISO-BMFF metadata preservation uses the extension to
                // select its parser. Give a mislabeled source its actual type.
                alias = Path.Combine(Path.GetDirectoryName(input)!, ".fixup-" + Guid.NewGuid().ToString("N") + "." + demuxer);
                File.Copy(input, alias);
            }
            await RemuxAsync(ffmpeg, alias ?? input, output, ct, "Fixup").ConfigureAwait(false);
        }
        finally { if (alias is not null) File.Delete(alias); }
    }
}
