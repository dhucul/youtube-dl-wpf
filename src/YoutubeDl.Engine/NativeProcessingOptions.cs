using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine;

/// <summary>Bounded FFmpeg argument overrides for the native processing stages.</summary>
public static class NativeProcessingOptions
{
    private sealed record Rule(string? Stage, bool Input, int? Index, string[] Arguments);
    private static readonly AsyncLocal<IReadOnlyList<Rule>?> Current = new();
    private static readonly HashSet<string> Stages = new(StringComparer.OrdinalIgnoreCase)
    { "ExtractAudio", "VideoConvertor", "VideoRemuxer", "Merger", "Metadata", "EmbedSubtitle", "EmbedThumbnail", "Fixup" };

    public static void Validate(string? fixup, IReadOnlyList<string>? arguments)
    {
        _ = Fixup(fixup);
        _ = Parse(arguments);
    }

    internal static string Fixup(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => "", "never" => "never", "warn" => "warn", "detect_or_warn" => "warn",
        "detect_or_fix" => "detect_or_fix", "force" => "force",
        _ => throw new ArgumentException("Choose never, warn, detect_or_warn, detect_or_fix or force for native fixup.")
    };

    internal static IDisposable Enter(NativeDownloadRequest request)
    {
        var previous = Current.Value;
        Current.Value = Parse(request.PostprocessorArguments);
        return new Restore(() => Current.Value = previous);
    }

    internal static IReadOnlyList<string> Apply(IReadOnlyList<string> original, params string[] stages)
    {
        if (Current.Value is not { Count: > 0 } rules) return original;
        var selected = rules.Where(rule => rule.Stage is null || stages.Contains(rule.Stage, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0) return original;
        var result = new List<string>();
        var input = 0;
        for (var i = 0; i < original.Count; i++)
        {
            if (original[i] == "-i")
            {
                input++;
                foreach (var rule in selected.Where(rule => rule.Input && (rule.Index is null || rule.Index == input))) result.AddRange(rule.Arguments);
            }
            if (i == original.Count - 1)
                foreach (var rule in selected.Where(rule => !rule.Input)) result.AddRange(rule.Arguments);
            result.Add(original[i]);
        }
        if (selected.Any(rule => rule.Input && rule.Index > input))
            throw new NotSupportedException("The requested FFmpeg input position does not exist in this native processing stage.");
        return result;
    }

    private static IReadOnlyList<Rule> Parse(IReadOnlyList<string>? values)
    {
        if (values is null) return [];
        if (values.Count > 64) throw new ArgumentException("Too many native postprocessor argument groups.");
        var result = new List<Rule>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 16384 || value.Any(char.IsControl))
                throw new ArgumentException("Native postprocessor arguments are empty or exceed their limits.");
            var colon = value.StartsWith('-') ? -1 : value.IndexOf(':');
            var prefix = colon < 0 ? "ffmpeg" : value[..colon];
            var body = colon < 0 ? value : value[(colon + 1)..];
            var match = Regex.Match(prefix, @"\A(?:(?<stage>[A-Za-z]+)\+)?(?<tool>ffmpeg|[A-Za-z]+)(?:_(?<position>[io])(?<index>[1-9][0-9]?))?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            // The unnumbered input/output spelling is also accepted.
            if (!match.Success) match = Regex.Match(prefix, @"\A(?:(?<stage>[A-Za-z]+)\+)?(?<tool>ffmpeg|[A-Za-z]+)_(?<position>[io])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) throw new NotSupportedException("Native postprocessor argument targeting is unsupported.");
            var tool = match.Groups["tool"].Value;
            var stage = match.Groups["stage"].Success ? match.Groups["stage"].Value : null;
            if (!tool.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                if (stage is not null || !Stages.Contains(tool)) throw new NotSupportedException("Choose a supported native FFmpeg processing stage.");
                stage = tool;
            }
            if (stage is not null && !Stages.Contains(stage)) throw new NotSupportedException("Choose a supported native FFmpeg processing stage.");
            var input = match.Groups["position"].Value.Equals("i", StringComparison.OrdinalIgnoreCase);
            int? index = match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            if (!input && index > 1) throw new NotSupportedException("Native postprocessors publish one output per invocation.");
            var args = Split(body);
            if (args.Count == 0 || args.Count > 256 || args.Count % 2 != 0) throw new ArgumentException("Native FFmpeg overrides require option/value pairs.");
            for (var i = 0; i < args.Count; i += 2)
            {
                var flag = args[i];
                var allowed = input ? flag is "-threads" or "-thread_queue_size" or "-probesize" or "-analyzeduration" :
                    flag is "-threads" or "-movflags" or "-id3v2_version" or "-write_id3v1" or "-compression_level" or "-q:a" or "-qscale:a" or "-b:a" or "-preset" or "-crf" or "-write_xing" ||
                    Regex.IsMatch(flag, @"\A-metadata(?::s(?::[vas](?::[0-9]{1,2})?)?)?\z", RegexOptions.CultureInvariant);
                if (!allowed) throw new NotSupportedException("This FFmpeg override requires additional native output verification support.");
                if (args[i + 1].Length == 0 && !flag.StartsWith("-metadata", StringComparison.Ordinal)) throw new ArgumentException("A native FFmpeg option is missing its value.");
            }
            result.Add(new(stage, input, index, args.ToArray()));
        }
        return result;
    }

    private static List<string> Split(string text)
    {
        var result = new List<string>(); var word = new StringBuilder(); char quote = '\0'; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length && (text[i + 1] is '\'' or '"' or '\\' || quote == '\0' && char.IsWhiteSpace(text[i + 1]))) { word.Append(text[++i]); started = true; }
            else if (ch == quote) quote = '\0';
            else if (quote == '\0' && ch is '\'' or '"') { quote = ch; started = true; }
            else if (quote == '\0' && char.IsWhiteSpace(ch)) { if (started) { result.Add(word.ToString()); word.Clear(); started = false; } }
            else { word.Append(ch); started = true; }
        }
        if (quote != '\0') throw new ArgumentException("Native FFmpeg arguments contain an unterminated quote.");
        if (started) result.Add(word.ToString());
        return result;
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
