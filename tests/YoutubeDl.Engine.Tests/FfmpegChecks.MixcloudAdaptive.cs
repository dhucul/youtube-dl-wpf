using System.Net;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Mixcloud;

internal static partial class FfmpegChecks
{
    private static async Task MixcloudAdaptive(string ffmpeg, string mode)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.m4a");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "aac", source]);
        var manifest = Path.Combine(files.Directory, mode == "hls" ? "index.m3u8" : "manifest.mpd");
        if (mode == "hls") await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c:a", "copy", "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod", manifest]);
        else await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", source, "-c:a", "copy", "-f", "dash", "-seg_duration", "1", manifest]);
        var field = mode == "hls" ? "hlsUrl" : "dashUrl"; var calls = new List<Uri>(); var callbacks = new List<string>(); var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = new NativeEngine([new MixcloudProvider()], _ => MixcloudChecks.Http(calls, playback: MixcloudChecks.Playback(kind: field), media: r =>
        {
            if (r.Headers.Contains("Authorization") || r.Headers.Contains("Cookie") || !r.Headers.UserAgent.ToString().StartsWith("YoutubeDlModernNative/")) throw new InvalidOperationException("Mixcloud adaptive request lost identification or leaked credentials.");
            var name = Path.GetFileName(r.RequestUri!.AbsolutePath); var path = Path.Combine(files.Directory, name); if (!File.Exists(path)) throw new InvalidOperationException("Unexpected adaptive resource.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) };
        }));
        var request = MixcloudChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = true, AddMetadata = true, WriteInfoJson = true, FormatId = mode, DownloadArchiveFile = archive };
        var result = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!result.Succeeded || result.OutputPaths.Count < 2 || callbacks.Count != result.OutputPaths.Count) throw new InvalidOperationException("Mixcloud adaptive complete publication failed.");
        foreach (var path in result.OutputPaths) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
        calls.Clear(); callbacks.Clear(); var repeat = await engine.DownloadAsync(request, null, null, default, callbacks.Add); if (!repeat.Succeeded || callbacks.Count != 0 || calls.Count != 0) throw new InvalidOperationException("Archived adaptive Mixcloud playback repeated media transfers.");
    }
}
