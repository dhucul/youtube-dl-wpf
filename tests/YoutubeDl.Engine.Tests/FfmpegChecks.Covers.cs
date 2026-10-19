using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> CoverChecks =>
    [
        ("Unsupported cover containers fail before media transfer or publication", CoverContainers),
        ("Automatic cover remux predicts the existing output for no-overwrite requests", CoverSkip)
    ];
    private static async Task CoverContainers()
    {
        using var files = new Files();
        foreach (var container in new[] { "mov", "wav", "webm", "avi", "flv" })
        {
            var previous = Path.Combine(files.Directory, "result." + container);
            await File.WriteAllTextAsync(previous, "existing result");
            var transfers = 0;
            try
            {
                await CoverEngine([], [], "mp4", false, _ => transfers++).DownloadAsync(new NativeDownloadRequest
                { Url = new("https://media.example/source.mp4"), OutputDirectory = files.Directory, FileName = "result", Container = container, EmbedThumbnail = true }, null, null, default);
                throw new InvalidOperationException("An unsupported cover container was accepted.");
            }
            catch (NotSupportedException ex) when (ex.Message.StartsWith("Cover embedding")) { }
            if (transfers != 0 || File.ReadAllText(previous) != "existing result") throw new InvalidOperationException("Unsupported cover processing transferred media or replaced a file.");
        }
    }
    private static async Task CoverSkip()
    {
        using var files = new Files();
        var path = Path.Combine(files.Directory, "result.mkv"); await File.WriteAllTextAsync(path, "existing result");
        var transfers = 0;
        var result = await CoverEngine([], [], "webm", false, _ => transfers++).DownloadAsync(new NativeDownloadRequest
        { Url = new("https://media.example/source.webm"), OutputDirectory = files.Directory, FileName = "result", EmbedThumbnail = true, NoOverwrites = true }, null, null, default);
        if (!result.Skipped || transfers != 0 || File.ReadAllText(path) != "existing result") throw new InvalidOperationException("Cover remux missed the existing Matroska output.");
    }
    private static async Task CoverMedia(string ffmpeg, string container)
    {
        using var files = new Files();
        var audioOnly = container is "mp3" or "m4a" or "flac" or "opus" or "ogg" or "mka";
        var source = Path.Combine(files.Directory, "source." + container);
        var image = Path.Combine(files.Directory, "image." + (container is "mkv" or "flac" ? "webp" : container is "mov" or "mka" ? "jpg" : "png"));
        var largeImage = container == "opus";
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", largeImage ? "testsrc2=s=256x256,noise=alls=50:allf=u:all_seed=7" : "color=c=yellow:s=64x48:d=0.04", "-frames:v", "1", "-update", "1", image]);
        if (largeImage && new FileInfo(image).Length < 32768) throw new InvalidOperationException("The cover fixture must exceed the command-line size limit.");
        var generate = new List<string> { "-nostdin", "-v", "error", "-y" };
        if (!audioOnly)
        {
            generate.AddRange(["-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.4", "-f", "lavfi", "-i", "color=c=red:s=96x64:r=10:d=0.4"]);
        }
        generate.AddRange(["-f", "lavfi", "-i", "sine=frequency=700:sample_rate=48000:duration=0.4"]);
        if (!audioOnly) generate.AddRange(["-map", "0:v:0", "-map", "1:v:0", "-map", "2:a:0", "-c:v", container == "webm" ? "libvpx-vp9" : "libx264", "-pix_fmt", "yuv420p"]);
        generate.AddRange(["-c:a", container switch { "mp3" => "libmp3lame", "flac" => "flac", "opus" or "webm" => "libopus", "ogg" => "libvorbis", _ => "aac" }, "-metadata", "album=Preserved album"]);
        if (container == "mkv")
        {
            var attachment = Path.Combine(files.Directory, "notes.txt"); await File.WriteAllTextAsync(attachment, "Preserve this attachment.");
            generate.AddRange(["-attach", attachment, "-metadata:s:t:0", "mimetype=text/plain", "-metadata:s:t:0", "filename=notes.txt"]);
        }
        generate.Add(source);
        await Run(ffmpeg, generate);
        var mediaBytes = await File.ReadAllBytesAsync(source); var imageBytes = await File.ReadAllBytesAsync(image);
        var request = new NativeDownloadRequest { Url = new("https://media.example/source." + container), OutputDirectory = files.Directory,
            FileName = "result", EmbedThumbnail = true, WriteThumbnail = container == "mp3", WriteInfoJson = true, AddMetadata = true,
            EmbedSubs = !audioOnly, SubLanguages = "en", EmbedChapters = !audioOnly, FfmpegPath = ffmpeg, Retries = 0 };
        var result = await CoverEngine(mediaBytes, imageBytes, container, audioOnly).DownloadAsync(request, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException("Cover embedding failed.");
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        var before = await Read(source); var after = await Read(result.OutputPath);
        var streams = after.GetProperty("streams").EnumerateArray().ToArray();
        var cover = streams.Where(IsCover).ToArray();
        if (container == "mkv" && !streams.Any(stream => stream.TryGetProperty("tags", out var attachmentTags) && HasTag(attachmentTags, "filename", "notes.txt")))
            throw new InvalidOperationException("Cover embedding removed an unrelated attachment.");
        if (cover.Length != 1 || cover[0].GetProperty("width").GetInt32() != (largeImage ? 256 : 64) || cover[0].GetProperty("height").GetInt32() != (largeImage ? 256 : 48))
            throw new InvalidOperationException("The embedded cover is missing, duplicated, or has the wrong dimensions.");
        var mediaBefore = before.GetProperty("streams").EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() is "video" or "audio").ToArray();
        var mediaAfter = streams.Where(stream => !IsCover(stream) && stream.GetProperty("codec_type").GetString() is "video" or "audio").ToArray();
        if (mediaBefore.Length != mediaAfter.Length || !mediaBefore.Zip(mediaAfter).All(pair =>
            pair.First.GetProperty("codec_name").GetString() == pair.Second.GetProperty("codec_name").GetString() &&
            pair.First.GetProperty("nb_read_frames").GetString() == pair.Second.GetProperty("nb_read_frames").GetString()))
            throw new InvalidOperationException("Cover embedding changed the media tracks or frame counts.");
        var tags = after.GetProperty("format").TryGetProperty("tags", out var globalTags) && HasTag(globalTags, "title", "Native cover fixture")
            ? globalTags : mediaAfter[0].GetProperty("tags");
        if (!HasTag(tags, "title", "Native cover fixture") || !HasTag(tags, "album", "Preserved album")) throw new InvalidOperationException("Cover embedding lost media metadata.");
        if (!audioOnly)
        {
            if (after.GetProperty("chapters").GetArrayLength() != 2 || streams.Count(stream => stream.GetProperty("codec_type").GetString() == "subtitle") != 1)
                throw new InvalidOperationException("Adding cover art lost chapters or captions.");
            var captions = Path.Combine(files.Directory, "captions.vtt");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath, "-map", "0:s:0", captions]);
            if (!File.ReadAllText(captions).Contains("Native caption & punctuation.")) throw new InvalidOperationException("Cover art changed the caption text.");
        }
        if (File.Exists(Path.Combine(files.Directory, "result.png")) != request.WriteThumbnail || Directory.EnumerateDirectories(files.Directory, ".native-download-*").Any())
            throw new InvalidOperationException("Cover-only processing saved an unwanted sidecar or left its workspace.");
        var extracted = Path.Combine(files.Directory, "extracted.png");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", result.OutputPath, "-map", "0:" + cover[0].GetProperty("index").GetInt32(), "-frames:v", "1", "-update", "1", extracted]);
        var originalPixels = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", image, "-map", "0:v:0", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "hash", "-"]);
        var extractedPixels = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", extracted, "-map", "0:v:0", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "hash", "-"]);
        if (originalPixels != extractedPixels) throw new InvalidOperationException("The embedded cover pixels changed.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0:V?", "-map", "0:a?", "-f", "null", "-"]);

        // A second embedding must replace the existing cover without losing media or accumulating images.
        var second = Path.Combine(files.Directory, "replacement" + Path.GetExtension(result.OutputPath));
        await FfmpegTool.EmbedAsync(ffmpeg, result.OutputPath, second, [], image, null, default);
        var replacement = await Read(second);
        if (replacement.GetProperty("streams").EnumerateArray().Count(IsCover) != 1 ||
            replacement.GetProperty("streams").GetArrayLength() != streams.Length)
            throw new InvalidOperationException("Replacing the cover duplicated it or lost another stream.");

        async Task<JsonElement> Read(string path)
        {
            using var document = JsonDocument.Parse(await Run(probe, ["-v", "error", "-count_frames", "-show_streams", "-show_format", "-show_chapters", "-of", "json", path]));
            return document.RootElement.Clone();
        }
    }

    private static async Task CoverFailure(string ffmpeg)
    {
        using var files = new Files();
        var source = Path.Combine(files.Directory, "source.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.4", "-an", "-c:v", "libx264", source]);
        var previous = Path.Combine(files.Directory, "result.mp4"); var thumbnail = Path.Combine(files.Directory, "result.png"); var archive = Path.Combine(files.Directory, "archive.txt");
        await File.WriteAllTextAsync(previous, "prior media"); await File.WriteAllTextAsync(thumbnail, "prior cover"); await File.WriteAllTextAsync(archive, "fixture previous\n");
        var completed = 0;
        var request = new NativeDownloadRequest { Url = new("https://media.example/source.mp4"), OutputDirectory = files.Directory, FileName = "result",
            EmbedThumbnail = true, WriteThumbnail = true, FfmpegPath = ffmpeg, DownloadArchiveFile = archive, Retries = 0 };
        try
        {
            await CoverEngine(await File.ReadAllBytesAsync(source), [137, 80, 78, 71, 13, 10, 26, 10], "mp4", false).DownloadAsync(request, null, null, default, _ => completed++);
            throw new InvalidOperationException("A malformed image was published.");
        }
        catch (InvalidDataException) { }
        using (var canceled = new CancellationTokenSource())
        {
            try
            {
                await CoverEngine(await File.ReadAllBytesAsync(source), [137, 80, 78, 71, 13, 10, 26, 10], "mp4", false,
                    url => { if (url.AbsolutePath == "/image") canceled.Cancel(); }).DownloadAsync(request, null, null, canceled.Token, _ => completed++);
                throw new InvalidOperationException("Cover cancellation was ignored.");
            }
            catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        }
        if (completed != 0 || File.ReadAllText(previous) != "prior media" || File.ReadAllText(thumbnail) != "prior cover" || File.ReadAllText(archive) != "fixture previous\n")
            throw new InvalidOperationException("Failed cover processing changed published output or archive state.");
    }

    private static bool IsCover(JsonElement stream) => stream.GetProperty("disposition").GetProperty("attached_pic").GetInt32() == 1;
    private static bool HasTag(JsonElement tags, string name, string value) => tags.EnumerateObject().Any(tag => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tag.Value.GetString() == value);
    private static NativeEngine CoverEngine(byte[] media, byte[] image, string container, bool audioOnly, Action<Uri>? onRequest = null) => new([new CoverProvider(container, audioOnly)], _ => new MediaHttp(new Handler(request =>
    {
        onRequest?.Invoke(request.RequestUri!);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.StartsWith("/source.") ? new ByteArrayContent(media) :
            request.RequestUri.AbsolutePath == "/image" ? new ByteArrayContent(image) : new StringContent(CaptionChecks.Caption) };
    })));
    private sealed class CoverProvider(string container, bool audioOnly) : IMediaProvider
    {
        public MediaProviderInfo Info => new("cover-fixture", "Cover fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        {
            Id = "cover-fixture", Extractor = "fixture", Title = "Native cover fixture", Uploader = "Artist", ThumbnailUrl = "https://media.example/image",
            Chapters = [new() { StartSeconds = 0, EndSeconds = 0.1, Title = "Opening" }, new() { StartSeconds = 0.1, EndSeconds = 0.4, Title = "Ending" }],
            Subtitles = [new() { Url = new("https://media.example/en.vtt"), Language = "en", Extension = "vtt" }],
            Media = [new() { Id = "media", Url = request.Url, Extension = container, Kind = MediaKind.Progressive, Source = "fixture", HasAudio = true, HasVideo = !audioOnly,
                MimeType = audioOnly ? "audio/" + container : "video/" + container }]
        });
    }
}
