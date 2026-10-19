using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Mastodon;

internal static partial class FfmpegChecks
{
    private static async Task MastodonMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var audio = mode == "audio"; var silent = mode == "gifv"; var extension = audio ? "mp3" : "mp4";
        var source = Path.Combine(files.Directory, "source." + extension); var args = new List<string> { "-nostdin", "-v", "error", "-y" };
        if (!audio) args.AddRange(["-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2"]);
        if (!silent) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=650:sample_rate=48000:duration=2"]);
        if (!audio) args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (!silent) args.AddRange(["-c:a", audio ? "libmp3lame" : "aac"]); args.Add(source); await Run(ffmpeg, args);
        var bytes = await File.ReadAllBytesAsync(source); var type = audio ? "audio" : silent ? "gifv" : "video";
        var post = mode == "multi" ? MastodonChecks.Post(MastodonChecks.Attachment("image", "image"), MastodonChecks.Attachment(), MastodonChecks.Attachment("second")) :
            MastodonChecks.Post(MastodonChecks.Attachment(type: type, duration: mode == "incomplete" ? 20 : 2));
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new MastodonProvider()], _ => MastodonChecks.Http(post.ToJsonString(), calls, r =>
        { if (r.RequestUri!.Query != MastodonChecks.Query || r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie")) throw new InvalidOperationException("Mastodon media query changed or credentials crossed origins."); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        var request = MastodonChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            PlaylistItems = mode == "multi" ? "3" : "", HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture", ["Cookie"] = "fixture=secret" } };
        if (mode == "incomplete")
        {
            var prior = Path.Combine(files.Directory, "mastodon.mp4"); await File.WriteAllTextAsync(prior, "prior complete file");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Mastodon video was published."); } catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(prior) != "prior complete file") throw new InvalidOperationException("Failed verification changed existing output or archive."); return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 2 || callbacks.Count != 2) throw new InvalidOperationException("Mastodon processed/original files were not published once.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (audio ? streams.Any(s => s.GetProperty("codec_type").GetString() == "video") : streams.Single(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48")
                throw new InvalidOperationException("Mastodon media lost video frames or audio-only roles.");
            if (silent && streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new InvalidOperationException("Mastodon GIFV gained an audio stream.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"], rejectDiagnostics: true);
        }
        if (!result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))) throw new InvalidOperationException("Mastodon original bytes changed.");
        var expected = (mode == "multi" ? "mastodon:media " : "mastodon ") + "https://social.example/users/fixture/statuses/" + MastodonChecks.Id + (mode == "multi" ? "#second" : "");
        if ((await File.ReadAllTextAsync(archive)).Trim() != expected || mode == "multi" && calls.Any(u => u.Host == "cdn.example" && !u.AbsolutePath.Contains("second")))
            throw new InvalidOperationException("Selected Mastodon attachments lost their original position or archive identity.");
        if (mode == "multi")
        { using var sidecar = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json")))); if (sidecar.RootElement.GetProperty("playlist_index").GetInt32() != 3) throw new InvalidOperationException("Selected attachment position changed."); }
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(u => u.Host == "cdn.example")) throw new InvalidOperationException("Mastodon archive retry transferred old media or repeated callbacks.");
    }
}
