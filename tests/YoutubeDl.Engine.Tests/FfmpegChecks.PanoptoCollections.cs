using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Panopto;

internal static partial class FfmpegChecks
{
    private static async Task PanoptoCollectionMedia(string ffmpeg, bool playlist)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<(Uri Url, string Body)>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new PanoptoProvider()], _ => PanoptoCollectionChecks.Http(calls, (url, _) =>
            url.Host == "cdn.example" ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : null));
        var request = (playlist ? PanoptoCollectionChecks.Playlist(files.Directory) : PanoptoCollectionChecks.Folder(files.Directory)) with {
            PlaylistItems = "2", FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, DownloadArchiveFile = archive,
            OutputTemplate = "%(playlist_index)02d-%(playlist_autonumber)02d-%(id)s.%(ext)s" };
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        var primary = result.OutputPaths.FirstOrDefault(path => !path.Contains(".original."));
        if (!result.Succeeded || primary is null || result.OutputPaths.Count != 2 || callbacks.Count != 2 ||
            !Path.GetFileName(primary).StartsWith("02-01-" + PanoptoCollectionChecks.Id(2)) || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))
            throw new InvalidOperationException("Collection selection lost media, source positions, autonumbers or original bytes.");
        if (calls.Count(call => call.Url.AbsolutePath.EndsWith("Viewer.aspx")) != 1 || calls.Count(call => call.Url.AbsolutePath.EndsWith("DeliveryInfo.aspx")) != 1 ||
            calls.Any(call => call.Url.AbsolutePath.EndsWith("Viewer.aspx") && !call.Url.Query.Contains(PanoptoCollectionChecks.Id(2))))
            throw new InvalidOperationException("An unselected collection recording was resolved.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"], rejectDiagnostics: true);
        var infoPath = Path.ChangeExtension(primary, ".info.json"); using var info = JsonDocument.Parse(await File.ReadAllTextAsync(infoPath));
        if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != 1 ||
            (await File.ReadAllTextAsync(archive)).Trim() != "panopto:delivery campus.example/" + PanoptoCollectionChecks.Id(2))
            throw new InvalidOperationException("Collection metadata or recording archive identity changed.");
        calls.Clear(); callbacks.Clear(); var repeated = await engine.DownloadAsync(request, null, null, default, callbacks.Add);
        if (!repeated.Succeeded || callbacks.Count != 0 || calls.Any(call => call.Url.AbsolutePath.EndsWith("Viewer.aspx") || call.Url.Host == "cdn.example"))
            throw new InvalidOperationException("Archived collection entries fetched playback or emitted new-file callbacks.");
    }
}
