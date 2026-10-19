using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task LimitedOriginals(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=purple:s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var data = ImgurChecks.Data();
        var extra = data["media"]![2]!.DeepClone(); extra["id"] = "Video04"; extra["url"] = "https://i.imgur.com/Video04.mp4";
        ((System.Text.Json.Nodes.JsonArray)data["media"]!).Add(extra); data["image_count"] = 4;
        var calls = new List<Uri>(); var completed = new List<string>();
        var engine = ImgurChecks.Engine(calls, data, media: request =>
        {
            if (request.RequestUri!.AbsolutePath is not ("/Video02.mp4" or "/Video03.mp4")) throw new InvalidOperationException("The item limit fetched an excluded video.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var result = await engine.DownloadAsync(ImgurChecks.Request(files.Directory) with { PlaylistItems = "2:4", MatchTitle = "SELECTED", MaxDownloads = 2,
            KeepOriginal = true, AddMetadata = true, FfmpegPath = ffmpeg, Container = "mp4", DownloadArchiveFile = archive }, null, null, default, completed.Add);
        if (!result.Succeeded || !result.DownloadLimitReached || completed.Count != 4 || result.OutputPaths.Count != 4 || calls.Count(url => url.Host == "i.imgur.com") != 2)
            throw new InvalidOperationException("The limit counted retained originals instead of completed media items.");
        if (result.OutputPaths.Count(path => File.ReadAllBytes(path).SequenceEqual(bytes)) != 2) throw new InvalidOperationException("Limited playback lost exact source originals.");
        var expected = await Frames(source);
        foreach (var output in result.OutputPaths)
        {
            if (await Frames(output) != expected) throw new InvalidOperationException("Limited playback lost audio/video frames.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);
        }
        if (!(await File.ReadAllLinesAsync(archive)).SequenceEqual(["imgur Video02", "imgur Video03"])) throw new InvalidOperationException("The limited job archived unprocessed items.");
        async Task<string> Frames(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return string.Join(';', json.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()));
        }
    }
}
