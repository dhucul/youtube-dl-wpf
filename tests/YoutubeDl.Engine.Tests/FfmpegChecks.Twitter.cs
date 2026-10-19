using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Twitter;

internal static partial class FfmpegChecks
{
    private static async Task TwitterMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2" };
        if (mode != "gif") args.AddRange(["-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "12"]); if (mode != "gif") args.AddRange(["-c:a", "aac"]); args.Add(source); await Run(ffmpeg, args);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "media.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var data = TwitterChecks.Data(); if (mode == "gif") data["mediaDetails"] = new JsonArray(TwitterChecks.Video(gif: true));
        if (mode == "multi") data["mediaDetails"] = new JsonArray(new JsonObject { ["type"] = "photo" }, TwitterChecks.Video(), TwitterChecks.Video(TwitterChecks.Other));
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var prefix = "https://video.twimg.com/amplify_video/" + TwitterChecks.Asset + "/pl/";
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24\n" + prefix + "media.m3u8" + TwitterChecks.Query + "\n";
        var playlist = string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"))).Select(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line) ? line : prefix + line + TwitterChecks.Query)) + "\n";
        var engine = new NativeEngine([new TwitterProvider()], _ => TwitterChecks.Http(data, calls, request =>
        {
            var url = request.RequestUri!; var name = Path.GetFileName(url.AbsolutePath);
            if (url.Host == "video.twimg.com" && url.Query != TwitterChecks.Query) throw new InvalidOperationException("Twitter media access queries changed.");
            return new(HttpStatusCode.OK) { Content = name == "master.m3u8" ? new StringContent(master) : name == "media.m3u8" ? new StringContent(playlist) :
                new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }));
        var request = TwitterChecks.Request(files.Directory) with { FormatId = mode == "hls" ? "hls" : "mp4-96-1", PlaylistItems = mode == "multi" ? "3" : "",
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "gif") request = request with { FormatId = "mp4-96-0" };
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        var primary = result.OutputPaths.FirstOrDefault(path => !path.Contains(".original."));
        if (!result.Succeeded || primary is null || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Twitter media/original publication was incomplete.");
        using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", primary], rejectDiagnostics: true));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48" ||
            streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio") != (mode != "gif")) throw new InvalidOperationException("Twitter processing lost frames or changed silent video roles.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        if (mode != "hls" && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(assets["source.mp4"]))) throw new InvalidOperationException("The original Twitter MP4 bytes changed.");
        var expected = mode == "multi" ? "twitter:media " + TwitterChecks.Id + "-3" : "twitter " + TwitterChecks.Id;
        if ((await File.ReadAllTextAsync(archive)).Trim() != expected) throw new InvalidOperationException("Twitter media archive ownership changed.");
        if (mode == "multi" && calls.Where(url => url.Host == "video.twimg.com").Any(url => !url.AbsolutePath.Contains(TwitterChecks.Other))) throw new InvalidOperationException("An unselected post video was transferred.");
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(url => url.Host == "video.twimg.com")) throw new InvalidOperationException("Twitter archive repeat emitted media or transferred video.");
    }
}
