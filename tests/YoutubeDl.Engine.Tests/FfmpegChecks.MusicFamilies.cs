using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static readonly string[] MusicModes = MusicFamilyFixture.Cases.Concat(new[] { "jio-hls", "jio-dash", "jio-renew", "qq-renew", "net-renew", "jio-removed", "qq-removed", "net-removed", "jio-changed", "qq-changed", "net-changed", "jio-short", "qq-short", "net-short", "jio-renew-bad", "qq-renew-bad", "net-renew-bad" }).ToArray();
    internal static IEnumerable<(string Name, Func<Task> Test)> MusicFamilyMediaChecks(string ffmpeg) => MusicModes.Select(mode => ("Music families media " + mode, (Func<Task>)(() => MusicFamilyMedia(ffmpeg, mode))));
    private static async Task MusicFamilyMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode.Split('-')[0]; var negative = mode.EndsWith("removed", StringComparison.Ordinal) || mode.EndsWith("changed", StringComparison.Ordinal) || mode.EndsWith("short", StringComparison.Ordinal) || mode.EndsWith("bad", StringComparison.Ordinal);
        var sourceMode = negative ? family + "-album" : mode.EndsWith("renew", StringComparison.Ordinal) || mode is "jio-hls" or "jio-dash" ? family + "-song" : mode;
        var video = sourceMode.EndsWith("-mv", StringComparison.Ordinal); var adaptive = mode is "jio-hls" or "jio-dash";
        var source = Path.Combine(files.Directory, video ? "source.mp4" : adaptive ? "source.m4a" : "source.mp3");
        if (video) await MixedSource(ffmpeg, source);
        else await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=3", "-vn", "-c:a", adaptive ? "aac" : "libmp3lame", "-b:a", "128k", .. (mode == "jio-hls" ? new[] { "-movflags", "frag_keyframe+empty_moov+default_base_moof" } : []), source]);
        var fixture = new MusicFamilyFixture { Audio = video ? null : await File.ReadAllBytesAsync(source), Video = video ? await File.ReadAllBytesAsync(source) : null, RejectOnce = mode.EndsWith("renew", StringComparison.Ordinal) || mode.EndsWith("bad", StringComparison.Ordinal), ChangeOnReject = mode.EndsWith("bad", StringComparison.Ordinal) };
        if (mode == "jio-hls")
        {
            fixture.MediaShape = "m3u8"; var bytes = fixture.Audio!; var boundary = 0;
            while (boundary + 8 <= bytes.Length && !bytes.AsSpan(boundary + 4, 4).SequenceEqual("moof"u8)) boundary += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(boundary, 4)));
            MusicFamilyFixture.Check(boundary > 0 && boundary < bytes.Length, "The generated audio source lacks a fragment.");
            fixture.Resources["/audio.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:3,\nsegment.m4s\n#EXT-X-ENDLIST");
            fixture.Resources["/init.mp4"] = bytes[..boundary]; fixture.Resources["/segment.m4s"] = bytes[boundary..];
        }
        if (mode == "jio-dash")
        {
            fixture.MediaShape = "mpd"; var manifest = Path.Combine(files.Directory, "audio.mpd"); await Run(ffmpeg, ["-v", "error", "-y", "-i", source, "-map", "0:a", "-c", "copy", "-f", "dash", "-seg_duration", "1", manifest]);
            fixture.Resources["/audio.mpd"] = await File.ReadAllBytesAsync(manifest);
            foreach (var path in Directory.EnumerateFiles(files.Directory, "*.m4s")) fixture.Resources["/" + Path.GetFileName(path)] = await File.ReadAllBytesAsync(path);
        }
        var collection = MusicFamilyFixture.Collection(sourceMode); var extension = video ? "mp4" : adaptive ? "m4a" : "mp3"; var output = Path.Combine(files.Directory, (collection ? "002 - " : "") + "recording." + extension); var archive = Path.Combine(files.Directory, "archive.txt");
        await File.WriteAllTextAsync(output, "previous output"); await File.WriteAllTextAsync(archive, "prior 1\n"); var callbacks = new List<string>(); var engine = fixture.Engine;
        var request = MusicFamilyChecks.Request(sourceMode) with { OutputDirectory = files.Directory, OutputTemplate = (collection ? "%(playlist_index)03d - " : "") + "recording.%(ext)s", PlaylistItems = collection ? "2" : "", DownloadArchiveFile = archive, FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true };
        var info = await engine.InspectAsync(request, default); MusicFamilyFixture.Check(fixture.MediaReads == 0 && (!collection || fixture.Grants == 0), "Inventory fetched media or a child grant."); request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
        fixture.Reordered = !negative;
        if (mode.EndsWith("removed", StringComparison.Ordinal)) fixture.RemoveSelected = true;
        if (mode.EndsWith("changed", StringComparison.Ordinal)) fixture.ChangeSelected = true;
        if (mode.EndsWith("short", StringComparison.Ordinal)) { var shortFile = Path.Combine(files.Directory, "short.mp3"); await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=0.3", "-c:a", "libmp3lame", shortFile]); fixture.Audio = await File.ReadAllBytesAsync(shortFile); }
        NativeDownloadResult? result = null;
        try { result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); } catch (Exception ex) when (negative && ex is InvalidDataException or InvalidOperationException or HttpRequestException or NotSupportedException) { }
        if (negative)
        {
            MusicFamilyFixture.Check(result?.Succeeded != true && callbacks.Count == 0 && await File.ReadAllTextAsync(output) == "previous output" && await File.ReadAllTextAsync(archive) == "prior 1\n", "Failed music publication damaged prior output/archive/callbacks.");
            if (mode.EndsWith("removed", StringComparison.Ordinal) || mode.EndsWith("changed", StringComparison.Ordinal)) MusicFamilyFixture.Check(fixture.MediaReads == 0, "Changed membership/identity fetched media.");
            return;
        }
        MusicFamilyFixture.Check(result!.Succeeded && callbacks.SequenceEqual(result.NewOutputPaths ?? result.OutputPaths), "Native music publication failed: " + result.Error);
        var streamInventories = new List<object>();
        foreach (var path in result.OutputPaths)
        {
            await Run(ffmpeg, ["-v", "error", "-xerror", "-i", path, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"]);
            foreach (var role in video ? new[] { "v:0", "a:0" } : ["a:0"])
            {
                var actual = await AdaptivePacketHashes(ffmpeg, path, role); if (actual.Length == 0) continue;
                MusicFamilyFixture.Check(actual.AsEnumerable().SequenceEqual(await AdaptivePacketHashes(ffmpeg, source, role)), "Coded music packets changed during publication."); streamInventories.Add(new { Path = path, Role = role, Packets = actual.Length });
            }
        }
        var before = (fixture.Grants, fixture.MediaReads, callbacks.Count); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        MusicFamilyFixture.Check(repeat.Succeeded && repeat.Skipped && before == (fixture.Grants, fixture.MediaReads, callbacks.Count), "Recorded music fetched another playback grant/media or emitted callbacks.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { Version = NativeEngine.Version, EngineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(NativeEngine).Assembly.Location))), Mode = mode, Source = source, Files = result.OutputPaths, Inventories = streamInventories, Grants = fixture.Grants, MediaReads = fixture.MediaReads, RejectedMediaRequests = fixture.Rejects, ArchiveRepeatPlaybackGrants = 0, ArchiveRepeatMediaRequests = 0, ArchiveRepeatCallbacks = 0, PrivateAccountVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
