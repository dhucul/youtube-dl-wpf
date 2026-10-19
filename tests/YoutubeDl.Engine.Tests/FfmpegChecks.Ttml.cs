using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task TtmlEmbedding(string ffmpeg, string target)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var engine = CaptionChecks.CreateEngine(await File.ReadAllBytesAsync(source), [], _ => new StringContent(TtmlChecks.Sample), [SubtitleFormatChecks.Track("en", "ttml")]);
        var result = await engine.DownloadAsync(CaptionChecks.Request(files.Directory) with
        { WriteSubs = true, EmbedSubs = true, ConvertSubs = target, FfmpegPath = ffmpeg }, null, null, default);
        var extension = target.Length == 0 ? "ttml" : target;
        var caption = Path.Combine(files.Directory, "result.en." + extension);
        if (!result.Succeeded || result.OutputPath is null || !File.Exists(caption)) throw new InvalidOperationException("TTML source/converted caption or embedded media was not published.");
        if (target.Length == 0 && await File.ReadAllTextAsync(caption) != TtmlChecks.Sample) throw new InvalidOperationException("Embedding TTML rewrote the requested original sidecar.");
        var extracted = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "srt", "pipe:1"]);
        AssertCaptionTimeline(extracted);
        if (!extracted.Contains("café") || !extracted.Contains("Final 日本語.") || !extracted.Contains("Second line.")) throw new InvalidOperationException("TTML embedding lost Unicode or multiline text.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,codec_name,nb_read_frames:stream_tags=language", "-of", "json", result.OutputPath]);
        using var json = JsonDocument.Parse(probe);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(item => item.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "30" ||
            streams.Single(item => item.GetProperty("codec_type").GetString() == "subtitle").GetProperty("tags").GetProperty("language").GetString() != "eng")
            throw new InvalidOperationException("TTML processing changed media frames or caption language.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v:0", "-f", "null", "-"]);
        if (Directory.EnumerateFiles(files.Directory, ".caption-*", SearchOption.AllDirectories).Any()) throw new InvalidOperationException("TTML embedding leaked temporary caption files.");
    }
}
