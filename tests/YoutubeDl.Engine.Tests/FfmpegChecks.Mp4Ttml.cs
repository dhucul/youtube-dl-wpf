using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class FfmpegChecks
{
    private static async Task Mp4TtmlMedia(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=4", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var captions = Mp4TtmlChecks.Samples(out var index);
        var result = await DashCaptionChecks.Engine(DashCaptionChecks.Xml(index, "stpp.ttml.im1t"), captions, video: await File.ReadAllBytesAsync(source))
            .DownloadAsync(new YoutubeDl.Engine.NativeDownloadRequest { Url = new("https://media.example/source.mpd"), OutputDirectory = files.Directory,
                FileName = "stpp", WriteSubs = true, EmbedSubs = true, SubLanguages = "en", ConcurrentFragments = 3, FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException("MP4 TTML native media output failed.");
        var extracted = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "webvtt", "pipe:1"]);
        var timestamps = Regex.Matches(extracted, @"\d{2}:\d{2}\.\d{3} --> \d{2}:\d{2}\.\d{3}").Select(match => match.Value);
        if (!timestamps.SequenceEqual(["00:00.000 --> 00:01.500", "00:03.000 --> 00:03.500"]) || !extracted.Contains("First TTML cue.") || !extracted.Contains("Last TTML cue."))
            throw new InvalidOperationException("Embedded MP4 TTML lost its sample-clipped cue boundaries or text.");
        var saved = await File.ReadAllTextAsync(Path.Combine(files.Directory, "stpp.en.vtt"));
        if (!saved.Contains("00:00:00.000 --> 00:00:01.500") || !saved.Contains("00:00:03.000 --> 00:00:03.500")) throw new InvalidOperationException("Saved MP4 TTML captions have a different timeline.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames:stream_tags=language", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(item => item.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "40" ||
            streams.Single(item => item.GetProperty("codec_type").GetString() == "subtitle").GetProperty("tags").GetProperty("language").GetString() != "eng")
            throw new InvalidOperationException("MP4 TTML processing changed media frames or caption language.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v:0", "-f", "null", "-"]);
    }
}
