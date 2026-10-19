using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;

internal static partial class FfmpegChecks
{
    private static async Task VimeoMedia(string ffmpeg, bool suppliedLink)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.6", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.6", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        var bytes = await File.ReadAllBytesAsync(source); var sourceUrl = new Uri("https://cdn.example/source.mp4?signature=fixture%2Bvalue");
        var config = JsonSerializer.Serialize(new { view = 1, video = new { id = 12345, title = "Native Vimeo fixture", duration = 0.6, owner = new { name = "Fixture" } },
            request = new { files = new { progressive = new[] { new { quality = "96p", url = sourceUrl.AbsoluteUri, mime = "video/mp4; codecs=\"avc1.42c00a,mp4a.40.2\"", width = 128, height = 96, fps = 10 } },
                hls = new { default_cdn = "selected", cdns = new { selected = new { url = "https://cdn.example/v2/playlist/drm/cbcs,protected/playlist.m3u8" } } } },
                text_tracks = new[] { new { lang = "en", url = "https://cdn.example/en.vtt" } } } });
        var calls = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/video/12345/config") return new(HttpStatusCode.OK) { Content = new StringContent(config) };
            if (request.RequestUri.AbsolutePath == "/en.vtt") return new(HttpStatusCode.OK) { Content = new StringContent("WEBVTT\n\n00:00:00.100 --> 00:00:00.500\nNative caption\n") };
            if (request.RequestUri.AbsolutePath.Contains("/drm/")) throw new InvalidOperationException("A protected manifest was fetched in place of clear media.");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            if (request.RequestUri.Host == "player.vimeo.com") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            return response;
        })));
        var result = await engine.DownloadAsync(new NativeDownloadRequest { Url = new(suppliedLink ? "https://player.vimeo.com/progressive_redirect/playback/12345/container/fixture/asset?signature=provided%2Bvalue" : "https://vimeo.com/12345"),
            OutputDirectory = files.Directory, FileName = "vimeo-output", FfmpegPath = ffmpeg, Retries = 0, AddMetadata = true, WriteSubs = !suppliedLink, EmbedSubs = !suppliedLink, SubLanguages = "en",
            MaximumHeight = suppliedLink ? null : 96, MaximumFrameRate = suppliedLink ? 0 : 10, VideoCodec = suppliedLink ? "any" : "h264" }, null, null, default);
        if (!result.Succeeded) throw new InvalidOperationException("Native Vimeo media processing failed.");
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        using var before = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", source]));
        using var after = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames:format_tags=title", "-of", "json", result.OutputPath!]));
        foreach (var kind in new[] { "video", "audio" })
        {
            string Frames(JsonDocument doc) => doc.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == kind).GetProperty("nb_read_frames").GetString()!;
            if (Frames(before) != Frames(after)) throw new InvalidOperationException("Native Vimeo lost complete media frames.");
        }
        if (!suppliedLink)
        {
            var extracted = Path.Combine(files.Directory, "extracted.vtt");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath!, "-map", "0:s:0", extracted]);
            if (!File.ReadAllText(extracted).Contains("Native caption") || after.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString() != "Native Vimeo fixture")
                throw new InvalidOperationException("Vimeo captions or metadata were not retained.");
        }
        if (calls.Count != (suppliedLink ? 2 : 3)) throw new InvalidOperationException("Unexpected Vimeo media requests.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-"]);
    }
}
