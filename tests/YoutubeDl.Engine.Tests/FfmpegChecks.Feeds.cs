using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task FeedMedia(string ffmpeg)
    {
        using var files = new Files();
        var video = Path.Combine(files.Directory, "source.mp4"); var cover = Path.Combine(files.Directory, "cover.png");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=64x48:d=0.04", "-frames:v", "1", "-update", "1", cover]);
        var videoBytes = await File.ReadAllBytesAsync(video); var imageBytes = await File.ReadAllBytesAsync(cover);
        var xml = FeedChecks.Rss.Replace("first.mp3", "first.mp4").Replace("type=\"audio/mpeg\"", "type=\"video/mp4\"");
        var engine = FeedChecks.Engine(uri => uri == FeedChecks.Url ? FeedChecks.Response(xml) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = uri.AbsolutePath.EndsWith(".mp4") ? new ByteArrayContent(videoBytes) :
                uri.AbsolutePath.EndsWith(".png") ? new ByteArrayContent(imageBytes) : new StringContent(CaptionChecks.Caption) }, []);
        var result = await engine.DownloadAsync(FeedChecks.Request(files.Directory) with { PlaylistItems = "1", EmbedSubs = true, WriteSubs = true, EmbedChapters = true,
            EmbedThumbnail = true, AddMetadata = true, WriteInfoJson = true, FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException("Native feed media did not complete.");
        using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"),
            ["-v", "error", "-count_frames", "-show_streams", "-show_chapters", "-of", "json", result.OutputPath]));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_name").GetString() == "h264").GetProperty("nb_read_frames").GetString() != "3" ||
            streams.Count(stream => stream.GetProperty("codec_name").GetString() == "mov_text") != 1 ||
            streams.Count(stream => stream.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 1) != 1)
            throw new InvalidOperationException("Feed processing lost media, timed transcript or artwork.");
        var chapters = probe.RootElement.GetProperty("chapters").EnumerateArray().ToArray();
        if (chapters.Length != 2 || chapters[1].GetProperty("start_time").GetString() != "0.100000" || chapters[1].GetProperty("end_time").GetString() != "0.300000")
            throw new InvalidOperationException("Feed chapter boundaries changed.");
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(result.OutputPath, ".info.json")));
        if (info.RootElement.GetProperty("id").GetString() != "episode-one" || File.ReadAllText(Path.ChangeExtension(result.OutputPath, ".en.vtt")) != CaptionChecks.Caption)
            throw new InvalidOperationException("Feed sidecars lost their episode identity or transcript.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:V", "-f", "null", "-"]);
    }
}
