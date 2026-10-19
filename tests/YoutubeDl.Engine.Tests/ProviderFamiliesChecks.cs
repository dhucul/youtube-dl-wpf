using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers.Arte;
using YoutubeDl.Engine.Providers.Orf;
using YoutubeDl.Engine.Providers.Tvp;

internal static partial class ProviderFamiliesChecks
{
    internal static NativeDownloadRequest Request(Fixture fixture, string folder, bool collection = false) => new() { Url = collection ? fixture.CollectionUrl : fixture.Url, OutputDirectory = folder, FileName = "recording", Retries = 0, MaximumPlaylistEntries = 100, NoMtime = true };
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
        new[] { "tvp", "arte", "orf" }.SelectMany(family => new[] { "metadata", "collection", "unavailable", "limit", "incomplete", "denied", "drm", "wrong-id", "wrong-type", "foreign-media", "headers", "archive", "no-overwrite", "renewal", "http", "redirect", "redirect-denied" }.Select(mode => ("Provider families " + family + " " + mode, (Func<Task>)(() => Contract(family, mode)))))
        .Concat(new[] { "arte-age", "arte-chapter", "arte-edition", "arte-region", "orf-size", "tvp-identity", "arte-resource-id", "orf-resource-id", "tvp-materials", "tvp-cache-redirect" }.Select(name => ("Provider families policy " + name, (Func<Task>)(() => Contract(name.Split('-')[0], name[(name.IndexOf('-') + 1)..])))))
        .Concat(new[] { ("Provider families URL grammar and embed scope", (Func<Task>)Addresses), ("Provider families unsupported classic controls stay explicit", (Func<Task>)ClassicRefusals) }).Concat(NetworkChecks).Concat(ClockChecks);
    private static async Task Contract(string family, string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var fixture = new Fixture(family) { Mode = mode }; var collection = mode is "collection" or "unavailable" or "limit" or "incomplete" or "edition" or "materials";
        var request = Request(fixture, files.Directory, collection); using var http = fixture.Http(mode == "headers");
        if (mode == "limit") request = request with { MaximumPlaylistEntries = 1 };
        if (mode == "archive")
        {
            request = request with { DownloadArchiveFile = Path.Combine(files.Directory, "archive.txt") }; await File.WriteAllTextAsync(request.DownloadArchiveFile, fixture.Extractor.ToLowerInvariant() + " " + fixture.Identity + "\n");
            var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.Transfers == 0, "An archived public programme transferred media."); return;
        }
        if (mode == "no-overwrite")
        {
            request = request with { NoOverwrites = true, Container = family == "orf" ? "" : "mp4" }; await File.WriteAllTextAsync(Path.Combine(files.Directory, family == "orf" ? "recording.mp3" : "recording.mp4"), "saved");
            var result = await fixture.Engine.DownloadAsync(request, null, null, default); Assert(result.Succeeded && result.Skipped && fixture.Transfers == 0, "A protected public programme transferred media."); return;
        }
        if (mode is "wrong-id" or "wrong-type" or "foreign-media" or "limit" or "incomplete" or "http" or "redirect" or "chapter" or "edition" or "size" or "identity" or "resource-id")
        { await Expect<Exception>(() => fixture.Provider.ExtractAsync(request, http, default)); Assert(fixture.Transfers == 0, "A refused public descriptor fetched media."); return; }
        var page = await fixture.Provider.ExtractAsync(request, http, default);
        if (mode is "denied" or "drm" or "region") { Assert(page.Media.Count == 0 && page.Warnings.Count > 0, "A public access denial became successful playback."); return; }
        if (collection)
        {
            Assert(page.IsPlaylist && page.Entries.Count == 2 && page.Entries.Select(entry => entry.Position).SequenceEqual(new[] { 1, 2 }) && page.Entries.All(entry => entry.ResolvedMedia is null), "A public collection lost order or retained signed playback links.");
            if (mode == "unavailable") Assert(page.Entries[0].Available && !page.Entries[1].Available, "An unavailable collection slot was hidden or shifted.");
            Assert(fixture.Transfers == 0, "Collection discovery transferred media."); return;
        }
        Assert(page.Id == fixture.Identity && page.DurationSeconds == 3 && page.Media.Count > 0, "A public provider changed its selected metadata.");
        if (mode == "headers") Assert(fixture.Requests.All(item => !item.Headers.Any(name => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("X-Fixture-Secret", StringComparison.OrdinalIgnoreCase) || name.Equals("Referer", StringComparison.OrdinalIgnoreCase))), "Source credentials entered a public verification API.");
        if (mode == "age") Assert(page.AgeLimit == 16, "ARTE lost its declared age restriction.");
        if (mode is "renewal" or "cache-redirect" or "redirect-denied")
        {
            using var anonymous = http.CreateAnonymousSession(); using var playback = anonymous.WithAuthorization(page.PlaybackAuthorization!); var output = Path.Combine(files.Directory, "body.bin");
            if (mode == "redirect-denied") { await Expect<InvalidDataException>(() => new FileDownloader(playback).DownloadAsync(page.Media[0].Url, output, null, null, default, 0)); Assert(!File.Exists(output) && fixture.Transfers == 1, "An unrelated redirect received public playback traffic."); return; }
            await new FileDownloader(playback).DownloadAsync(page.Media[0].Url, output, null, null, default, 0);
            Assert(File.ReadAllBytes(output).SequenceEqual(fixture.Data) && fixture.Transfers == 2 && fixture.Generation == (mode == "renewal" ? 2 : 1), "A public playback renewal or declared cache redirect changed.");
        }
        else Assert(fixture.Transfers == 0, "Metadata inspection transferred media.");
    }
    private static Task Addresses()
    {
        Assert(TvpAddress.Parse(new("https://vod.tvp.pl/seriale,18/fixture-odcinki,19/odcinek-1,S01E01,41")).Id == "41", "TVP episode grammar selected its parent ID.");
        Assert(OrfPodcastAddress.Parse(new("https://sound.orf.at/podcast/oe1/fixture/episode-one")).Episode == "episode-one", "ORF lost its selected episode slug.");
        var embed = "https://www.arte.tv/player/v7/index.php?json_url=" + Uri.EscapeDataString("https://api.arte.tv/api/player/v2/config/fr/123456-001-A?platform=ARTE_NEXT");
        Assert(ArteAddress.Parse(new(embed)).Embed, "ARTE lost its declared embed identity.");
        foreach (var url in new[] { "https://www.arte.tv/player/v7/index.php?json_url=https%3A%2F%2Fother.example%2Fsource", "https://www.arte.tv/fr/videos/123456-001-A/?redirect=1" }) Reject(() => ArteAddress.Parse(new(url)));
        Reject(() => TvpAddress.Parse(new("https://vod.tvp.pl.evil.example/programy,88/fixture,41"))); Reject(() => OrfPodcastAddress.Parse(new("https://sound.orf.at/radio/oe1"))); return Task.CompletedTask;
    }
    private static async Task ClassicRefusals()
    {
        var fixture = new Fixture("arte"); var request = Request(fixture, "unused");
        foreach (var option in new[] { "--geo-bypass-country", "--geo-bypass-ip-block", "--ap-mso", "--ap-username", "--ap-password" }) Reject(() => NativeExtraArguments.Apply(request, [option, "owned-unimplemented-value"]));
        await Expect<NotSupportedException>(() => fixture.Engine.InspectAsync(request with { Authentication = new() { Username = "owned", Password = "owned" } }, default));
        Assert(fixture.Api == 0, "An unsupported account option reached a public source.");
        Assert(!JsonSerializer.Serialize(request).Contains("owned-unimplemented-value", StringComparison.Ordinal), "A refused option leaked into metadata.");
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected failure was not raised."); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidDataException) { return; } throw new Exception("An unsupported provider argument was accepted."); }
}
