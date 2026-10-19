using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task NfbMedia(string ffmpeg, bool described, bool shortManifest = false, bool series = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4"); var manifestPath = Path.Combine(files.Directory, "index.m3u8");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=" + (described ? "900" : "500") + ":sample_rate=48000:duration=2",
            "-c:v", "libx264", "-g", "24", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-hls_time", "1", "-hls_list_size", "0",
            "-hls_segment_filename", Path.Combine(files.Directory, "part-%02d.ts"), manifestPath]);
        var asset = "411214_213822_DM-9111" + (described ? "_aen" : ""); var prefix = "https://d3acx5b8mnvbua.cloudfront.net/hls/" + asset + "/";
        const string query = "?Policy=A%2Bb&Signature=C%2Fd&Key-Pair-Id=fixture";
        var parts = new Dictionary<string, byte[]>(); var lines = new List<string>(); using var combined = new MemoryStream();
        foreach (var line in await File.ReadAllLinesAsync(manifestPath))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) { lines.Add(line); continue; }
            var bytes = await File.ReadAllBytesAsync(Path.Combine(files.Directory, line)); parts.Add(line, bytes); combined.Write(bytes); lines.Add(prefix + line + query);
        }
        var served = Path.Combine(files.Directory, "served.ts"); await File.WriteAllBytesAsync(served, combined.ToArray());
        var origin = await FfmpegTool.ReadStartTimeAsync(ffmpeg, served, default);
        var caption = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:" + decimal.ToInt64(decimal.Round(origin * 90000)) + "\n\n00:00:00.000 --> 00:00:01.000\nNFB caption.\n";
        var master = "#EXTM3U\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"English\",LANGUAGE=\"eng\",DEFAULT=YES,URI=\"" + prefix + "captions.m3u8" + query + "\"\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=128x96,FRAME-RATE=24,SUBTITLES=\"subs\"\n" + prefix + "index.m3u8" + query + "\n" +
            "#EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=10000,URI=\"" + prefix + "trick.m3u8" + query + "\"\n#EXT-X-IMAGE-STREAM-INF:URI=\"" + prefix + "images.m3u8" + query + "\"\n";
        var calls = new List<Uri>(); var completed = new List<string>();
        HttpResponseMessage Media(HttpRequestMessage request)
        {
            var url = request.RequestUri!;
            if (url.AbsolutePath.Contains("_aen") != described || url.Query != query) throw new InvalidOperationException("NFB changed the selected audio variant or its literal access parameters.");
            var name = Path.GetFileName(url.AbsolutePath); HttpContent content = name switch
            {
                var value when value == asset + ".m3u8" => new StringContent(master),
                "index.m3u8" => new StringContent(string.Join('\n', lines.Select(line => shortManifest && line.StartsWith("#EXTINF:") ? "#EXTINF:20," : line))),
                "captions.m3u8" => new StringContent("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\n" + prefix + "captions.vtt" + query + "\n#EXT-X-ENDLIST\n"),
                "captions.vtt" => new StringContent(caption),
                _ when parts.ContainsKey(name) => new ByteArrayContent(parts[name]),
                _ => throw new InvalidOperationException("A preview or unexpected NFB resource was requested.")
            };
            return new(HttpStatusCode.OK) { Content = content };
        }
        var engine = series ? NfbSeriesChecks.Engine(calls, media: Media) : NfbChecks.Engine(calls, playerHtml: NfbChecks.Player(duration: 100), media: Media);
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = (series ? NfbSeriesChecks.Request(files.Directory) with { PlaylistItems = "2" } : NfbChecks.Request(files.Directory)) with { FfmpegPath = ffmpeg, FormatSelector = described ? "hls-described" : "", Container = "mp4", MaximumHeight = 96,
            AddMetadata = true, KeepOriginal = true, WriteSubs = !shortManifest, EmbedSubs = !shortManifest, SubLanguages = "all", WriteInfoJson = true, DownloadArchiveFile = archive, ConcurrentFragments = 2 };
        if (shortManifest)
        {
            var prior = Path.Combine(files.Directory, "nfb.mp4"); await File.WriteAllTextAsync(prior, "previous complete film");
            try { await engine.DownloadAsync(request, null, null, default, completed.Add); throw new InvalidOperationException("A short NFB stream was accepted."); }
            catch (InvalidDataException ex) when (ex.Message.Contains("decoded media duration")) { }
            if (File.Exists(archive) || completed.Count != 0 || await File.ReadAllTextAsync(prior) != "previous complete film") throw new InvalidOperationException("Failed NFB timeline verification changed published data.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        var output = result.OutputPath ?? result.OutputPaths.FirstOrDefault(path => path.EndsWith(".mp4"));
        if (!result.Succeeded || output is null || result.OutputPaths.Count != 2 || completed.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(combined.ToArray())))
            throw new InvalidOperationException("NFB playback lost complete media or exact originals.");
        var before = await Frames(served); var after = await Frames(output);
        if (!before.SequenceEqual(after)) throw new InvalidOperationException("NFB processing lost video/audio frames.");
        var text = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "webvtt", "-"]);
        if (!text.Contains("NFB caption.") || !Directory.EnumerateFiles(files.Directory, "*.vtt").Any(path => File.ReadAllText(path).Contains("NFB caption.")))
            throw new InvalidOperationException("NFB manifest captions were not saved and embedded.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);
        if ((await File.ReadAllTextAsync(archive)).Trim() != "nfb:registry 213822") throw new InvalidOperationException("NFB registry archives changed.");
        if (series)
        {
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(output, ".info.json")));
            if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != 1 || calls.Any(url => url.AbsolutePath.Contains("/first/")))
                throw new InvalidOperationException("The selected NFB episode lost its source position or fetched another film.");
            calls.Clear(); completed.Clear(); var repeated = await engine.DownloadAsync(request, null, null, default, completed.Add);
            if (!repeated.Succeeded || !repeated.Skipped || calls.Count != 2 || completed.Count != 0) throw new InvalidOperationException("A known archived episode fetched its player again.");
        }
        async Task<string[]> Frames(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return json.RootElement.GetProperty("streams").EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() is "video" or "audio")
                .Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()).ToArray();
        }
    }
}
