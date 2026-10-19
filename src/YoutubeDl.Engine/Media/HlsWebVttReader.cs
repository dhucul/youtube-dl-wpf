using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

public sealed record HlsWebVttSegment(string Text, decimal StartSeconds, decimal DurationSeconds, string? Initialization = null,
    decimal? MediaStartSeconds=null,decimal? ExplicitOffsetSeconds=null,decimal ClipStartSeconds=0,decimal? ClipEndSeconds=null);

/// <summary>Combines WebVTT segments on a probed media clock, including the 33-bit MPEG timestamp rollover.</summary>
public static class HlsWebVttReader
{
    private const decimal WrapSeconds = 8589934592m / 90000m;
    private sealed record Cue(decimal Start, decimal End, string Id, string Settings, string Text);
    private sealed record Document(decimal Local, ulong Mpeg, IReadOnlyList<Cue> Cues, IReadOnlyList<string> Styles);
    private const string Timestamp = @"(?:\d{2,12}:)?[0-5]\d:[0-5]\d\.\d{3}";

    public static string Join(IReadOnlyList<HlsWebVttSegment> segments, decimal mediaStartSeconds, decimal durationSeconds, CancellationToken ct = default)
    {
        if (segments.Count is 0 or > 10000 || durationSeconds <= 0) throw new InvalidDataException("The HLS caption presentation is empty or excessive.");
        var cues = new List<Cue>(); var previous = new Dictionary<(string, string, string), int>();
        var styles = new List<string>(); var seen = new HashSet<Cue>();
        long inputBytes = 0; var outputCharacters = 0;
        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            if (segment.StartSeconds < 0 || segment.DurationSeconds <= 0 || segment.DurationSeconds >= WrapSeconds / 2)
                throw new NotSupportedException("The HLS caption segment has an unsupported timing span.");
            inputBytes += Encoding.UTF8.GetByteCount(segment.Text) + (segment.Initialization is null ? 0 : Encoding.UTF8.GetByteCount(segment.Initialization));
            if (inputBytes > 8 * 1024 * 1024) throw new InvalidDataException("The HLS caption input exceeds its size limit.");
            var text = Normalize(segment.Text);
            if (!text.StartsWith("WEBVTT", StringComparison.Ordinal))
            {
                if (segment.Initialization is null) throw new InvalidDataException("The WebVTT segment has no header or initialization map.");
                text = Normalize(segment.Initialization).TrimEnd() + "\n\n" + text;
            }
            var document = Parse(text);
            foreach (var style in document.Styles)
            {
                if (styles.Contains(style, StringComparer.Ordinal)) continue;
                if (cues.Count > 0) throw new NotSupportedException("Changing HLS WebVTT styles or regions require a separate presentation timeline.");
                styles.Add(style); outputCharacters += style.Length;
            }
            if (document.Cues.Count == 0) continue;
            var first = document.Cues[0];
            var rawFirst = document.Mpeg / 90000m + first.Start - document.Local;
            var origin=segment.MediaStartSeconds??mediaStartSeconds;
            var cycles = decimal.Round((origin + segment.StartSeconds - rawFirst) / WrapSeconds, 0, MidpointRounding.ToEven);
            // WebVTT has millisecond precision; probing the media clock prints rounded decimals.
            // Quantize the mapping once so a sub-microsecond probe rounding error cannot add a millisecond to every cue end.
            var offset = segment.ExplicitOffsetSeconds??decimal.Round(document.Mpeg / 90000m - document.Local + cycles * WrapSeconds - origin, 3, MidpointRounding.AwayFromZero);
            foreach (var cue in document.Cues)
            {
                ct.ThrowIfCancellationRequested();
                if (cue.End - cue.Start >= WrapSeconds / 2) throw new NotSupportedException("The WebVTT cue span is too long to resolve MPEG timestamp rollover unambiguously.");
                var start = Math.Max(segment.ClipStartSeconds,Math.Max(0, cue.Start + offset)); var end = Math.Min(segment.ClipEndSeconds??durationSeconds,Math.Min(durationSeconds, cue.End + offset));
                if (end <= start) continue;
                var mapped = cue with { Start = start, End = end };
                if (!seen.Add(mapped)) continue;
                var key = (cue.Id, cue.Settings, cue.Text);
                if (previous.TryGetValue(key, out var index) && cues[index].Start <= mapped.End && mapped.Start <= cues[index].End)
                    cues[index] = cues[index] with { Start = Math.Min(cues[index].Start, mapped.Start), End = Math.Max(cues[index].End, mapped.End) };
                else
                {
                    if (cues.Count >= 100000) throw new InvalidDataException("The HLS caption cue count exceeds its limit.");
                    outputCharacters += cue.Id.Length + cue.Settings.Length + cue.Text.Length + 64;
                    if (outputCharacters > 2 * 1024 * 1024) throw new InvalidDataException("The combined HLS captions exceed their output limit.");
                    previous[key] = cues.Count; cues.Add(mapped);
                }
            }
        }
        var output = new StringBuilder("WEBVTT\n\n");
        foreach (var style in styles) output.Append(style).Append("\n\n");
        foreach (var cue in cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.End))
        {
            if (cue.Id.Length > 0) output.Append(cue.Id).Append('\n');
            output.Append(Format(cue.Start, false)).Append(" --> ").Append(Format(cue.End, true));
            if (cue.Settings.Length > 0) output.Append(' ').Append(cue.Settings);
            output.Append('\n').Append(cue.Text).Append("\n\n");
        }
        if (Encoding.UTF8.GetByteCount(output.ToString()) > 2 * 1024 * 1024) throw new InvalidDataException("The combined HLS captions exceed their byte limit.");
        return output.ToString();
    }

    private static Document Parse(string text)
    {
        if (!text.StartsWith("WEBVTT", StringComparison.Ordinal) || text.Length > 6 && text[6] is not (' ' or '\t' or '\n'))
            throw new InvalidDataException("The caption segment is not WebVTT.");
        var separator = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (separator < 0) throw new InvalidDataException("The WebVTT header is not terminated.");
        decimal local = 0; ulong mpeg = 0; var mapped = false;
        foreach (var header in text[..separator].Split('\n').Skip(1))
        {
            if (header.Contains("-->")) throw new InvalidDataException("The WebVTT header contains a cue timing line.");
            if (!header.StartsWith("X-TIMESTAMP-MAP=", StringComparison.Ordinal)) continue;
            if (mapped) throw new InvalidDataException("The WebVTT header repeats its timestamp map.");
            mapped = true; var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in header[16..].Split(','))
            {
                var keyValue = part.Split(':', 2);
                if (keyValue.Length != 2 || !fields.TryAdd(keyValue[0], keyValue[1])) throw new InvalidDataException("The WebVTT timestamp map is malformed.");
            }
            if (fields.Count != 2 || !fields.TryGetValue("LOCAL", out var localText) || !fields.TryGetValue("MPEGTS", out var mpegText) ||
                !ulong.TryParse(mpegText, NumberStyles.None, CultureInfo.InvariantCulture, out mpeg) || mpeg >= 1UL << 33)
                throw new InvalidDataException("The WebVTT timestamp map is invalid.");
            local = ParseTime(localText);
        }
        var cues = new List<Cue>(); var styles = new List<string>();
        foreach (var block in Regex.Split(text[(separator + 2)..].Trim('\n'), @"\n[ \t]*\n", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            if (block.Length == 0 || block == "NOTE" || block.StartsWith("NOTE ", StringComparison.Ordinal) || block.StartsWith("NOTE\t", StringComparison.Ordinal) || block.StartsWith("NOTE\n", StringComparison.Ordinal)) continue;
            if (block.StartsWith("STYLE\n", StringComparison.Ordinal) || block.StartsWith("REGION\n", StringComparison.Ordinal)) { styles.Add(block); continue; }
            var lines = block.Split('\n'); var position = lines[0].Contains("-->") ? 0 : 1;
            if (position >= lines.Length) throw new InvalidDataException("The WebVTT cue is missing its timing line.");
            var match = Regex.Match(lines[position], @"\A(?<start>" + Timestamp + @")[ \t]+-->[ \t]+(?<end>" + Timestamp + @")(?:[ \t]+(?<settings>.*))?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) throw new InvalidDataException("The WebVTT cue timing is invalid.");
            var start = ParseTime(match.Groups["start"].Value); var end = ParseTime(match.Groups["end"].Value);
            if (end <= start) throw new InvalidDataException("The WebVTT cue has an empty or reversed time range.");
            cues.Add(new(start, end, position == 0 ? "" : lines[0], match.Groups["settings"].Value, string.Join("\n", lines.Skip(position + 1))));
            if (cues.Count > 100000) throw new InvalidDataException("The WebVTT segment has too many cues.");
        }
        return new(local, mpeg, cues, styles);
    }
    private static decimal ParseTime(string value)
    {
        if (!Regex.IsMatch(value, @"\A" + Timestamp + @"\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidDataException("The WebVTT timestamp is invalid.");
        var parts = value.Split(':'); var seconds = decimal.Parse(parts[^1], CultureInfo.InvariantCulture) + decimal.Parse(parts[^2], CultureInfo.InvariantCulture) * 60;
        if (parts.Length == 3) seconds += decimal.Parse(parts[0], CultureInfo.InvariantCulture) * 3600;
        return seconds;
    }
    private static string Format(decimal time, bool end)
    {
        var ms = checked((long)(end ? decimal.Ceiling(time * 1000) : decimal.Floor(time * 1000)));
        return string.Create(CultureInfo.InvariantCulture, $"{ms / 3600000:D2}:{ms / 60000 % 60:D2}:{ms / 1000 % 60:D2}.{ms % 1000:D3}");
    }
    private static string Normalize(string text)
    {
        if (text.Contains('\0')) throw new InvalidDataException("The WebVTT document contains a null character.");
        return text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
