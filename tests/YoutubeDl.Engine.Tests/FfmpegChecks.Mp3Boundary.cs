using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    private static async Task Mp3Boundary(string ffmpeg, string mode, bool gapless = false)
    {
        using var files = new Files(); var source = Path.Combine(files.Directory, "source.mp3"); var output = Path.Combine(files.Directory, "processed.mp3");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=650:sample_rate=44100:duration=2", "-c:a", "libmp3lame", "-write_xing", gapless ? "1" : "0", source]);
        var bytes = await File.ReadAllBytesAsync(source);
        if (mode == "remux") await FfmpegTool.RemuxAsync(ffmpeg, source, output, default);
        else if (mode == "copy") await FfmpegTool.ExtractAudioAsync(ffmpeg, source, output, "copy", "5", default);
        else await FfmpegTool.EmbedAsync(ffmpeg, source, output, [], null, new Dictionary<string, string> { ["title"] = "Preserved audio samples" }, default);
        foreach (var path in new[] { source, output }) await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:a", "-f", "null", "-"], rejectDiagnostics: true);
        var one = await Run(ffmpeg, ["-v", "error", "-i", source, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true);
        var two = await Run(ffmpeg, ["-v", "error", "-i", output, "-map", "0:a", "-c:a", "pcm_s16le", "-f", "hash", "-hash", "sha256", "-"], rejectDiagnostics: true);
        if (one != two) throw new InvalidOperationException("Copying an MP3 changed decoded boundary samples.");
        if (!(await File.ReadAllBytesAsync(source)).SequenceEqual(bytes)) throw new InvalidOperationException("MP3 boundary preservation changed the original file.");
    }
}
