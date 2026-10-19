using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task ChapterMedia(string ffmpeg, string container)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var result = await ChapterChecks.CreateEngine(await File.ReadAllBytesAsync(source), ChapterChecks.Markers).DownloadAsync(ChapterChecks.Request(files.Directory) with
        { EmbedChapters = true, AddMetadata = true, Container = container, FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_chapters", "-show_entries", "format_tags=title,artist", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var chapters = document.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
        if (chapters.Length != 2 || chapters[0].GetProperty("tags").GetProperty("title").GetString() != ChapterChecks.Markers[0].Title || chapters[1].GetProperty("tags").GetProperty("title").GetString() != "Ending")
            throw new InvalidOperationException("Embedded chapter titles were changed or lost.");
        if (chapters[0].GetProperty("start_time").GetString() != "0.000000" || chapters[0].GetProperty("end_time").GetString() != "0.100000" ||
            chapters[1].GetProperty("end_time").GetString() != "0.300000") throw new InvalidOperationException("Chapter times were changed.");
        var tags = document.RootElement.GetProperty("format").GetProperty("tags");
        if (tags.GetProperty("title").GetString() != "Chapter fixture") throw new InvalidOperationException("Chapter titles changed the media title.");
        if (Directory.EnumerateFiles(files.Directory).Any(path => Path.GetFileName(path).StartsWith(".chapters-"))) throw new InvalidOperationException("Temporary chapter metadata was not removed.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
    }
}
