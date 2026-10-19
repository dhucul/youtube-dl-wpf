using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    private static async Task AllThumbnailMedia(string ffmpeg, string preferredType)
    {
        using var files = new Files(); var media = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=green:s=128x96:r=10:d=0.4", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", media]);
        var preferred = Path.Combine(files.Directory, "preferred." + preferredType); var other = Path.Combine(files.Directory, "other.png");
        var imageArgs = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=64x48:r=5:d=0.4" };
        if (preferredType == "avif") imageArgs.AddRange(["-frames:v", "1", "-c:v", "libaom-av1", "-cpu-used", "8", "-still-picture", "1"]);
        else if (preferredType == "bmp") imageArgs.AddRange(["-frames:v", "1", "-update", "1"]);
        imageArgs.Add(preferred); await Run(ffmpeg, imageArgs);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=32x24:d=0.04", "-frames:v", "1", "-update", "1", other]);
        var assets = new Dictionary<string, byte[]> { ["/source.mp4"] = await File.ReadAllBytesAsync(media), ["/preferred"] = await File.ReadAllBytesAsync(preferred), ["/other"] = await File.ReadAllBytesAsync(other) };
        var requests = new List<string>();
        var page = new PageMetadata { Id = "art", Title = "All art", ThumbnailUrl = "https://media.example/preferred", Thumbnails = [
            new() { Id = "chosen", Url = new("https://media.example/preferred"), Width = 64, Height = 48 }, new() { Id = "other", Url = new("https://media.example/other"), Width = 32, Height = 24 }],
            Media = [new() { Url = new("https://media.example/source.mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }] };
        var engine = new NativeEngine([new ThumbnailProvider(page)], _ => new MediaHttp(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; requests.Add(path); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(assets[path]) };
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://media.example/watch"), OutputDirectory = files.Directory, FileName = "result",
            WriteAllThumbnails = true, EmbedThumbnail = true, FfmpegPath = ffmpeg, Retries = 0 }, null, null, default);
        if (!result.Succeeded || requests.Count != 3) throw new InvalidOperationException("All-thumbnail processing failed or refetched the preferred image.");
        if (!File.ReadAllBytes(Path.Combine(files.Directory, "result.chosen." + preferredType)).AsSpan().SequenceEqual(assets["/preferred"]) ||
            !File.ReadAllBytes(Path.Combine(files.Directory, "result.other.png")).AsSpan().SequenceEqual(assets["/other"]))
            throw new InvalidOperationException("Embedding changed the saved original thumbnail variants.");
        using var json = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=index,codec_name,codec_type,nb_read_frames,width,height:stream_disposition=attached_pic", "-of", "json", result.OutputPath!]));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var cover = streams.Single(stream => stream.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 1);
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video" && stream.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 0);
        if (video.GetProperty("nb_read_frames").GetString() != "4" || cover.GetProperty("width").GetInt32() != 64 || cover.GetProperty("height").GetInt32() != 48 || cover.GetProperty("codec_name").GetString() != "png")
            throw new InvalidOperationException("The preferred image conversion changed media frames or chose another thumbnail.");
        var inputPixels = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", preferred, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "hash", "-hash", "SHA256", "-"]);
        var outputPixels = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath!, "-map", "0:" + cover.GetProperty("index").GetInt32(), "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "hash", "-hash", "SHA256", "-"]);
        if (inputPixels.Trim() != outputPixels.Trim()) throw new InvalidOperationException("The embedded cover changed the preferred first-frame pixels.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:V:0", "-map", "0:a:0", "-f", "null", "-"]);
    }
    private sealed class ThumbnailProvider(PageMetadata page) : IMediaProvider
    { public MediaProviderInfo Info { get; } = new("thumbnail-fixture", "Thumbnail fixture", 1000); public bool CanHandle(Uri url) => true; public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(page); }
}
