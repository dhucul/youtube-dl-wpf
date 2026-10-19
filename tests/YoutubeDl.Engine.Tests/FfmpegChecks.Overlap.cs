using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task OverlapCaptions(string ffmpeg, bool srt)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); var output = Path.Combine(files.Directory, "output.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=24:d=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var text = srt ? "1\n00:00:00,000 --> 00:00:01,500\nAlpha\n\n2\n00:00:01,000 --> 00:00:02,000\nBeta\n" :
            "WEBVTT\n\nNOTE1\n00:00:00.000 --> 00:00:01.500\nAlpha\n\nsecond\n00:00:01.000 --> 00:00:02.000\nBeta\n";
        var subtitle = Path.Combine(files.Directory, "source." + (srt ? "srt" : "vtt")); await File.WriteAllTextAsync(subtitle, text);
        await FfmpegTool.EmbedAsync(ffmpeg, source, output, [new(subtitle, "en")], null, null, default);
        if (await File.ReadAllTextAsync(subtitle) != text || Directory.EnumerateFiles(files.Directory, ".caption-overlap-*").Any()) throw new InvalidOperationException("Embedding rewrote original captions or left temporary copies.");
        var rendered = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "webvtt", "-"]);
        var cues = Regex.Matches(rendered.Replace("\r\n", "\n"), @"(?m)^(?<start>[0-9:.]+) --> (?<end>[0-9:.]+)[^\n]*\n(?<text>[\s\S]*?)(?=\n\n|\z)")
            .Select(match => (Start: Seconds(match.Groups["start"].Value), End: Seconds(match.Groups["end"].Value), Text: match.Groups["text"].Value.Trim())).ToArray();
        if (!cues.SequenceEqual(new[] { (0m, 1m, "Alpha"), (1m, 1.5m, "Alpha\nBeta"), (1.5m, 2m, "Beta") })) throw new InvalidOperationException("MP4 embedding shortened or lost overlapping caption text.");
        using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "json", output]));
        if (probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString() != "48") throw new InvalidOperationException("Caption normalization changed video frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-f", "null", "-"]);
        static decimal Seconds(string value) { decimal time = 0; foreach (var part in value.Split(':')) time = time * 60 + decimal.Parse(part, CultureInfo.InvariantCulture); return time; }
    }
}
