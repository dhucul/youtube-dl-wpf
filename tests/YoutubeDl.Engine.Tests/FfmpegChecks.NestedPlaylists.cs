using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Pinterest;

internal static partial class FfmpegChecks
{
    private static async Task NestedPinterestMedia(string ffmpeg, bool hls)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "12", "-c:a", "aac", source]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c", "copy", "-f", "hls", "-hls_time", "0.5", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(files.Directory, "part%d.ts"), Path.Combine(files.Directory, "media.m3u8")]);
        var assets = System.IO.Directory.EnumerateFiles(files.Directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=300000,RESOLUTION=160x96,FRAME-RATE=24\nmedia.m3u8" + PinterestChecks.Query + "\n";
        var playlist = string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(files.Directory, "media.m3u8"))).Select(line => line.StartsWith('#') || string.IsNullOrWhiteSpace(line) ? line : line + PinterestChecks.Query)) + "\n";
        var story = PinterestStoryChecks.Pin(4, PinterestStoryChecks.Page("123", PinterestStoryChecks.Block()), PinterestStoryChecks.Page("456", PinterestStoryChecks.Block(PinterestChecks.Other)));
        var board = PinterestBoardChecks.Props([PinterestBoardChecks.Row("1"), PinterestBoardChecks.Row(PinterestChecks.Id, true)], 2);
        var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new PinterestProvider()], _ => PinterestBoardChecks.Http(board, calls: calls, pinHtml: PinterestChecks.Html(story), media: request =>
        {
            var url = request.RequestUri!; var name = Path.GetFileName(url.AbsolutePath);
            if (url.Query != PinterestChecks.Query) throw new InvalidOperationException("Nested media access queries changed.");
            return new(HttpStatusCode.OK) { Content = name.EndsWith(".m3u8") ? new StringContent(name == "media.m3u8" ? playlist : master) :
                new ByteArrayContent(name.EndsWith(".mp4") ? assets["source.mp4"] : assets[name]) };
        }));
        var request = PinterestBoardChecks.Request(files.Directory) with { PlaylistItems = "2", FormatId = hls ? "vHLSV4" : "v720P", FfmpegPath = ffmpeg, AddMetadata = true,
            KeepOriginal = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        var inspected = await engine.InspectAsync(request, default); request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot };
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 4 || callbacks.Count != 4 || callbacks.Distinct().Count() != 4)
            throw new InvalidOperationException("Nested board/story publication lost media or duplicated completion callbacks.");
        foreach (var path in result.OutputPaths)
        {
            using var probe = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path], rejectDiagnostics: true));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString() != "48" || !streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))
                throw new InvalidOperationException("Nested media lost complete frames or audio.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        }
        var infoFiles = result.SidecarPaths.Where(path => path.EndsWith(".info.json")).ToArray();
        if (infoFiles.Length != 2) throw new InvalidOperationException("Nested leaf sidecars were not returned.");
        foreach (var path in infoFiles)
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)); var parent = json.RootElement.GetProperty("playlist_parents")[0];
            if (parent.GetProperty("id").GetString() != PinterestBoardChecks.BoardId || parent.GetProperty("playlist_index").GetInt32() != 2 || parent.GetProperty("n_entries").GetInt32() != 2 ||
                json.RootElement.GetProperty("playlist_id").GetString() != PinterestChecks.Id || json.RootElement.GetProperty("n_entries").GetInt32() != 2)
                throw new InvalidOperationException("Nested board/story source contexts collapsed.");
        }
        if ((await File.ReadAllLinesAsync(archive)).Length != 2 || calls.Any(url => url.AbsolutePath == "/pin/1/")) throw new InvalidOperationException("Nested archive or outer selection included unrelated pins.");
        callbacks.Clear(); calls.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeat.Succeeded || callbacks.Count != 0 || calls.Any(url => url.Host == "v1.pinimg.com")) throw new InvalidOperationException("Nested archive retry retransferred media or emitted completion twice.");
    }
}
