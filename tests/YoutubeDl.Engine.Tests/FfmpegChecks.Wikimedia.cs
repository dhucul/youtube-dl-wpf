using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class FfmpegChecks
{
    private static async Task WikimediaMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var audio = mode == "audio"; var silent = mode == "silent"; var metadataOnly = mode == "captions";
        var source = Path.Combine(files.Directory, audio ? "source.ogg" : "source.webm");
        if (!metadataOnly)
        {
            var args = new List<string> { "-nostdin", "-v", "error", "-y" };
            if (!audio) args.AddRange(["-f", "lavfi", "-i", "color=c=blue:s=426x240:r=24:d=2"]);
            if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=400:sample_rate=48000:duration=2"]);
            if (!audio) args.AddRange(["-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8"]);
            if (!silent) args.AddRange(["-c:a", audio ? "libvorbis" : "libopus"]);
            args.Add(source); await Run(ffmpeg, args.ToArray());
        }
        var bytes = metadataOnly ? [] : await File.ReadAllBytesAsync(source); var data = WikimediaChecks.Data(silent); var info = WikimediaChecks.Info(data);
        if (audio)
        {
            data = JsonNode.Parse(data.ToJsonString().Replace("Fixture.webm", "Fixture.ogg"))!; info = WikimediaChecks.Info(data);
            info["mediatype"] = "AUDIO"; info["mime"] = "application/ogg"; info["size"] = bytes.Length; info["width"] = 0; info["height"] = 0;
            info["derivatives"] = new JsonArray(new JsonObject { ["src"] = info["url"]!.DeepClone(), ["type"] = "audio/ogg; codecs=\"vorbis\"" });
            info["timedtext"] = new JsonArray(); info["thumburl"] = "https://commons.wikimedia.org/w/resources/assets/file-type-icons/fileicon-ogg.png"; info.AsObject().Remove("responsiveUrls");
        }
        const string caption = "WEBVTT\n\n00:00:00.100 --> 00:00:01.800\nWikimedia caption.\n";
        var calls = new List<Uri>(); var done = new List<string>();
        var engine = WikimediaChecks.Engine(calls, data, media: message =>
        {
            if (message.RequestUri!.Host == "commons.wikimedia.org" && message.RequestUri.Query.Contains("action=timedtext")) return new(HttpStatusCode.OK) { Content = new StringContent(caption) };
            var expected = audio ? WikimediaChecks.Original.Replace("Fixture.webm", "Fixture.ogg") : WikimediaChecks.Derived;
            if (metadataOnly || message.RequestUri.AbsoluteUri != expected) throw new InvalidOperationException("Wikimedia fetched an unselected or incomplete media resource.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = WikimediaChecks.Request(files.Directory) with { Url = new("https://commons.wikimedia.org/wiki/File:Fixture." + (audio ? "ogg" : "webm")), FfmpegPath = ffmpeg,
            MaximumHeight = audio ? null : silent ? 240 : 360, Container = audio ? "" : "mkv", AddMetadata = !metadataOnly, KeepOriginal = !metadataOnly,
            SkipDownload = metadataOnly, WriteSubs = !audio, EmbedSubs = !audio && !metadataOnly, ConvertSubs = metadataOnly ? "srt" : "", SubLanguages = "en", DownloadArchiveFile = metadataOnly ? "" : archive };
        var result = await engine.DownloadAsync(request, null, null, default, done.Add);
        if (metadataOnly)
        {
            if (!result.Succeeded || result.SidecarPaths.Count != 1 || !result.SidecarPaths[0].EndsWith(".srt") || !File.ReadAllText(result.SidecarPaths[0]).Contains("Wikimedia caption.") || calls.Count != 2 || calls.Any(url => url.Host == "upload.wikimedia.org"))
                throw new InvalidOperationException("Standalone Wikimedia captions requested video or lost content.");
            return;
        }
        if (!result.Succeeded || result.OutputPath is null || result.OutputPaths.Count != 2 || done.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))
            throw new InvalidOperationException("Wikimedia media or exact originals were not retained.");
        if (await Frames(source) != await Frames(result.OutputPath)) throw new InvalidOperationException("Wikimedia processing lost source media frames.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
        if (!audio)
        {
            var subtitles = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-map", "0:s:0", "-f", "webvtt", "-"]);
            if (!subtitles.Contains("Wikimedia caption.") || !Directory.EnumerateFiles(files.Directory, "*.vtt").Any(path => File.ReadAllText(path) == caption)) throw new InvalidOperationException("Wikimedia captions were not saved and embedded.");
        }
        if ((await File.ReadAllTextAsync(archive)).Trim() != "wikimedia:page commons.wikimedia.org/42") throw new InvalidOperationException("Wikimedia repository/page archives changed.");
        async Task<string> Frames(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return string.Join(';', json.RootElement.GetProperty("streams").EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() is "video" or "audio")
                .Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()));
        }
    }
}
