using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

/// <summary>Bounded parser for exposed-format selection, filters, fallbacks, and track pairing.</summary>
public static class NativeFormatSelector
{
    private abstract record Node;
    private sealed record Choice(IReadOnlyList<Node> Options) : Node;
    private sealed record Merge(IReadOnlyList<Node> Tracks) : Node;
    private sealed record Named(string Name, bool Exact) : Node;
    private sealed record Filtered(Node Inner, IReadOnlyList<Filter> Filters) : Node;
    private sealed record Filter(string Field, string Operator, string Value, bool AllowMissing, bool Numeric, Regex? Expression);
    private static readonly HashSet<string> NumericFields = ["height", "width", "fps", "filesize", "tbr", "aspect_ratio"];
    private static readonly HashSet<string> TextFields = ["format_id", "ext", "container", "protocol", "vcodec", "acodec", "language", "url"];
    private static readonly HashSet<string> Extensions = ["3gp", "aac", "flv", "m4a", "mp3", "mp4", "ogg", "wav", "webm"];

    public static void Validate(string selector) { if (!string.IsNullOrWhiteSpace(selector)) Parse(selector, 0); }
    public static IReadOnlyList<MediaCandidate> Select(PageMetadata page, string selector, NativeDownloadRequest request)
    {
        var node = Parse(selector, 0);
        var available = page.Media.Where(candidate => FormatFacts.MatchesConstraints(page, candidate, request)).ToArray();
        var result = Evaluate(node, available, page, request.PreferFreeFormats);
        if (result is null || result.Count == 0)
            throw new NotSupportedException("No exposed native formats satisfy the requested selector and constraints. Player challenges or missing format facts may still need support.");
        return result;
    }
    private static IReadOnlyList<MediaCandidate>? Evaluate(Node node, IReadOnlyList<MediaCandidate> available, PageMetadata page, bool preferFreeFormats)
    {
        switch (node)
        {
            case Choice choice:
                foreach (var option in choice.Options) if (Evaluate(option, available, page, preferFreeFormats) is { Count: > 0 } selected) return selected;
                return null;
            case Filtered filtered:
                return Evaluate(filtered.Inner, available.Where(candidate => filtered.Filters.All(filter => Matches(filter, page, candidate))).ToArray(), page, preferFreeFormats);
            case Merge merge:
                var combined = new List<MediaCandidate>();
                foreach (var track in merge.Tracks)
                {
                    var selected = Evaluate(track, available, page, preferFreeFormats);
                    if (selected is null) return null;
                    foreach (var candidate in selected)
                    {
                        if (combined.Any(previous => previous.Id == candidate.Id && previous.Url == candidate.Url)) continue;
                        // With one stream of each kind, later formats carrying an already selected kind are ignored.
                        if (FormatFacts.Video(candidate) && combined.Any(FormatFacts.Video) || FormatFacts.Audio(candidate) && combined.Any(FormatFacts.Audio)) continue;
                        combined.Add(candidate);
                    }
                }
                return combined;
            case Named named:
                var name = named.Name;
                if (named.Exact) return available.FirstOrDefault(candidate => candidate.Id == name) is { } exact ? [exact] : null;
                var rank = 1;
                var rankMatch = Regex.Match(name, @"\A(?<name>(?:best|worst|b|w)(?:video|audio|v|a)?\*?)\.(?<rank>[0-9]+)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (rankMatch.Success)
                {
                    name = rankMatch.Groups["name"].Value;
                    rank = int.Parse(rankMatch.Groups["rank"].Value, CultureInfo.InvariantCulture);
                    if (rank is < 1 or > 1000) throw new ArgumentException("Native format rank must be between 1 and 1000.");
                }
                var worst = name.StartsWith('w');
                IEnumerable<MediaCandidate> pool = name switch
                {
                    "b" or "best" or "w" or "worst" => available.Where(candidate => FormatFacts.Audio(candidate) && FormatFacts.Video(candidate)),
                    "b*" or "best*" or "w*" or "worst*" => available,
                    "bv" or "bestvideo" or "wv" or "worstvideo" => available.Where(candidate => FormatFacts.Video(candidate) && !FormatFacts.Audio(candidate)),
                    "bv*" or "bestvideo*" or "wv*" or "worstvideo*" => available.Where(FormatFacts.Video),
                    "ba" or "bestaudio" or "wa" or "worstaudio" => available.Where(candidate => FormatFacts.Audio(candidate) && !FormatFacts.Video(candidate)),
                    "ba*" or "bestaudio*" or "wa*" or "worstaudio*" => available.Where(FormatFacts.Audio),
                    _ when Extensions.Contains(name) => available.Where(candidate => candidate.Extension == name),
                    _ => available.Where(candidate => candidate.Id == name)
                };
                var sorted = pool.OrderByDescending(candidate => candidate.Height ?? 0).ThenByDescending(candidate => candidate.FramesPerSecond ?? FormatFacts.Reported(page, candidate)?.FramesPerSecond ?? 0);
                if (preferFreeFormats) sorted = sorted.ThenByDescending(FreeFormatPreference.Rank);
                sorted = sorted.ThenByDescending(candidate => candidate.Bandwidth ?? 0).ThenByDescending(candidate => candidate.IsDefaultAudio);
                var value = (worst ? sorted.Reverse() : sorted).OrderBy(candidate => candidate.IsFallback).Skip(rank - 1).FirstOrDefault();
                return value is null ? null : [value];
            default: throw new InvalidOperationException("Unknown selector node.");
        }
    }
    private static bool Matches(Filter filter, PageMetadata page, MediaCandidate candidate)
    {
        if (filter.Numeric)
        {
            var actual = FormatFacts.Number(page, candidate, filter.Field);
            if (actual is null) return filter.AllowMissing;
            var expected = NumericValue(filter.Value);
            return filter.Operator switch { "=" => actual == expected, "!=" => actual != expected, "<" => actual < expected, "<=" => actual <= expected, ">" => actual > expected, ">=" => actual >= expected, _ => false };
        }
        var text = FormatFacts.Text(page, candidate, filter.Field);
        if (text is null) return filter.AllowMissing;
        var op = filter.Operator.TrimStart('!');
        var match = op switch
        {
            "=" => text.Equals(filter.Value, StringComparison.OrdinalIgnoreCase),
            "^=" => text.StartsWith(filter.Value, StringComparison.OrdinalIgnoreCase), "$=" => text.EndsWith(filter.Value, StringComparison.OrdinalIgnoreCase),
            "*=" => text.Contains(filter.Value, StringComparison.OrdinalIgnoreCase), "~=" => filter.Expression!.IsMatch(text), _ => false
        };
        return filter.Operator.StartsWith('!') ? !match : match;
    }
    private static double NumericValue(string text)
    {
        var match = Regex.Match(text, @"\A(?<number>[0-9]+(?:\.[0-9]+)?)(?<unit>[kKmMgGtT]?)(?<binary>[iI]?)[bB]?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new ArgumentException("A native numeric format filter has an invalid value.");
        var value = double.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToUpperInvariant();
        var power = unit.Length == 0 ? 0 : "KMGT".IndexOf(unit[0]) + 1;
        value *= Math.Pow(match.Groups["binary"].Success && match.Groups["binary"].Length > 0 ? 1024 : 1000, power);
        if (!double.IsFinite(value)) throw new ArgumentException("The native format filter value is excessive.");
        return value;
    }
    private static Node Parse(string text, int depth)
    {
        text = text.Trim();
        if (text.Length is 0 or > 4096 || depth > 20) throw new ArgumentException("The format selector is empty, oversized, or too deeply nested.");
        var alternatives = Split(text, '/');
        if (alternatives.Count > 1) return new Choice(alternatives.Select(part => Parse(part, depth + 1)).ToArray());
        var tracks = Split(text, '+');
        if (tracks.Count > 1) return new Merge(tracks.Select(part => Parse(part, depth + 1)).ToArray());
        if (Split(text, ',').Count > 1) throw new NotSupportedException("Native selectors requesting separate output files remain pending.");
        var start = FindFilterStart(text);
        var filters = start < 0 ? [] : ReadFilters(text[start..]);
        var atom = (start < 0 ? text : text[..start]).Trim();
        Node node;
        if (atom.StartsWith('(') && atom.EndsWith(')')) node = Parse(atom[1..^1], depth + 1);
        else
        {
            var exact = atom.Length >= 2 && (atom[0] is '\'' or '"') && atom[^1] == atom[0];
            if (exact) atom = atom[1..^1];
            if (!exact && atom is "all" or "mergeall" or "-") throw new NotSupportedException("Native all-format, multistream, and interactive selectors remain pending.");
            if (!exact && Regex.IsMatch(atom, @"\A(?:best|worst|b|w)(?:video|audio|v|a)?\*?\.[0-9]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                if (!int.TryParse(atom[(atom.LastIndexOf('.') + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var rank) || rank is < 1 or > 1000)
                    throw new ArgumentException("Native format rank must be between 1 and 1000.");
            }
            if (atom.Length == 0) atom = "best";
            node = new Named(atom, exact);
        }
        return filters.Count == 0 ? node : new Filtered(node, filters);
    }
    private static IReadOnlyList<string> Split(string text, char separator)
    {
        var result = new List<string>();
        var bracket = 0; var parentheses = 0; var quoted = '\0'; var escaped = false; var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quoted != '\0') { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == quoted) quoted = '\0'; continue; }
            if (ch is '\'' or '"') { quoted = ch; continue; }
            if (ch == '[') bracket++; else if (ch == ']') bracket--;
            else if (bracket == 0 && ch == '(') parentheses++; else if (bracket == 0 && ch == ')') parentheses--;
            if (bracket < 0 || parentheses < 0) throw new ArgumentException("The format selector has unbalanced delimiters.");
            if (ch == separator && bracket == 0 && parentheses == 0) { result.Add(text[start..index]); start = index + 1; }
        }
        if (quoted != '\0' || bracket != 0 || parentheses != 0) throw new ArgumentException("The format selector has unfinished delimiters or quotes.");
        result.Add(text[start..]);
        if (result.Count > 128) throw new ArgumentException("The format selector has too many alternatives or tracks.");
        return result;
    }
    private static int FindFilterStart(string text)
    {
        var depth = 0; var quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quote != '\0') { if (ch == quote && (index == 0 || text[index - 1] != '\\')) quote = '\0'; continue; }
            if (ch is '\'' or '"') quote = ch; else if (ch == '(') depth++; else if (ch == ')') depth--; else if (ch == '[' && depth == 0) return index;
        }
        return -1;
    }
    private static List<Filter> ReadFilters(string text)
    {
        var filters = new List<Filter>();
        while (text.Length > 0)
        {
            text = text.TrimStart();
            if (!text.StartsWith('[')) throw new ArgumentException("Unexpected text after a format filter.");
            var end = -1; var quote = '\0'; var escaped = false;
            for (var index = 1; index < text.Length; index++)
            {
                var ch = text[index];
                if (quote != '\0') { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == quote) quote = '\0'; }
                else if (ch is '\'' or '"') quote = ch; else if (ch == ']') { end = index; break; }
            }
            if (end < 0) throw new ArgumentException("The format filter is unfinished.");
            var match = Regex.Match(text[1..end], @"\A\s*(?<field>[a-z_]+)\s*(?<operator>!\^=|!\$=|!\*=|!~=|!=|<=|>=|\^=|\$=|\*=|~=|=|<|>)\s*(?<missing>\?)?\s*(?<value>.*?)\s*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) throw new ArgumentException("The native format filter has invalid syntax.");
            var field = match.Groups["field"].Value; var op = match.Groups["operator"].Value; var value = match.Groups["value"].Value;
            var numeric = NumericFields.Contains(field);
            if (!numeric && !TextFields.Contains(field)) throw new NotSupportedException("Native format filtering is pending for: " + field);
            if (value.Length >= 2 && (value[0] is '\'' or '"') && value[^1] == value[0]) value = value[1..^1];
            if (numeric) { if (op is not ("=" or "!=" or "<" or "<=" or ">" or ">=")) throw new ArgumentException("This numeric format filter operator is invalid."); NumericValue(value); }
            else if (op is "<" or "<=" or ">" or ">=") throw new ArgumentException("Text format filters do not support numeric ordering.");
            var regex = op.EndsWith("~=", StringComparison.Ordinal) ? new Regex(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) : null;
            filters.Add(new(field, op, value, match.Groups["missing"].Success, numeric, regex));
            if (filters.Count > 64) throw new ArgumentException("The selector has too many format filters.");
            text = text[(end + 1)..];
        }
        return filters;
    }
}
