using System.Globalization;
using System.Text;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static class ChapterMetadataWriter
{
    public static IReadOnlyList<ChapterMarker> WithExplicitGaps(IReadOnlyList<ChapterMarker> chapters)
    {
        _ = Render(chapters);
        var result = new List<ChapterMarker>(); long previousEnd = 0;
        foreach (var chapter in chapters)
        {
            var start = checked((long)Math.Round(chapter.StartSeconds * 1000, MidpointRounding.AwayFromZero));
            if (start > previousEnd) result.Add(new() { Title = "", StartSeconds = previousEnd / 1000d, EndSeconds = start / 1000d });
            result.Add(chapter);
            previousEnd = checked((long)Math.Round(chapter.EndSeconds * 1000, MidpointRounding.AwayFromZero));
        }
        if (result.Count > 1000) throw new NotSupportedException("Preserving all chapter gaps exceeds the movie chapter limit.");
        return result;
    }

    public static string Render(IReadOnlyList<ChapterMarker> chapters)
    {
        if (chapters.Count > 1000) throw new InvalidDataException("The chapter list exceeds the supported size.");
        var output = new StringBuilder(";FFMETADATA1\n");
        long previousEnd = 0;
        foreach (var chapter in chapters)
        {
            if (!double.IsFinite(chapter.StartSeconds) || !double.IsFinite(chapter.EndSeconds) || chapter.StartSeconds < 0 ||
                chapter.EndSeconds <= chapter.StartSeconds || chapter.EndSeconds >= long.MaxValue / 1000d ||
                chapter.Title.Length > 4096 || chapter.Title.Contains('\0')) throw new InvalidDataException("The chapter has invalid times or title.");
            var start = checked((long)Math.Round(chapter.StartSeconds * 1000, MidpointRounding.AwayFromZero));
            var end = checked((long)Math.Round(chapter.EndSeconds * 1000, MidpointRounding.AwayFromZero));
            if (start < previousEnd || end <= start) throw new InvalidDataException("Chapters must have ordered, non-overlapping ranges with millisecond precision.");
            output.Append("\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=").Append(start.ToString(CultureInfo.InvariantCulture))
                .Append("\nEND=").Append(end.ToString(CultureInfo.InvariantCulture)).Append("\ntitle=").Append(Escape(chapter.Title)).Append('\n');
            previousEnd = end;
        }
        return output.ToString();
    }
    private static string Escape(string title)
    {
        var output = new StringBuilder();
        foreach (var ch in title.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        { if (ch is '\\' or '=' or ';' or '#' or '\n') output.Append('\\'); output.Append(ch); }
        return output.ToString();
    }
}
