namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static void SetMovieClock(List<string> arguments, string output)
    {
        if (Path.GetExtension(output).ToLowerInvariant() is not (".mp4" or ".mov" or ".m4v" or ".m4a" or ".3gp")) return;
        // Automatic LCM-based movie clocks can overflow the MOV chapter sample's
        // 32-bit duration field. The movie clock does not change encoded stream
        // packets or their track clocks. Use millisecond chapter/header precision.
        arguments.AddRange(["-movie_timescale", "1000"]);
    }
}
