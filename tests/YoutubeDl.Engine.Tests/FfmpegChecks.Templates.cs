using YoutubeDl.Engine;

internal static partial class FfmpegChecks
{
    private static async Task TemplateAudio(string ffmpeg)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.m4a");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.3", "-vn", "-c:a", "aac", source]);
            var result = await OutputTemplateChecks.AudioEngine(await File.ReadAllBytesAsync(source)).DownloadAsync(new NativeDownloadRequest
            {
                Url = new Uri("https://media.example/source.mp4"), OutputDirectory = directory, OutputTemplate = "%(ext)s/%(title)s [%(id)s].%(ext)s",
                AudioOnly = true, AudioFormat = "mp3", AudioQuality = "192K", FfmpegPath = ffmpeg, WriteInfoJson = true, Retries = 0
            }, null, null, default);
            var expected = Path.Combine(directory, "mp3", "Template fixture [video-id].mp3");
            if (!result.Succeeded || result.OutputPath != expected || !File.Exists(Path.ChangeExtension(expected, "info.json")))
                throw new InvalidOperationException("Converted output and sidecars did not use the final extension/folder.");
            await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", expected, "-f", "null", "-"]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
