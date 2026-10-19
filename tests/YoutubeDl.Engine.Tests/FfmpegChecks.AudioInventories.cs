using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> AudioInventoryMediaChecks(string ffmpeg) =>
        new[] { "user", "related", "search", "hls-renew", "hls-renew-identity", "hls-renew-shape" }.Select(mode =>
            ("SoundCloud inventories media " + mode, (Func<Task>)(() => AudioInventoryMedia(ffmpeg, mode))));

    private static async Task AudioInventoryMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=800:sample_rate=48000:duration=3", "-vn", "-c:a", "aac", "-b:a", "128k", "-movflags", "frag_keyframe+empty_moov+default_base_moof", source]);
        var bytes = await File.ReadAllBytesAsync(source); var boundary = 0;
        while (boundary + 8 <= bytes.Length && !bytes.AsSpan(boundary + 4, 4).SequenceEqual("moof"u8)) boundary += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary, 4)));
        SoundCloudInventoryChecks.Check(boundary > 0 && boundary < bytes.Length, "Generated audio has no fragment.");
        var kind = mode is "related" or "search" ? mode : "user"; var hls = mode.StartsWith("hls-", StringComparison.Ordinal); var bad = mode is "hls-renew-identity" or "hls-renew-shape";
        var parents = 0; var children = 0; var mediaReads = 0; var denied = 0; var fresh = false; var calls = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => SoundCloudInventoryChecks.Http(message =>
        {
            var uri = message.RequestUri!; calls.Add(uri.AbsoluteUri); SoundCloudInventoryChecks.Check(message.Headers.Authorization is null, "An API credential reached media.");
            if (uri.Host == "soundcloud.com" && uri.AbsolutePath != "/artist/two")
            {
                parents++; return SoundCloudInventoryChecks.Json(SoundCloudInventoryChecks.Page(kind == "related" ? SoundCloudInventoryChecks.Track(111, "one") : kind == "search" ? null : SoundCloudInventoryChecks.User(), kind == "user"));
            }
            if (uri.Host == "api-v2.soundcloud.com" && !uri.AbsolutePath.StartsWith("/media/", StringComparison.Ordinal))
                return SoundCloudInventoryChecks.Json(SoundCloudInventoryChecks.Collection(fresh ? [SoundCloudInventoryChecks.Track(222, "two"), SoundCloudInventoryChecks.Track(111, "one")] : [SoundCloudInventoryChecks.Track(111, "one"), SoundCloudInventoryChecks.Track(222, "two")]));
            if (uri.AbsolutePath == "/artist/two")
            {
                children++; var track = SoundCloudInventoryChecks.Track(children > 1 && mode == "hls-renew-identity" ? 999 : 222, "two"); track["duration"] = 3000; track["full_duration"] = 3000;
                var formats = track["media"]!["transcodings"]!.AsArray(); formats.RemoveAt(hls ? 0 : 1); formats[0]!["duration"] = 3000;
                if (!hls) formats[0]!["format"]!["mime_type"] = "audio/mp4";
                return SoundCloudInventoryChecks.Json(SoundCloudInventoryChecks.Page(track));
            }
            if (uri.Host == "api-v2.soundcloud.com") return SoundCloudInventoryChecks.Json("{\"url\":\"https://cf-media.sndcdn.com/" + (hls ? "playlist.m3u8" : "audio.m4a") + "?signature=" + children + "\"}");
            SoundCloudInventoryChecks.Check(uri.Host == "cf-media.sndcdn.com", "Unselected or foreign playback was requested.");
            if (uri.AbsolutePath == "/playlist.m3u8")
            {
                var duration = children > 1 && mode == "hls-renew-shape" ? "2" : "3";
                return SoundCloudInventoryChecks.Json("#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MAP:URI=\"init.mp4?signature=" + children + "\"\n#EXTINF:" + duration + ",\nsegment.m4s?signature=" + children + "\n#EXT-X-ENDLIST");
            }
            mediaReads++;
            if (hls && uri.AbsolutePath == "/segment.m4s" && uri.Query == "?signature=1") { denied++; return new(HttpStatusCode.Forbidden); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath == "/init.mp4" ? bytes[..boundary] : uri.AbsolutePath == "/segment.m4s" ? bytes[boundary..] : bytes) };
        }));
        var output = Path.Combine(files.Directory, "002 - recording.m4a"); var archive = Path.Combine(files.Directory, "archive.txt");
        await File.WriteAllTextAsync(output, "previous output"); await File.WriteAllTextAsync(archive, "previous archive\n");
        var request = SoundCloudChecks.Request(files.Directory) with { Url = SoundCloudInventoryChecks.Source(kind), OutputTemplate = "%(playlist_index)03d - recording.%(ext)s", PlaylistItems = "2", DownloadArchiveFile = archive, FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, Retries = 0 };
        var info = await engine.InspectAsync(request, default); SoundCloudInventoryChecks.Check(children == 0 && mediaReads == 0 && info.Entries.Count == 2, "Inspection resolved playback/media."); fresh = true;
        request = request with { PlaylistSnapshot = info.PlaylistSnapshot }; var callbacks = new List<string>(); NativeDownloadResult? result = null;
        try { result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); }
        catch (Exception ex) when (bad && ex is InvalidDataException or NotSupportedException or HttpRequestException) { }
        if (bad)
        {
            SoundCloudInventoryChecks.Check(result?.Succeeded != true && denied == 1 && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous output" && await File.ReadAllTextAsync(archive) == "previous archive\n", "Failed HLS renewal changed existing publication."); return;
        }
        SoundCloudInventoryChecks.Check(result!.Succeeded && parents == 2 && children == (hls ? 2 : 1) && denied == (hls ? 1 : 0) && result.OutputPaths.Count == 2 && callbacks.SequenceEqual(result.NewOutputPaths ?? result.OutputPaths), "Fresh audio publication failed: " + result.Error);
        var expected = await AdaptivePacketHashes(ffmpeg, source, "a:0");
        foreach (var path in result.OutputPaths)
        {
            SoundCloudInventoryChecks.Check(expected.AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, path, "a:0")), "Complete coded audio packets changed.");
            await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:a:0", "-f", "null", "-"]);
        }
        var before = (children, mediaReads, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        SoundCloudInventoryChecks.Check(repeat.Succeeded && repeat.Skipped && before == (children, mediaReads, callbacks.Count), "Archived audio fetched playback/media or emitted callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new
        { Version = NativeEngine.Version, EngineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(NativeEngine).Assembly.Location))), Mode = mode, Source = source, Files = result.OutputPaths, Output = output, SelectedPlaybackReads = children, DeniedMediaRequests = denied, ArchiveRepeatPlaybackRequests = 0, ArchiveRepeatMediaRequests = 0, ArchiveRepeatCallbacks = 0, PrivateAccountVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
