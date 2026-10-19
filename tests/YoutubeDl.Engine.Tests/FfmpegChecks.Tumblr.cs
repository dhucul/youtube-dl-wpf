using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Tumblr;

internal static partial class FfmpegChecks
{
    private static async Task TumblrMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var audio = mode == "audio"; var extension = audio ? "mp3" : mode == "mov" ? "mov" : "mp4"; var source = Path.Combine(files.Directory, "source." + extension);
        var args = new List<string> { "-nostdin", "-v", "error", "-y" };
        if (!audio) args.AddRange(["-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2"]);
        args.AddRange(["-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2"]);
        if (!audio) args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]); args.AddRange(["-c:a", audio ? "libmp3lame" : "aac", source]); await Run(ffmpeg, args);
        var bytes = await File.ReadAllBytesAsync(source); var block = TumblrChecks.Video(duration: mode == "incomplete" ? 20 : 2, audio: audio);
        if (mode == "mov")
        {
            block["url"] = "https://64.media.tumblr.com/hash/revision/original.mov" + TumblrChecks.Query;
            block["media"]!["url"] = "https://64.media.tumblr.com/hash/revision/s96/scaled.mov" + TumblrChecks.Query; block["media"]!["media_key"] = "hash:revision";
            block["media"]!["width"] = 1920; block["media"]!["height"] = 1080;
        }
        var html = mode == "multi" ? TumblrChecks.Html(new JsonObject { ["type"] = "text", ["text"] = "caption" }, block, TumblrChecks.Video("other")) : TumblrChecks.Html(block);
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new TumblrProvider()], _ => TumblrChecks.Http(html, calls, request =>
        { if (request.RequestUri!.Query != TumblrChecks.Query) throw new InvalidOperationException("Tumblr literal resource queries changed."); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        var request = TumblrChecks.Request(files.Directory) with { FormatId = "native", MaximumHeight = mode == "mov" ? 96 : null, PlaylistItems = mode == "multi" ? "3" : "",
            FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        if (mode == "incomplete")
        {
            var previous = Path.Combine(files.Directory, "tumblr.mp4"); await File.WriteAllTextAsync(previous, "previous complete movie");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Tumblr file was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete movie") throw new InvalidOperationException("Failed Tumblr verification changed prior files or archive state.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Tumblr native files/originals were not atomically published.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (audio ? streams.Any(stream => stream.GetProperty("codec_type").GetString() == "video") : streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48")
                throw new InvalidOperationException("Tumblr native media lost frames or changed audio-only roles.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Tumblr native originals changed bytes.");
        var expected = mode == "multi" ? "tumblr:media " + TumblrChecks.Id + "-tumblr_other" : "tumblr " + TumblrChecks.Id;
        if ((await File.ReadAllTextAsync(archive)).Trim() != expected || calls.Any(url => url.AbsolutePath.Contains("suggested"))) throw new InvalidOperationException("Tumblr archive/media identity included a suggested post.");
        if (mode == "multi" && calls.Where(url => url.Host.EndsWith(".media.tumblr.com")).Any(url => !url.AbsolutePath.Contains("other"))) throw new InvalidOperationException("An unselected native media block was transferred.");
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(url => url.Host.EndsWith(".media.tumblr.com"))) throw new InvalidOperationException("Tumblr archive retry retransferred media or notified old files.");
    }
}
