using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

internal static class SubtitleFormats
{
    internal static bool CanSave(string extension) => extension is "vtt" or "srt" or "ass" or "ssa" or "ttml" or "dfxp" or "lrc";
    internal static string Normalize(string extension) => extension.Trim().ToLowerInvariant();

    internal static string[] Preferences(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return ["best"];
        if (expression.Length > 256) throw new ArgumentException("The subtitle format preference is too long.");
        var formats = expression.Split('/', StringSplitOptions.TrimEntries).Select(Normalize).ToArray();
        if (formats.Length > 16 || formats.Any(format => !Regex.IsMatch(format, @"\A[a-z0-9]{1,16}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
            throw new ArgumentException("Use subtitle format preferences such as srt/vtt/best.");
        return formats;
    }

    internal static string ConversionTarget(string value)
    {
        var target = Normalize(value);
        if (target.Length > 0 && target is not ("srt" or "vtt" or "ass" or "lrc"))
            throw new NotSupportedException("Native subtitle conversion supports srt, vtt, ass and lrc.");
        return target;
    }

    internal static SubtitleTrack Choose(IEnumerable<SubtitleTrack> candidates, IReadOnlyList<string> preferences, string language)
    {
        var offered = candidates.ToArray();
        foreach (var preference in preferences)
        {
            var match = preference == "best"
                ? offered.FirstOrDefault(track => CanSave(Normalize(track.Extension)))
                : offered.FirstOrDefault(track => Normalize(track.Extension) == preference);
            if (match is not null) return match;
        }
        throw new NotSupportedException("No offered subtitle format for " + language + " matches " + string.Join('/', preferences) + ".");
    }

    internal static void ValidateSelection(NativeDownloadRequest request, IReadOnlyList<SubtitleTrack> tracks)
    {
        var target = ConversionTarget(request.ConvertSubs);
        foreach (var track in tracks)
        {
            var source = Normalize(track.Extension);
            if (!CanSave(source)) throw new NotSupportedException("Native caption saving is pending for: " + source);
            if (target.Length == 0 || target == source) continue;
            if (source is "ttml" or "dfxp" && target is "srt" or "vtt") continue;
            if (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath))
                throw new FileNotFoundException("ffmpeg is required to convert subtitle formats.");
        }
    }

    internal static void ValidateConverted(string text, string format)
    {
        var pattern = format switch
        {
            "vtt" or "srt" => @"^\d{2,}:(?:\d{2}:)?\d{2}[.,]\d{3}\s+-->\s+\d{2,}:(?:\d{2}:)?\d{2}[.,]\d{3}",
            "ass" => @"^Dialogue:\s*\d+,\d+:\d{2}:\d{2}\.\d{2},",
            "lrc" => @"^\[\d{2,}:\d{2}\.\d{2,3}\]",
            _ => throw new NotSupportedException("The converted subtitle format is unavailable.")
        };
        if (!Regex.IsMatch(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(500)))
            throw new InvalidDataException("Subtitle conversion did not produce any timed cues.");
    }
}
