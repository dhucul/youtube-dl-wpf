using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

internal static class SubtitleOverlap
{
    private sealed record Cue(long Start, long End, string Text);
    internal static byte[]? Normalize(byte[] bytes, string extension, CancellationToken ct)
    {
        if (bytes.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("The embedding caption exceeds its size limit.");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        var cues = new List<Cue>();
        const string time = @"(?:[0-9]{2,6}:)?[0-5][0-9]:[0-5][0-9][.,][0-9]{1,3}";
        foreach (var block in Regex.Split(text, @"\n[ \t]*\n", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(block) || extension == ".vtt" && (block.StartsWith("WEBVTT") || block == "NOTE" || block.StartsWith("NOTE ") || block.StartsWith("NOTE\t") || block.StartsWith("NOTE\n") || block.StartsWith("STYLE\n") || block.StartsWith("REGION\n"))) continue;
            var lines = block.Trim('\n').Split('\n'); var index = lines[0].Contains("-->") ? 0 : 1;
            if (index >= lines.Length) return null;
            var match = Regex.Match(lines[index], @"\A(?<start>" + time + @")[ \t]+-->[ \t]+(?<end>" + time + @")(?:[ \t].*)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) return null; // Leave other supported syntax to the existing parser.
            var start = Milliseconds(match.Groups["start"].Value); var end = Milliseconds(match.Groups["end"].Value);
            if (end <= start) throw new InvalidDataException("An embedding caption has an empty or reversed interval.");
            cues.Add(new(start, end, string.Join('\n', lines.Skip(index + 1))));
            if (cues.Count > 100000) throw new InvalidDataException("The embedding caption has too many cues.");
        }
        long latestEnd = -1; var overlap = false;
        foreach (var cue in cues.OrderBy(cue => cue.Start)) { if (cue.Start < latestEnd) overlap = true; latestEnd = Math.Max(latestEnd, cue.End); }
        if (!overlap) return null;
        if (cues.Count > 10000) throw new InvalidDataException("The overlapping caption has too many cues to compose safely.");
        if (cues.Any(cue => Regex.IsMatch(cue.Text, "<" + time + ">", RegexOptions.CultureInvariant)))
            throw new NotSupportedException("Overlapping inline-timed captions require additional native rendering support.");
        var events = new SortedDictionary<long, List<(int Index, bool Start)>>();
        for (var index = 0; index < cues.Count; index++)
        {
            Add(cues[index].Start, index, true); Add(cues[index].End, index, false);
        }
        var active = new SortedSet<int>(); var rendered = new List<Cue>(); long? previous = null; var work = 0; long renderedCharacters = 0;
        foreach (var pair in events)
        {
            ct.ThrowIfCancellationRequested();
            if (previous is { } start && start < pair.Key && active.Count > 0)
            {
                work += active.Count; if (work > 1000000) throw new InvalidDataException("Overlapping caption rendering exceeds its work limit.");
                var payload = string.Join('\n', active.Select(index => cues[index].Text));
                if (rendered.Count > 0 && rendered[^1].End == start && rendered[^1].Text == payload) rendered[^1] = rendered[^1] with { End = pair.Key };
                else
                {
                    renderedCharacters += payload.Length + 80;
                    if (renderedCharacters > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("Overlapping captions exceed their rendered size limit.");
                    rendered.Add(new(start, pair.Key, payload));
                }
            }
            foreach (var change in pair.Value) { if (change.Start) active.Add(change.Index); else active.Remove(change.Index); }
            previous = pair.Key;
        }
        var result = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in rendered)
        {
            result.Append(Stamp(cue.Start)).Append(" --> ").Append(Stamp(cue.End)).Append('\n').Append(cue.Text).Append("\n\n");
            if (result.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("Overlapping captions exceed their rendered size limit.");
        }
        var output = Encoding.UTF8.GetBytes(result.ToString());
        if (output.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("Overlapping captions exceed their rendered byte limit.");
        return output;
        void Add(long timeValue, int index, bool start) { if (!events.TryGetValue(timeValue, out var list)) events.Add(timeValue, list = []); list.Add((index, start)); }
    }
    private static long Milliseconds(string text)
    {
        decimal seconds = 0; foreach (var part in text.Replace(',', '.').Split(':')) seconds = seconds * 60 + decimal.Parse(part, CultureInfo.InvariantCulture);
        return checked((long)(seconds * 1000));
    }
    private static string Stamp(long value) => string.Create(CultureInfo.InvariantCulture, $"{value / 3600000:D2}:{value / 60000 % 60:D2}:{value / 1000 % 60:D2}.{value % 1000:D3}");
}
