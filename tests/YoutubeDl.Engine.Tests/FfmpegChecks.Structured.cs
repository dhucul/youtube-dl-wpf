using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class FfmpegChecks
{
    private static async Task StructuredMedia(string ffmpeg, string mode)
    {
        using var files = new Files(); var audio = mode == "audio";
        var first = Path.Combine(files.Directory, "first." + (audio ? "mp3" : "mp4")); var second = Path.Combine(files.Directory, "second.mp4");
        if (audio) await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=500:duration=2", "-c:a", "libmp3lame", first]);
        else
        {
            await Generate(first, "red", "64x48"); await Generate(second, "blue", "128x96");
        }
        var firstBytes = await File.ReadAllBytesAsync(first); var secondBytes = audio ? [] : await File.ReadAllBytesAsync(second);
        var node = new JsonObject { ["@context"] = "https://schema.org", ["@type"] = audio ? "AudioObject" : "VideoObject", ["@id"] = "#main", ["name"] = "Verified & structured media",
            ["description"] = "Literal metadata", ["duration"] = "PT2S", ["author"] = new JsonObject { ["@type"] = "Person", ["name"] = "Creator" } };
        node["caption"] = new JsonObject { ["@type"] = "MediaObject", ["contentUrl"] = "/captions.vtt", ["encodingFormat"] = "text/vtt", ["inLanguage"] = "en" };
        string json;
        if (mode == "encodings")
        {
            node["width"] = 1920; node["height"] = 1080;
            node["encoding"] = new JsonArray(new JsonObject { ["@type"] = "MediaObject", ["contentUrl"] = "https://cdn.example/low?token=A%2Bb&part=1", ["encodingFormat"] = "video/mp4", ["width"] = 64, ["height"] = 48 },
                new JsonObject { ["@type"] = "MediaObject", ["contentUrl"] = "https://cdn.example/high?token=a%2Bb&part=2", ["encodingFormat"] = "video/mp4", ["width"] = 128, ["height"] = 96 });
            json = node.ToJsonString();
        }
        else if (mode == "collection")
        {
            node["contentUrl"] = "https://cdn.example/first.mp4"; var other = node.DeepClone(); other["@id"] = "#second"; other["name"] = "Selected second video"; other["contentUrl"] = "https://cdn.example/second.mp4";
            json = new JsonArray(node, other).ToJsonString();
        }
        else
        {
            node["contentUrl"] = "https://cdn.example/download?token=A%2Bb&part=1"; node["encodingFormat"] = "audio/mpeg"; json = node.ToJsonString();
        }
        var calls = new List<Uri>(); var completed = new List<string>();
        const string caption = "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nA complete caption.\n";
        var engine = StructuredMediaChecks.Engine(StructuredMediaChecks.Html(json), calls, request => new(HttpStatusCode.OK)
        { Content = request.RequestUri!.AbsolutePath == "/captions.vtt" ? new StringContent(caption) : new ByteArrayContent(request.RequestUri.AbsolutePath is "/high" or "/second.mp4" ? secondBytes : firstBytes) });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = StructuredMediaChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, AddMetadata = true, KeepOriginal = true, WriteInfoJson = true, WriteSubs = true, SubLanguages = "en",
            MaximumHeight = mode == "encodings" ? 48 : null, PlaylistItems = mode == "collection" ? "2" : "", DownloadArchiveFile = archive };
        var result = await engine.DownloadAsync(request, null, null, default, completed.Add);
        var output = result.OutputPaths.FirstOrDefault(path => !Path.GetFileName(path).Contains(".original."));
        var expectedBytes = mode == "collection" ? secondBytes : firstBytes; var expectedSource = mode == "collection" ? second : first;
        if (!result.Succeeded || output is null || result.OutputPaths.Count != 2 || completed.Count != 2 || calls.Count != 3 || !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(expectedBytes)))
            throw new InvalidOperationException("Structured playback lost selected media, originals or callbacks.");
        if (mode == "encodings" && (calls[1].AbsolutePath != "/low" || calls[1].Query != "?token=A%2Bb&part=1") || mode == "collection" && calls[1].AbsolutePath != "/second.mp4")
            throw new InvalidOperationException("Structured selection fetched the wrong encoding or video.");
        var before = await Frames(expectedSource); var after = await Frames(output);
        if (!before.SequenceEqual(after)) throw new InvalidOperationException("Structured media processing changed source frame counts.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", output, "-map", "0", "-f", "null", "-"]);
        using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(output, ".info.json")));
        if (info.RootElement.GetProperty("title").GetString() != (mode == "collection" ? "Selected second video" : "Verified & structured media")) throw new InvalidOperationException("Structured title metadata changed.");
        if (await File.ReadAllTextAsync(Path.ChangeExtension(output, ".en.vtt")) != caption) throw new InvalidOperationException("A structured caption changed or was not saved.");
        if (mode == "collection" && info.RootElement.GetProperty("playlist_index").GetInt32() != 2) throw new InvalidOperationException("Structured playlist source positions changed.");
        calls.Clear(); completed.Clear(); var repeated = await engine.DownloadAsync(request, null, null, default, completed.Add);
        if (!repeated.Succeeded || !repeated.Skipped || calls.Count != 1 || completed.Count != 0) throw new InvalidOperationException("An archived structured media identity fetched its media again.");
        async Task Generate(string path, string color, string size) => await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=" + color + ":s=" + size + ":r=24:d=2",
            "-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", path]);
        async Task<string[]> Frames(string path)
        {
            using var document = JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"), ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return document.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_type").GetString() + ":" + stream.GetProperty("nb_read_frames").GetString()).ToArray();
        }
    }
}
