using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static class SubtitleSelection
{
    public static IReadOnlyList<SubtitleTrack> Select(IReadOnlyList<SubtitleTrack> tracks, NativeDownloadRequest request)
    {
        var patterns = Patterns(request.SubLanguages);
        var formats = SubtitleFormats.Preferences(request.SubFormat);
        if (tracks.Count > 256) throw new InvalidDataException("The provider returned too many caption tracks.");
        var available = tracks.Where(track => track.Automatic ? request.WriteAutoSubs : request.WriteSubs || request.EmbedSubs)
            .GroupBy(track => track.Language, StringComparer.OrdinalIgnoreCase).ToArray();
        IEnumerable<IGrouping<string, SubtitleTrack>> languages;
        if (patterns.Count == 0)
            languages = available.OrderByDescending(group => group.Key.Equals("en", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(group => group.Key.StartsWith("en-", StringComparison.OrdinalIgnoreCase)).Take(1);
        else languages = available.Where(group => patterns.Any(pattern => !pattern.Exclude && pattern.Regex.IsMatch(group.Key)) &&
            !patterns.Any(pattern => pattern.Exclude && pattern.Regex.IsMatch(group.Key)));
        return languages.Select(group => SubtitleFormats.Choose(group.Any(track => !track.Automatic) ? group.Where(track => !track.Automatic) : group, formats, group.Key)).ToArray();
    }

    public static void Validate(string languages) => Patterns(languages);

    private static List<(bool Exclude, Regex Regex)> Patterns(string languages)
    {
        var result = new List<(bool, Regex)>();
        if (string.IsNullOrWhiteSpace(languages)) return result;
        if (languages.Length > 1024) throw new ArgumentException("The caption language selection is too long.");
        foreach (var part in languages.Split(',', StringSplitOptions.TrimEntries))
        {
            var exclude = part.StartsWith('-');
            var pattern = exclude ? part[1..] : part;
            if (pattern.Length == 0) throw new ArgumentException("The caption language selection contains an empty pattern.");
            if (pattern.Equals("all", StringComparison.OrdinalIgnoreCase)) pattern = ".*";
            result.Add((exclude, new Regex("\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))));
        }
        if (result.All(pattern => pattern.Item1)) result.Insert(0, (false, new Regex(".*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))));
        return result;
    }
}
