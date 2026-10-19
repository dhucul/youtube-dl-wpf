using System.Net;
using System.Text.Json;

internal static partial class FfmpegChecks
{
    private static async Task ImgurMedia(string ffmpeg, bool sound, bool incomplete = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=160x96:r=24:d=2" };
        if (sound) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=2"]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (sound) args.AddRange(["-c:a", "aac"]);
        args.Add(source); await Run(ffmpeg, args.ToArray());
        var bytes = await File.ReadAllBytesAsync(source); var data = ImgurChecks.Data(sound);
        data["media"]![1]!["size"] = bytes.Length;
        if (incomplete) data["media"]![1]!["metadata"]!["duration"] = 20;
        var calls = new List<Uri>(); var completed = new List<string>();
        var engine = ImgurChecks.Engine(calls, data, media: message =>
        {
            if (message.RequestUri!.AbsoluteUri != "https://i.imgur.com/Video02.mp4?token=A%2Bb&Case=C%2Fd") throw new InvalidOperationException("Imgur fetched an unselected item or changed the literal resource URL.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = ImgurChecks.Request(files.Directory) with { PlaylistItems = "2", FfmpegPath = ffmpeg, Container = "mp4", AddMetadata = true,
            KeepOriginal = true, WriteInfoJson = true, DownloadArchiveFile = archive };
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (incomplete)
        {
            if (result.Succeeded || completed.Count != 0 || File.Exists(archive) || Directory.EnumerateFiles(files.Directory, "imgur*").Any())
                throw new InvalidOperationException("Incomplete Imgur media was published or archived.");
            return;
        }
        if (!result.Succeeded || result.OutputPaths.Count != 2 || completed.Count != 2 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes)))
            throw new InvalidOperationException("Imgur did not preserve complete media and byte-identical originals.");
        foreach (var output in result.OutputPaths)
        {
            if (!string.Equals(await Frames(source), await Frames(output), StringComparison.Ordinal)) throw new InvalidOperationException("Imgur processing changed video/audio frame counts.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0:v:0", "-map", "0:a?", "-f", "null", "-"]);
        }
        var infoPath = Directory.EnumerateFiles(files.Directory, "*.info.json").Single();
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(infoPath));
        if (info.RootElement.GetProperty("playlist_index").GetInt32() != 2 || info.RootElement.GetProperty("playlist_autonumber").GetInt32() != 1 ||
            (await File.ReadAllTextAsync(archive)).Trim() != "imgur Video02") throw new InvalidOperationException("Imgur lost album source positions or per-video archive identity.");
        calls.Clear(); completed.Clear(); var again = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!again.Succeeded || !again.Skipped || calls.Count != 1 || completed.Count != 0) throw new InvalidOperationException("Imgur fetched archived album media again.");
        async Task<string> Frames(string path)
        {
            using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return string.Join(';', json.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()));
        }
    }
}
