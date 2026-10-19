using System.Text.Json;
using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task BilibiliProgressive(string ffmpeg, string mode)
    {
        using var files = new Files(); var extension = mode == "flv" ? "flv" : "mp4";
        var source = Path.Combine(files.Directory, "input." + extension);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=128x96:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var calls = new List<Uri>(); var completed = new List<string>();
        var archive = Path.Combine(files.Directory, "archive.txt");
        var engine = BilibiliChecks.Engine(calls, playback: cid =>
        {
            var data = BilibiliChecks.Progressive(cid); var file = data["data"]!["durl"]![0]!;
            file["url"] = "https://upos-test.bilivideo.com/video/" + cid + "." + extension + "?token=a%2Bb"; file["size"] = bytes.Length;
            return data;
        }, media: _ => bytes, observe: request =>
        {
            if (request.Headers.Referrer?.AbsoluteUri != "https://embedding.example/page" || request.Headers.UserAgent.ToString() != "NativeTest/1.0")
                throw new InvalidOperationException("Caller headers were lost during verified progressive download.");
        });
        var request = BilibiliChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, WriteInfoJson = true, DownloadArchiveFile = archive,
            UserAgent = "NativeTest/1.0", Referer = "https://embedding.example/page" };
        if (mode == "playlist") request = request with { Url = new("https://www.bilibili.com/video/" + BilibiliChecks.Bvid), PlaylistItems = "2" };
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPaths.Count != 1 || completed.Count != 1 || !File.ReadAllBytes(result.OutputPaths[0]).SequenceEqual(bytes))
            throw new InvalidOperationException("Verified progressive output did not retain the exact source bytes.");
        var media = calls.Where(url => url.Host != "api.bilibili.com").ToArray();
        var cidText = mode == "playlist" ? "1445375" : "1445374";
        if (media.Length != 1 || !media[0].AbsolutePath.Contains(cidText) || media[0].Query != "?token=a%2Bb" || calls.Count != (mode == "playlist" ? 4 : 3))
            throw new InvalidOperationException("A progressive job fetched the wrong part or changed its access query.");
        if ((await File.ReadAllTextAsync(archive)).Trim() != "bilibili " + BilibiliChecks.Bvid + "_" + cidText)
            throw new InvalidOperationException("Verified publication did not write the selected part archive identity.");
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(result.OutputPaths[0], ".info.json")));
        if (info.RootElement.GetProperty("id").GetString() != BilibiliChecks.Bvid + "_" + cidText || mode == "playlist" &&
            (info.RootElement.GetProperty("playlist_index").GetInt32() != 2 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != 1))
            throw new InvalidOperationException("Verified publication changed its part metadata or playlist positions.");
        calls.Clear(); completed.Clear();
        var repeated = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!repeated.Succeeded || !repeated.Skipped || completed.Count != 0 || calls.Any(url => url.Host != "api.bilibili.com"))
            throw new InvalidOperationException("An archived verified part was downloaded again.");
    }
}
