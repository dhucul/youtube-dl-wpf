using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    internal static readonly string[] RadioCompletionModes = ["orf-direct", "orf-item", "orf-sequence", "orf-flac", "orf-no-overwrite", "orf-interrupted", "orf-malformed", "orf-corrupt", "geo-native", "geo-curl", "geo-denied-native", "geo-denied-curl", "rf-direct", "rf-flac", "rf-collection", "rf-series", "rf-renewal", "rf-member-replaced", "rf-interrupted", "rf-no-overwrite", "rf-denied"];
    internal static IEnumerable<(string Name, Func<Task> Test)> RadioCompletionMediaChecks(string ffmpeg) => RadioCompletionModes.Select(mode => ("Radio completion media " + mode, (Func<Task>)(() => RadioCompletionMedia(ffmpeg, mode))));
    internal static async Task RadioCompletionMedia(string ffmpeg, string mode, string? retained = null)
    {
        using var files = new Files(retained, retained is null); var family = mode.Split('-')[0]; var operation = mode[(mode.IndexOf('-') + 1)..]; var source = Path.Combine(files.Directory, family == "rf" ? "source.m4a" : "source.mp3");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=540:sample_rate=48000:duration=3", "-c:a", family == "rf" ? "aac" : "libmp3lame", "-b:a", "128k", source]); var clean = await File.ReadAllBytesAsync(source);
        NativeEngine engine; NativeDownloadRequest request; Func<int> count; RadioCompletionChecks.RadioFranceFixture? rf = null; OrfBroadcastChecks.Fixture? orf = null; RadioCompletionChecks.GeoServer? server = null;
        var raw = clean.Concat(new byte[] { 0xff }).ToArray();
        if (family == "orf")
        {
            if (operation == "malformed") raw = clean[..^100].Concat(new byte[] { 0xff }).ToArray();
            if (operation == "corrupt") { raw = raw.ToArray(); for (var i = 433; i < 449 && i < raw.Length - 1; i++) raw[i] = 0xff; }
            orf = new("radio") { Mode = operation is "item" or "sequence" ? operation : "metadata", Data = raw };
            if (operation == "sequence")
            {
                var second = Path.Combine(files.Directory, "source-second.mp3"); await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=1080:sample_rate=48000:duration=3", "-c:a", "libmp3lame", "-b:a", "128k", second]); orf.Files = new() { ["source-part0.mp3"] = raw, ["source-part1.mp3"] = (await File.ReadAllBytesAsync(second)).Concat(new byte[] { 0xff }).ToArray() };
            }
            engine = orf.Engine; request = OrfBroadcastChecks.Request(orf, Path.Combine(files.Directory, "output")); count = () => orf.Transfers;
        }
        else if (family == "rf")
        {
            rf = new() { Mode = operation is "renewal" or "denied" or "series" ? operation : "metadata", Data = clean }; engine = rf.Engine; var collection = operation is "collection" or "series" or "member-replaced"; request = RadioCompletionChecks.Request(rf, Path.Combine(files.Directory, "output"), collection) with { PlaylistItems = collection ? "2" : "" }; count = () => rf.MediaCalls;
            if (collection)
            {
                var info = await engine.InspectAsync(request with { ExpandPlaylist = true }, default); request = request with { PlaylistSnapshot = info.PlaylistSnapshot };
                if (operation == "member-replaced")
                {
                    rf.Mode = "parent"; await ProviderFamiliesChecks.Expect<InvalidDataException>(() => engine.DownloadAsync(request, null, null, default)); RadioCompletionChecks.Assert(rf.MediaCalls == 0, "Changed RF inspection transferred replacement audio."); return;
                }
            }
        }
        else
        {
            server = new() { Data = clean, Deny = operation.StartsWith("denied", StringComparison.Ordinal) }; engine = new([new RadioCompletionChecks.GeoProvider(server)]); request = new() { Url = new(server.Root, "page"), OutputDirectory = Path.Combine(files.Directory, "output"), FileName = "recording", GeoVerification = new() { ForwardedForMode = "8.8.8.8/32" }, ExternalDownloader = operation.EndsWith("curl", StringComparison.Ordinal) ? "curl" : null, Retries = 0, NoCache = true }; count = () => server.Headers.Count(header => header.Path.StartsWith("/audio", StringComparison.Ordinal));
        }
        try
        {
            request = request with { FfmpegPath = ffmpeg, KeepOriginal = true, DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt"), AudioOnly = operation == "flac", AudioFormat = "flac", NoMtime = true };
            if (operation.StartsWith("denied", StringComparison.Ordinal) || operation == "denied")
            {
                if (family == "geo") await ProviderFamiliesChecks.Expect<System.Net.Http.HttpRequestException>(() => engine.DownloadAsync(request, null, null, default));
                else { var denied = await engine.DownloadAsync(request, null, null, default); RadioCompletionChecks.Assert(!denied.Succeeded && count() == 0, "A denied RF source published playback."); }
                RadioCompletionChecks.Assert(!File.Exists(request.DownloadArchiveFile), "A preserved denial modified an archive."); return;
            }
            if (operation is "malformed" or "corrupt")
            {
                var callbacks = new List<string>(); await ProviderFamiliesChecks.Expect<Exception>(() => engine.DownloadAsync(request, null, null, default, callbacks.Add)); RadioCompletionChecks.Assert(callbacks.Count == 0 && !File.Exists(request.DownloadArchiveFile), "MP3 clipping repair suppressed damaged audio errors."); return;
            }
            if (operation == "interrupted")
            {
                PublicationFaults.Observer = phase => phase == "processing" ? throw new IOException("Owned radio boundary interruption") : Task.CompletedTask;
                try { await ProviderFamiliesChecks.Expect<IOException>(() => engine.DownloadAsync(request, null, null, default)); } finally { PublicationFaults.Observer = null; }
                RadioCompletionChecks.Assert(!File.Exists(request.DownloadArchiveFile), "Interrupted radio processing recorded success.");
            }
            var completed = new List<string>(); var result = await engine.DownloadAsync(request, null, null, default, completed.Add); RadioCompletionChecks.Assert(result.Succeeded && result.OutputPath is not null && completed.Count > 0, "Radio completion media failed: " + result.Error);
            await Run(ffmpeg, ["-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
            if (family == "orf")
            {
                var originals = operation == "sequence" ? orf!.Files.Values.ToArray() : [raw];
                foreach (var original in originals) RadioCompletionChecks.Assert(result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(original)), "The exact original clipped MP3 object was not retained.");
                RadioCompletionChecks.Assert(!File.ReadAllBytes(result.OutputPath!).SequenceEqual(raw), "The incomplete sync prefix remained in the completed media.");
            }
            if (server is not null) RadioCompletionChecks.Assert(server.Headers.Count >= 2 && server.Headers.All(header => header.Forwarded == "8.8.8.8"), "Native/curl media lost the one job-owned forwarded address.");
            var transferred = count();
            if (operation == "no-overwrite") { var protectedResult = await engine.DownloadAsync(request with { NoOverwrites = true, DownloadArchiveFile = "" }, null, null, default); RadioCompletionChecks.Assert(protectedResult.Succeeded && protectedResult.Skipped && count() == transferred, "Protected normalized media retransferred its source."); }
            var repeat = await engine.DownloadAsync(request, null, null, default); RadioCompletionChecks.Assert(repeat.Succeeded && repeat.Skipped && count() == transferred, "An archived completed radio source was retransferred.");
            if (retained is not null) await File.WriteAllTextAsync(Path.Combine(files.Directory, "verification.json"), JsonSerializer.Serialize(new { mode, Source = source, result.OutputPath, result.OutputPaths, result.SidecarPaths, Transfers = count(), Callbacks = completed.Count, HeaderRequests = server?.Headers.Count, ActualCurl = request.ExternalDownloader == "curl" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (server is not null) await server.DisposeAsync(); }
    }
}
