using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;

internal static partial class FfmpegChecks
{
    private static async Task SubtitleConversion(string ffmpeg, string target)
    {
        using var files = new Files(); var calls = new List<string>();
        var source = target == "vtt" ? "srt" : "vtt";
        var original = source == "srt" ? SubtitleFormatChecks.Srt : SubtitleFormatChecks.Vtt;
        var engine = CaptionChecks.CreateEngine("unused media"u8.ToArray(), calls, _ => new StringContent(original), [SubtitleFormatChecks.Track("en", source)]);
        var request = CaptionChecks.Request(files.Directory) with
        { WriteSubs = true, SkipDownload = true, ConvertSubs = target, SubFormat = source, FfmpegPath = ffmpeg };
        var result = await engine.DownloadAsync(request, null, null, default);
        var path = Path.Combine(files.Directory, "result.en." + target);
        if (!result.Succeeded || result.OutputPaths.Count != 0 || !result.SidecarPaths.SequenceEqual([path]) || !calls.SequenceEqual(["en." + source]))
            throw new InvalidOperationException("Subtitle conversion did not use standalone caption-only transfer and the final requested extension.");
        var text = await File.ReadAllTextAsync(path);
        if (!text.Contains("café") || !text.Contains("Final 日本語.") || !text.Contains("Second line."))
            throw new InvalidOperationException("Subtitle conversion changed Unicode text or lost a multiline cue.");
        if (target == "lrc")
        {
            if (!text.Contains("[00:00.12]") || !text.Contains("[00:01.23]")) throw new InvalidOperationException("LRC cue-start timestamps were not retained.");
        }
        else
        {
            var srt = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", path, "-map", "0:s:0", "-f", "srt", "pipe:1"]);
            AssertCaptionTimeline(srt);
        }
        if (Directory.EnumerateFiles(files.Directory, ".caption-*", SearchOption.AllDirectories).Any() || File.Exists(Path.Combine(files.Directory, "result.en." + source)))
            throw new InvalidOperationException("Subtitle conversion published an unrequested original or leaked a temporary document.");
        var timestamp = File.GetLastWriteTimeUtc(path);
        var repeated = await engine.DownloadAsync(request with { NoOverwrites = true }, null, null, default);
        if (!repeated.Skipped || repeated.NewSidecarPaths.Count != 0 || File.GetLastWriteTimeUtc(path) != timestamp || File.ReadAllText(path) != text)
            throw new InvalidOperationException("No-overwrite changed an existing converted subtitle.");
    }

    private static async Task ConvertedSubtitleEmbedding(string ffmpeg, string target)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", source]);
        var engine = CaptionChecks.CreateEngine(await File.ReadAllBytesAsync(source), [], _ => new StringContent(SubtitleFormatChecks.Vtt), [SubtitleFormatChecks.Track("en", "vtt")]);
        var result = await engine.DownloadAsync(CaptionChecks.Request(files.Directory) with
        { WriteSubs = true, EmbedSubs = true, ConvertSubs = target, FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded || result.OutputPath is null || !File.Exists(Path.Combine(files.Directory, "result.en." + target)))
            throw new InvalidOperationException("Converted subtitle sidecar or embedded media was not saved.");
        var srt = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "srt", "pipe:1"]);
        AssertCaptionTimeline(srt);
        if (!srt.Contains("café") || !srt.Contains("Final 日本語.")) throw new InvalidOperationException("Embedded converted captions lost Unicode text.");
        var probe = await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,codec_name,nb_read_frames:stream_tags=language", "-of", "json", result.OutputPath]);
        using var json = JsonDocument.Parse(probe);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(item => item.GetProperty("codec_type").GetString() == "video");
        var subtitle = streams.Single(item => item.GetProperty("codec_type").GetString() == "subtitle");
        if (video.GetProperty("nb_read_frames").GetString() != "30" || subtitle.GetProperty("codec_name").GetString() != "mov_text" ||
            subtitle.GetProperty("tags").GetProperty("language").GetString() != "eng") throw new InvalidOperationException("Subtitle conversion changed media frames, language or embedded codec.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v:0", "-f", "null", "-"]);
        if (Directory.EnumerateFiles(files.Directory, ".caption-*", SearchOption.AllDirectories).Any()) throw new InvalidOperationException("Embedding leaked a caption source file.");
    }

    private static async Task AssSubtitleConversion(string ffmpeg)
    {
        using var files = new Files(); var original = Path.Combine(files.Directory, "source.srt"); var ass = Path.Combine(files.Directory, "source.ass");
        await File.WriteAllTextAsync(original, SubtitleFormatChecks.Srt);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", original, ass]);
        var bytes = await File.ReadAllBytesAsync(ass);
        var result = await CaptionChecks.CreateEngine("unused"u8.ToArray(), [], _ => new ByteArrayContent(bytes), [SubtitleFormatChecks.Track("en", "ass")])
            .DownloadAsync(CaptionChecks.Request(files.Directory) with { SkipDownload = true, WriteSubs = true, ConvertSubs = "srt", FfmpegPath = ffmpeg }, null, null, default);
        if (!result.Succeeded) throw new InvalidOperationException("ASS to SRT conversion failed.");
        AssertCaptionTimeline(await File.ReadAllTextAsync(Path.Combine(files.Directory, "result.en.srt")));
    }

    private static void AssertCaptionTimeline(string srt)
    {
        var timestamps = Regex.Matches(srt, @"\d{2}:\d{2}:\d{2},\d{3} --> \d{2}:\d{2}:\d{2},\d{3}").Select(match => match.Value);
        if (!timestamps.SequenceEqual(["00:00:00,120 --> 00:00:00,870", "00:00:01,230 --> 00:00:02,340"]))
            throw new InvalidOperationException("Converted or embedded captions lost explicit cue starts, ends or gaps.");
    }
}
