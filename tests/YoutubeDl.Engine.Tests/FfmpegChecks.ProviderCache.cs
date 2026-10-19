using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using YoutubeDl.Engine;
using YoutubeDl.Engine.PlayerRuntime;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> ProviderCacheMediaChecks(string ffmpeg) =>
        new[] { "person", "channel", "movie", "tags", "playlist", "embed", "hls-renew", "dash-renew", "hls-renew-shape", "dash-renew-identity", "cache-playback" }
            .Select(mode => ("Provider cache media " + mode, (Func<Task>)(() => ProviderCacheMedia(ffmpeg, mode))));
    private static async Task ProviderCacheMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp4"); await MixedSource(ffmpeg, source);
        var bytes = await File.ReadAllBytesAsync(source); var hls = mode.StartsWith("hls-", StringComparison.Ordinal); var dash = mode.StartsWith("dash-", StringComparison.Ordinal); var cached = mode == "cache-playback";
        var bad = mode is "hls-renew-shape" or "dash-renew-identity"; var kind = mode is "channel" or "movie" or "tags" or "playlist" ? mode : "person"; var single = mode == "embed" || cached;
        var fresh = false; var children = 0; var mediaReads = 0; var rejected = 0; var scripts = 0; var callbacks = new List<string>(); var calls = new List<string>();
        byte[]? fragmented = null; var boundary = 0; string? dashManifest = null;
        if (hls)
        {
            var fragment = Path.Combine(files.Directory, "fragmented.mp4"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-movflags", "frag_keyframe+empty_moov+default_base_moof", fragment]); fragmented = await File.ReadAllBytesAsync(fragment);
            while (boundary + 8 <= fragmented.Length && !fragmented.AsSpan(boundary + 4, 4).SequenceEqual("moof"u8)) boundary += checked((int)BinaryPrimitives.ReadUInt32BigEndian(fragmented.AsSpan(boundary, 4)));
            ProviderCacheChecks.Check(boundary > 0 && boundary < fragmented.Length, "Generated paired source has no fragment.");
        }
        if (dash)
        {
            var manifest = Path.Combine(files.Directory, "source.mpd"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "dash", "-seg_duration", "1", manifest]); dashManifest = await File.ReadAllTextAsync(manifest);
        }
        var engine = new NativeEngine(playerRuntime: cached ? new ManagedYouTubePlayerRuntime() : null, httpFactory: _ => ProviderCacheChecks.Http(message =>
        {
            var uri = message.RequestUri!; calls.Add(uri.AbsoluteUri); ProviderCacheChecks.Check(message.Headers.Authorization is null, "A credential reached public playback.");
            if (cached)
            {
                if (uri.AbsolutePath == "/watch")
                {
                    children++; return SoundCloudChecks.Response("<script>ytInitialPlayerResponse=" + JsonSerializer.Serialize(new { videoDetails = new { videoId = "abcdefghijk", title = "Fresh cached playback", lengthSeconds = "3" }, playabilityStatus = new { status = "OK" }, streamingData = new { formats = new[] { new { itag = 18, url = "https://r1.googlevideo.com/videoplayback?n=fixture", mimeType = "video/mp4; codecs=\"avc1.42c01e,mp4a.40.2\"", width = 128, height = 72, contentLength = bytes.Length.ToString() } } } }) + ";var config={\"jsUrl\":\"https://www.youtube.com/s/player/cache-fixture/player_ias.vflset/en_US/base.js\"};</script>");
                }
                if (uri.Host == "www.youtube.com") { scripts++; return SoundCloudChecks.Response(PlayerRuntimeChecks.Script); }
                ProviderCacheChecks.Check(uri.Query.Contains("n=solved-fixture"), "Cached player transformation was not used for transfer."); mediaReads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            if (uri.AbsolutePath == ProviderCacheChecks.Connection(kind)) return ProviderCacheChecks.Json(ProviderCacheChecks.Page(kind, 1, fresh ? [ProviderCacheChecks.Video(ProviderCacheChecks.Second), ProviderCacheChecks.Video()] : [ProviderCacheChecks.Video(), ProviderCacheChecks.Video(ProviderCacheChecks.Second)]));
            if (uri.AbsolutePath is "/api/metainfo/tv/7/" or "/api/playlist/custom/7/") return ProviderCacheChecks.Json(ProviderCacheChecks.Parent(kind));
            if (uri.AbsolutePath.StartsWith("/api/video/", StringComparison.Ordinal)) { children++; return ProviderCacheChecks.Json(ProviderCacheChecks.Video(children > 1 && mode == "dash-renew-identity" ? ProviderCacheChecks.First : ProviderCacheChecks.Second)); }
            if (uri.AbsolutePath.StartsWith("/api/play/options/", StringComparison.Ordinal)) return ProviderCacheChecks.Json(ProviderCacheChecks.Options(ProviderCacheChecks.Second, "https://video.rtbcdn.ru/recording." + (hls ? "m3u8" : dash ? "mpd" : "mp4") + "?sign=" + children));
            ProviderCacheChecks.Check(uri.Host == "video.rtbcdn.ru", "Unselected/foreign playback was requested.");
            if (uri.AbsolutePath == "/recording.m3u8") return SoundCloudChecks.Response("#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MAP:URI=\"init.mp4?sign=" + children + "\"\n#EXTINF:" + (children > 1 && mode == "hls-renew-shape" ? "2" : "3") + ",\nsegment.m4s?sign=" + children + "\n#EXT-X-ENDLIST");
            if (uri.AbsolutePath == "/recording.mpd")
            {
                var document = XDocument.Parse(dashManifest!); foreach (var attr in document.Descendants().Attributes().Where(a => a.Name.LocalName is "media" or "initialization")) attr.Value += "?sign=" + children;
                return SoundCloudChecks.Response(document.ToString());
            }
            mediaReads++;
            if ((hls && uri.AbsolutePath == "/segment.m4s" || dash && uri.AbsolutePath.Contains("chunk-", StringComparison.Ordinal)) && uri.Query == "?sign=1" && rejected == 0) { rejected++; return new(HttpStatusCode.Forbidden); }
            var body = hls ? uri.AbsolutePath == "/init.mp4" ? fragmented![..boundary] : fragmented![boundary..] : dash ? File.ReadAllBytes(Path.Combine(files.Directory, Path.GetFileName(uri.AbsolutePath))) : bytes;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }));
        var output = Path.Combine(files.Directory, single ? "recording.mp4" : "002 - recording.mp4"); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(output, "previous output"); await File.WriteAllTextAsync(archive, "prior archive\n");
        var request = new NativeDownloadRequest { Url = cached ? new("https://www.youtube.com/watch?v=abcdefghijk") : mode == "embed" ? new("https://rutube.ru/play/embed/" + ProviderCacheChecks.Second) : ProviderCacheChecks.Source(kind), OutputDirectory = files.Directory,
            OutputTemplate = single ? "recording.%(ext)s" : "%(playlist_index)03d - recording.%(ext)s", PlaylistItems = single ? "" : "2", DownloadArchiveFile = archive, FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, Retries = 0, CacheDirectory = cached ? Path.Combine(files.Directory, "cache") : null };
        var info = await engine.InspectAsync(request, default); ProviderCacheChecks.Check(mediaReads == 0 && (single || children == 0), "Inventory fetched child playback/media."); fresh = true; request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
        NativeDownloadResult? result = null; try { result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); } catch (Exception ex) when (bad && ex is InvalidDataException or HttpRequestException) { }
        if (bad) { ProviderCacheChecks.Check(result?.Succeeded != true && rejected == 1 && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous output" && await File.ReadAllTextAsync(archive) == "prior archive\n", "Failed renewal damaged prior publication."); return; }
        ProviderCacheChecks.Check(result!.Succeeded && callbacks.SequenceEqual(result.NewOutputPaths ?? result.OutputPaths) && (!cached || scripts == 1 && children == 2), "Fresh provider/cache publication failed: " + result.Error);
        var inventories = new List<object>();
        foreach (var path in result.OutputPaths)
        {
            await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
            foreach (var role in new[] { "v:0", "a:0" })
            {
                var actual = await AdaptivePacketHashes(ffmpeg, path, role); if (actual.Length == 0) continue;
                ProviderCacheChecks.Check(actual.AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, source, role)), "Published/retained coded packets changed."); inventories.Add(new { Path = path, Role = role, Packets = actual.Length });
            }
        }
        var before = (children, mediaReads, scripts, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); ProviderCacheChecks.Check(repeat.Succeeded && repeat.Skipped && before == (children, mediaReads, scripts, callbacks.Count), "Archived playback fetched media/source/player or emitted callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { Version = NativeEngine.Version, EngineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(NativeEngine).Assembly.Location))), Mode = mode, Source = source, Files = result.OutputPaths, Inventories = inventories, SelectedPlaybackReads = children, ScriptsFetched = scripts, RejectedMediaRequests = rejected, ArchiveRepeatPlaybackRequests = 0, ArchiveRepeatMediaRequests = 0, ArchiveRepeatCallbacks = 0, PrivateAccountVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
