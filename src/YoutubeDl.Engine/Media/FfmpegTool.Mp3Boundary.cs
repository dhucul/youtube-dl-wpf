using System.Diagnostics;
using System.Text.Json;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task PreserveMp3BoundaryAsync(string ffmpeg, string input, string output, List<string> arguments, CancellationToken ct)
    {
        if (!Path.GetExtension(input).Equals(".mp3", StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(output).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return;
        var start = new ProcessStartInfo(RequireProbe(ffmpeg, "preserve copied MP3 boundary samples"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "-v", "error", "-max_alloc", "16777216", "-protocol_whitelist", "file,pipe", "-f", "mp3", "-select_streams", "a:0", "-read_intervals", "%+#1", "-show_packets", "-show_entries", "packet=side_data_list", "-of", "json", input }) start.ArgumentList.Add(value);
        using var process = new Process { StartInfo = start }; ct.ThrowIfCancellationRequested(); if (!process.Start()) throw new InvalidOperationException("Could not inspect the copied MP3 boundary.");
        var stdout = ReadProbeAsync(process.StandardOutput); var stderr = ReadTailAsync(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await ProcessLifetime.KillAndDrainAsync(process, null, stdout, stderr).ConfigureAwait(false); ct.ThrowIfCancellationRequested(); throw new TimeoutException("MP3 boundary inspection timed out.");
        }
        var json = await stdout.ConfigureAwait(false); var error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0 || error.Length != 0) throw new InvalidDataException("The MP3 boundary could not be inspected before copying.");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("packets", out var packets) || packets.ValueKind != JsonValueKind.Array || packets.GetArrayLength() != 1) throw new InvalidDataException("The MP3 source omitted its initial audio packet.");
        var packet = packets[0]; var gapless = packet.TryGetProperty("side_data_list", out var sideData) && sideData.EnumerateArray().Any(item => item.TryGetProperty("side_data_type", out var type) && type.GetString() == "Skip Samples");
        // The MP3 muxer otherwise invents an encoder-delay trim when copying
        // streams with no source skip-sample metadata. Retain existing gapless
        // metadata normally; do not introduce a Xing header for this case.
        if (!gapless) arguments.AddRange(["-write_xing", "0"]);
    }
}
