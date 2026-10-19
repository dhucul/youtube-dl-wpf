using System.Security.Cryptography;
using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    internal static readonly string[] RtveModes = ["audio", "curl", "metadata", "conversion", "playlist", "interrupted", "stale-membership"];
    internal static IEnumerable<(string Name, Func<Task> Test)> RtveMediaChecks(string ffmpeg) => RtveModes.Select(mode => ("RTVE media " + mode, (Func<Task>)(() => RtveMedia(ffmpeg, mode))));
    internal static async Task RtveMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var source = Path.Combine(files.Directory, "source.mp3");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=310:sample_rate=48000:duration=3", "-c:a", "libmp3lame", "-b:a", "128k", source]);
        var fixture = new RtveChecks.Fixture { Media = await File.ReadAllBytesAsync(source) };
        var collection = mode is "playlist" or "stale-membership";
        var request = RtveChecks.Request(Path.Combine(files.Directory, "output"), collection) with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), AddMetadata = mode == "metadata", AudioOnly = mode == "conversion", AudioFormat = "flac", PlaylistItems = collection ? "2" : "", ExternalDownloader = mode == "curl" ? "curl" : null };
        if (collection)
        {
            var inspected = await fixture.Engine.InspectAsync(request with { ExpandPlaylist = true }, default);
            request = request with { PlaylistSnapshot = inspected.PlaylistSnapshot };
            if (mode == "stale-membership")
            {
                fixture.Mode = "moved"; var membershipCallbacks = new List<string>();
                try { var changed = await fixture.Engine.DownloadAsync(request, null, null, default, membershipCallbacks.Add); RtveChecks.Assert(!changed.Succeeded, "An old RTVE inspection selected replacement membership."); }
                catch (InvalidDataException) { }
                RtveChecks.Assert(fixture.Transfers == 0 && membershipCallbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "Changed RTVE membership transferred or published media."); return;
            }
        }
        if (mode == "curl")
        {
            // Opaque injected handlers must refuse external delegation; the actual proxy transport is checked separately.
            await RtveChecks.Expect<NotSupportedException>(() => fixture.Engine.DownloadAsync(request, null, null, default)); RtveChecks.Assert(fixture.Transfers == 0, "Opaque curl delegation fetched media."); return;
        }
        if (mode == "interrupted")
        {
            YoutubeDl.Engine.Download.PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned RTVE processing interruption") : Task.CompletedTask;
            try { await RtveChecks.Expect<IOException>(() => fixture.Engine.DownloadAsync(request, null, null, default)); }
            finally { YoutubeDl.Engine.Download.PublicationFaults.Observer = null; }
            RtveChecks.Assert(!File.Exists(request.DownloadArchiveFile), "Interrupted RTVE processing updated the archive.");
        }
        var callbacks = new List<string>(); var result = await fixture.Engine.DownloadAsync(request, null, null, default, callbacks.Add);
        RtveChecks.Assert(result.Succeeded && result.OutputPath is not null, "RTVE media did not publish successfully.");
        await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
        if (mode is "audio" or "playlist" or "interrupted") RtveChecks.Assert(SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath!)).SequenceEqual(SHA256.HashData(fixture.Media)), "RTVE changed the complete original MP3.");
        var transfers = fixture.Transfers; var skipped = await fixture.Engine.DownloadAsync(request, null, null, default);
        RtveChecks.Assert(skipped.Succeeded && skipped.Skipped && fixture.Transfers == transfers, "RTVE archive repeat fetched media.");
        if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, fixture.Api, fixture.Transfers, Callbacks = callbacks.Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
