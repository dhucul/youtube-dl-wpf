using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> AudioChecks =>
    [
        ("Invalid native audio formats and quality values fail before network access", InvalidAudioOptions)
    ];
    private static async Task InvalidAudioOptions()
    {
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Invalid audio requests must not create HTTP sessions."));
        foreach (var request in new[] { AudioRequest(Path.GetTempPath(), "invalid"), AudioRequest(Path.GetTempPath(), "mp3") with { AudioQuality = "NaN" },
            AudioRequest(Path.GetTempPath(), "opus") with { AudioQuality = "-1" }, AudioRequest(Path.GetTempPath(), "aac") with { AudioQuality = "10" },
            AudioRequest(Path.GetTempPath(), "mp3") with { AudioQuality = "192" }, AudioRequest(Path.GetTempPath(), "mp3") with { AudioQuality = "0K" } })
        {
            try { await engine.InspectAsync(request, default); throw new InvalidOperationException("Invalid audio options were accepted."); }
            catch (ArgumentException) { }
        }
    }

    private static async Task AutomaticAudio(string ffmpeg, string codec, bool standardContainer = false)
    {
        using var files = new Files();
        var inputExtension = standardContainer ? codec switch { "aac" => "mp4", "libopus" => "opus", "libvorbis" => "ogg", _ => "mkv" } : "mkv";
        var source = Path.Combine(files.Directory, "source." + inputExtension);
        var generate = new List<string> { "-nostdin", "-v", "error", "-y" };
        var video = inputExtension is "mp4" or "mkv";
        if (video) generate.AddRange(["-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.4"]);
        generate.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.4"]);
        if (video) generate.AddRange(["-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        generate.AddRange(["-c:a", codec, source]);
        await Run(ffmpeg, generate);
        var bytes = await File.ReadAllBytesAsync(source);
        var result = await AudioEngine(bytes).DownloadAsync(AudioRequest(files.Directory, "best", ffmpeg) with { OutputTemplate = "%(ext)s/%(title)s.%(ext)s", WriteInfoJson = true }, null, null, default);
        var extension = codec switch { "aac" => "m4a", "libmp3lame" => "mp3", "libopus" => "opus", "libvorbis" => "ogg", "flac" => "flac", "pcm_s24le" => "wav", _ => "mka" };
        if (!standardContainer && codec is "aac" or "libopus" or "libvorbis") extension = "mka";
        var expected = Path.Combine(files.Directory, extension, "Audio fixture." + extension);
        if (!result.Succeeded || result.OutputPath != expected || !File.Exists(Path.ChangeExtension(expected, ".info.json"))) throw new InvalidOperationException("Automatic audio used an incorrect output extension or sidecar location.");
        var inputPackets = await PacketHashes(ffmpeg, source); var outputPackets = await PacketHashes(ffmpeg, expected);
        if (codec != "pcm_s24le" && !inputPackets.SequenceEqual(outputPackets)) throw new InvalidOperationException("Best audio re-encoded or lost source audio packets.");
        var before = await AudioHash(ffmpeg, source); var after = await AudioHash(ffmpeg, expected);
        if (before != after) throw new InvalidOperationException("Automatic audio changed decoded samples.");
        var streams = await AudioStreams(ffmpeg, expected);
        if (streams.Length != 1 || streams[0].GetProperty("codec_type").GetString() != "audio") throw new InvalidOperationException("Audio extraction retained video or lost its audio track.");
    }

    private static async Task DefaultAudio(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=48000:duration=0.4", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000:duration=0.4",
            "-map", "0:a", "-map", "1:a", "-ac:a:0", "2", "-c:a", "flac", "-disposition:a:0", "0", "-disposition:a:1", "default", source]);
        var result = await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(AudioRequest(files.Directory, "best", ffmpeg), null, null, default);
        if (!result.Succeeded || await AudioHash(ffmpeg, source, "0:a:1") != await AudioHash(ffmpeg, result.OutputPath!))
            throw new InvalidOperationException("Automatic extraction did not choose the source's default audio track.");
    }

    private static async Task AudioConversion(string ffmpeg, string format, string quality)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=color=pink:sample_rate=48000:duration=0.6:seed=7", "-ac", "2", "-c:a", "pcm_s24le", source]);
        var result = await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(AudioRequest(files.Directory, format, ffmpeg) with { AudioQuality = quality, AddMetadata = format == "aac" }, null, null, default);
        var expectedCodec = format switch { "m4a" or "aac" => "aac", "vorbis" => "vorbis", "wav" => "pcm_s24le", _ => format };
        var extension = format switch { "vorbis" => "ogg", "alac" => "m4a", _ => format };
        if (!result.Succeeded || Path.GetExtension(result.OutputPath!) != "." + extension) throw new InvalidOperationException("Explicit audio conversion used the wrong container.");
        var streams = await AudioStreams(ffmpeg, result.OutputPath!);
        if (streams.Length != 1 || streams[0].GetProperty("codec_name").GetString() != expectedCodec) throw new InvalidOperationException("Explicit audio conversion used the wrong codec.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath!, "-f", "null", "-"]);
        if (format is "wav" or "flac" or "alac" && await AudioHash(ffmpeg, source) != await AudioHash(ffmpeg, result.OutputPath!)) throw new InvalidOperationException("Lossless audio conversion changed 24-bit samples.");
        if (format == "opus" && quality == "128K")
        {
            using var packets = JsonDocument.Parse(await Run(Probe(ffmpeg), ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_entries", "packet=size", "-of", "json", result.OutputPath!]));
            if (packets.RootElement.GetProperty("packets").EnumerateArray().Any(packet => packet.GetProperty("size").GetString() != "320"))
                throw new InvalidOperationException("The requested 128K Opus bitrate did not produce fixed 20ms packets.");
        }
        if (format == "aac")
        {
            using var tags = JsonDocument.Parse(await Run(Probe(ffmpeg), ["-v", "error", "-show_entries", "format_tags=title", "-of", "json", result.OutputPath!]));
            if (tags.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString() != "Audio fixture")
                throw new InvalidOperationException("Raw AAC did not retain requested ID3 metadata.");
        }
    }
    private static async Task AudioQualityDirection(string ffmpeg, string format)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=color=pink:sample_rate=48000:duration=0.6:seed=9", "-ac", "2", "-c:a", "flac", source]);
        var engine = AudioEngine(await File.ReadAllBytesAsync(source));
        var best = await engine.DownloadAsync(AudioRequest(files.Directory, format, ffmpeg) with { FileName = "highest", AudioQuality = "0" }, null, null, default);
        var worst = await engine.DownloadAsync(AudioRequest(files.Directory, format, ffmpeg) with { FileName = "lowest", AudioQuality = "9" }, null, null, default);
        if (!best.Succeeded || !worst.Succeeded || new FileInfo(best.OutputPath!).Length <= new FileInfo(worst.OutputPath!).Length)
            throw new InvalidOperationException("The encoder reversed or ignored the highest-to-lowest quality scale.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", best.OutputPath!, "-f", "null", "-"]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", worst.OutputPath!, "-f", "null", "-"]);
    }
    private static async Task AudioNoOverwrite(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var existing = Path.Combine(files.Directory, "mka", "Audio fixture.mka"); Directory.CreateDirectory(Path.GetDirectoryName(existing)!); await File.WriteAllTextAsync(existing, "previous output");
        var completed = 0;
        var result = await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(AudioRequest(files.Directory, "best", ffmpeg) with
        { OutputTemplate = "%(ext)s/%(title)s.%(ext)s", NoOverwrites = true, WriteInfoJson = true }, null, null, default, _ => completed++);
        if (!result.Skipped || result.OutputPath != existing || completed != 0 || File.ReadAllText(existing) != "previous output" || File.Exists(Path.ChangeExtension(existing, ".info.json")) ||
            Directory.EnumerateDirectories(files.Directory, ".native-download-*", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Late automatic-format skipping changed an existing output or left a download workspace.");
    }
    private static async Task AudioMissing(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", source]);
        var prior = Path.Combine(files.Directory, "audio.mp3"); var archive = Path.Combine(files.Directory, "archive.txt"); await File.WriteAllTextAsync(prior, "previous output"); await File.WriteAllTextAsync(archive, "fixture prior\n");
        try
        {
            await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(AudioRequest(files.Directory, "mp3", ffmpeg) with { DownloadArchiveFile = archive }, null, null, default);
            throw new InvalidDataException("A video-only source produced a completed audio job.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no audio track")) { }
        if (File.ReadAllText(prior) != "previous output" || File.ReadAllText(archive) != "fixture prior\n") throw new InvalidOperationException("Missing audio changed a published file or archive.");
    }
    private static async Task AudioArchiveCollision(string ffmpeg)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mkv");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-c:a", "aac", source]);
        var archive = Path.Combine(files.Directory, "audio.mka"); await File.WriteAllTextAsync(archive, "fixture prior\n");
        try
        {
            await AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(AudioRequest(files.Directory, "best", ffmpeg) with { NoOverwrites = true, DownloadArchiveFile = archive }, null, null, default);
            throw new InvalidOperationException("The archive file was mistaken for existing audio.");
        }
        catch (IOException ex) when (ex.Message.Contains("archive")) { }
        if (File.ReadAllText(archive) != "fixture prior\n") throw new InvalidOperationException("An audio-name collision changed the archive.");
    }
    private static string Probe(string ffmpeg) => Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
    private static async Task<JsonElement[]> AudioStreams(string ffmpeg, string path)
    {
        using var document = JsonDocument.Parse(await Run(Probe(ffmpeg), ["-v", "error", "-show_streams", "-of", "json", path]));
        return document.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.Clone()).ToArray();
    }
    private static async Task<string[]> PacketHashes(string ffmpeg, string path)
    {
        using var document = JsonDocument.Parse(await Run(Probe(ffmpeg), ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_entries", "packet=data_hash", "-show_data_hash", "sha256", "-of", "json", path]));
        return document.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();
    }
    private static Task<string> AudioHash(string ffmpeg, string path, string stream = "0:a:0") => Run(ffmpeg, ["-nostdin", "-v", "error", "-i", path, "-map", stream, "-c:a", "pcm_f64le", "-f", "hash", "-"]);
    private static NativeDownloadRequest AudioRequest(string directory, string format, string? ffmpeg = null) => new() { Url = new("https://media.example/source.mkv"), OutputDirectory = directory,
        FileName = "audio", AudioOnly = true, AudioFormat = format, FfmpegPath = ffmpeg, Retries = 0 };
    private static NativeEngine AudioEngine(byte[] bytes) => new([new AudioProvider()], _ => new MediaHttp(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
    private sealed class AudioProvider : IMediaProvider
    {
        public MediaProviderInfo Info => new("audio-fixture", "Audio fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        { Id = "audio-fixture", Title = "Audio fixture", Extractor = "fixture", Media = [new() { Url = request.Url, Extension = "mkv", Kind = MediaKind.Progressive,
            HasVideo = true, HasAudio = true, AudioCodec = "declared-codec-must-not-override-probe", Source = "fixture" }] });
    }
}
