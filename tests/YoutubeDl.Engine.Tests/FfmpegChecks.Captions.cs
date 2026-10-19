using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task DashCaptionMedia(string ffmpeg)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=3.5", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var caption = DashCaptionChecks.Movie([DashCaptionChecks.Sample("Native MP4 WebVTT text."), DashCaptionChecks.Sample("Native MP4 WebVTT text."), DashCaptionChecks.Sample("Final text.")], out var offset);
        var engine = DashCaptionChecks.Engine(DashCaptionChecks.Xml(offset), caption, video: await File.ReadAllBytesAsync(source));
        var result = await engine.DownloadAsync(new YoutubeDl.Engine.NativeDownloadRequest { SubtitleSleepSeconds = PublicSubtitleFixtureDelay.Value,  Url = new Uri("https://media.example/source.mpd"), OutputDirectory = files.Directory,
            FileName = "dash-caption-result", WriteSubs = true, EmbedSubs = true, SubLanguages = "en", FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException("DASH caption embedding failed.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_name,codec_type:stream_tags=language", "-of", "json", result.OutputPath]);
        using var json = JsonDocument.Parse(probe);
        var track = json.RootElement.GetProperty("streams").EnumerateArray().Single(item => item.GetProperty("codec_type").GetString() == "subtitle");
        if (track.GetProperty("codec_name").GetString() != "mov_text" || track.GetProperty("tags").GetProperty("language").GetString() != "eng") throw new InvalidOperationException("DASH caption language or codec was lost.");
        var extracted = Path.Combine(files.Directory, "dash-caption-extracted.vtt");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath, "-map", "0:s:0", extracted]);
        var text = await File.ReadAllTextAsync(extracted);
        if (!text.Contains("Native MP4 WebVTT text.") || !text.Contains("00:00.000 --> 00:02.000")) throw new InvalidOperationException("DASH caption text or merged timing changed during embedding.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
    }

    private static async Task CaptionMedia(string ffmpeg, string container)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var result = await CaptionChecks.CreateEngine(await File.ReadAllBytesAsync(source), []).DownloadAsync(CaptionChecks.Request(files.Directory) with
        { EmbedSubs = true, WriteSubs = container == "mp4", SubLanguages = "en,fr", Container = container, FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-show_entries", "stream=codec_name,codec_type:stream_tags=language", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var captions = document.RootElement.GetProperty("streams").EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() == "subtitle").ToArray();
        if (captions.Length != 2 || !captions.Select(stream => stream.GetProperty("tags").GetProperty("language").GetString()).SequenceEqual(["eng", "fra"]))
            throw new InvalidOperationException("Embedded caption languages were lost.");
        if (captions.Any(stream => stream.GetProperty("codec_name").GetString() != (container == "mp4" ? "mov_text" : "subrip")))
            throw new InvalidOperationException("The caption codec does not match the output container.");
        var extracted = Path.Combine(files.Directory, "extracted.srt");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath, "-map", "0:s:0", extracted]);
        if (!(await File.ReadAllTextAsync(extracted)).Contains("Native caption & punctuation.")) throw new InvalidOperationException("Embedded caption text was changed.");
        if (Directory.EnumerateFiles(files.Directory).Any(path => Path.GetFileName(path).StartsWith(".caption-"))) throw new InvalidOperationException("Temporary captions were not removed.");
        if (File.Exists(Path.Combine(files.Directory, "result.en.vtt")) != (container == "mp4")) throw new InvalidOperationException("Caption saving option ignored.");
    }
}
