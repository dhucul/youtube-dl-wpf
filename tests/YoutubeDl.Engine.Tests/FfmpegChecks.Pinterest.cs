using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Pinterest;

internal static partial class FfmpegChecks
{
    private static async Task PinterestMedia(string ffmpeg, string mode, bool board = false, bool snapshot = false, bool story = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=750:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "12", "-c:a", "aac", source]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "media.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24\nmedia.m3u8" + PinterestChecks.Query + "\n";
        var playlist = string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"))).Select(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line) ? line : line + PinterestChecks.Query)) + "\n";
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var incomplete = mode.StartsWith("incomplete", StringComparison.Ordinal); var hls = mode.EndsWith("hls", StringComparison.Ordinal);
        var pin = PinterestChecks.Pin(incomplete ? 20 : 2);
        if (story)
        {
            var duration = incomplete ? 20 : 2;
            pin = mode == "multi" ? PinterestStoryChecks.Pin(4, PinterestStoryChecks.Page("123", new System.Text.Json.Nodes.JsonObject { ["__typename"] = "StoryPinImageBlock", ["blockType"] = 1 }, PinterestStoryChecks.Block()),
                PinterestStoryChecks.Page("456", PinterestStoryChecks.Block(PinterestChecks.Other))) : PinterestStoryChecks.Pin(duration, PinterestStoryChecks.Page("123", PinterestStoryChecks.Block(seconds: duration)));
            foreach (var page in pin["storyPinData"]!["pages"]!.AsArray())
                foreach (var block in page!["blocks"]!.AsArray().Where(block => block!["videoDataV2"] is not null))
                {
                    var asset = block!["videoDataV2"]!["videoList"]!["v720P"]!["url"]!.GetValue<string>().Contains(PinterestChecks.Other) ? PinterestChecks.Other : PinterestChecks.Asset;
                    var captionConfig = PinterestChecks.Pin()["videos"]!["videoListForCaptions"]!.ToJsonString().Replace(PinterestChecks.Asset, asset, StringComparison.Ordinal);
                    block["videoDataV2"]!["videoListForCaptions"] = System.Text.Json.Nodes.JsonNode.Parse(captionConfig);
                }
        }
        HttpResponseMessage Media(HttpRequestMessage mediaRequest)
        {
            var url = mediaRequest.RequestUri!; var name = Path.GetFileName(url.AbsolutePath);
            if (url.Query != PinterestChecks.Query) throw new InvalidOperationException("Pinterest offered resource queries changed.");
            return new(HttpStatusCode.OK) { Content = name.EndsWith(".vtt") ? new StringContent("WEBVTT\n\n00:00:00.100 --> 00:00:01.900\nFixture pin caption.\n") :
                name == PinterestChecks.Asset + ".m3u8" ? new StringContent(master) : name == "media.m3u8" ? new StringContent(playlist) :
                new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }
        var engine = new NativeEngine([new PinterestProvider()], _ => board ? PinterestBoardChecks.Http(PinterestBoardChecks.Props(
            [PinterestBoardChecks.Row("1"), PinterestBoardChecks.Row("2"), PinterestBoardChecks.Row(PinterestChecks.Id, true)], 3), calls: calls, media: Media) :
            PinterestChecks.Http(PinterestChecks.Html(pin), calls, Media));
        var request = (board ? PinterestBoardChecks.Request(files.Directory) : PinterestChecks.Request(files.Directory)) with {
            PlaylistItems = board || story && mode == "multi" ? "3" : "", FormatId = hls ? "vHLSV4" : "v720P", FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true,
            WriteAutoSubs = true, EmbedSubs = true, SubLanguages = "en-us", WriteInfoJson = true, DownloadArchiveFile = archive };
        if (snapshot)
        {
            var inspected = await engine.InspectAsync(request, default);
            request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot ?? throw new InvalidOperationException("The board inspection did not capture stable selection identities.") };
            calls.Clear();
        }
        if (incomplete)
        {
            var previous = Path.Combine(files.Directory, "pinterest.mp4"); await File.WriteAllTextAsync(previous, "previous complete pin");
            try { await engine.DownloadAsync(request, null, null, default, callbacks.Add); throw new InvalidOperationException("Incomplete Pinterest playback was published."); }
            catch (InvalidDataException) { }
            if (callbacks.Count != 0 || File.Exists(archive) || await File.ReadAllTextAsync(previous) != "previous complete pin")
                throw new InvalidOperationException("Failed Pinterest verification changed published files or archive state.");
            return;
        }
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        var primary = result.OutputPaths.FirstOrDefault(path => !path.Contains(".original."));
        if (!result.Succeeded || primary is null || result.OutputPaths.Count != 2 || callbacks.Count != 2 || !result.SidecarPaths.Any(path => path.EndsWith(".vtt")))
            throw new InvalidOperationException("Pinterest media/original/caption publication was incomplete.");
        using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", primary], rejectDiagnostics: true));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48" ||
            !streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio")) throw new InvalidOperationException("Pinterest processing lost complete frames or audio.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        var embedded = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", primary, "-map", "0:s:0", "-f", "srt", "-"], rejectDiagnostics: true);
        var caption = result.SidecarPaths.Single(path => path.EndsWith(".vtt"));
        if (!embedded.Contains("Fixture pin caption.") || !embedded.Contains("00:00:00,100 --> 00:00:01,900") || !(await File.ReadAllTextAsync(caption)).Contains("Fixture pin caption."))
            throw new InvalidOperationException("Pinterest captions lost text or original timing.");
        if (!hls && !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(assets["source.mp4"]))) throw new InvalidOperationException("Pinterest MP4 original bytes changed.");
        var expectedArchive = story && mode == "multi" ? "pinterest:story " + PinterestChecks.Id + "-456-" + PinterestChecks.Other : "pinterest " + PinterestChecks.Id;
        if ((await File.ReadAllTextAsync(archive)).Trim() != expectedArchive) throw new InvalidOperationException("Pinterest archive ownership changed.");
        if (story && mode == "multi" && calls.Where(url => url.Host == "v1.pinimg.com").Any(url => !url.AbsolutePath.Contains(PinterestChecks.Other)))
            throw new InvalidOperationException("An unselected story asset was transferred.");
        if (board)
        {
            if (snapshot && calls.Any(url => url.AbsolutePath == PinterestBoardChecks.Page.AbsolutePath))
                throw new InvalidOperationException("Inspected named-video selection fetched a new board listing.");
            if (calls.Where(url => url.Host == "www.pinterest.com" && url.AbsolutePath.StartsWith("/pin/", StringComparison.Ordinal)).Any(url => !url.AbsolutePath.Contains(PinterestChecks.Id)))
                throw new InvalidOperationException("Pinterest board fetched an unselected pin.");
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(result.SidecarPaths.Single(path => path.EndsWith(".info.json"))));
            if (info.RootElement.GetProperty("playlist_index").GetInt32() != 3 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != 1 || info.RootElement.GetProperty("n_entries").GetInt32() != 3)
                throw new InvalidOperationException("Pinterest board source positions or selected-job metadata changed.");
        }
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(url => url.Host != "www.pinterest.com") || board && calls.Any(url => url.AbsolutePath.StartsWith("/pin/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Pinterest archive repeat fetched selected pin, media or related files.");
    }
}
