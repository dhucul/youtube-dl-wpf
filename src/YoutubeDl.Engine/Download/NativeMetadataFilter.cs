using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

/// <summary>Compiles declarative metadata predicates; no expression or shell evaluation occurs.</summary>
internal sealed class NativeMetadataFilter
{
    private static readonly HashSet<string> NumericFields = new(StringComparer.Ordinal)
    { "duration", "timestamp", "view_count", "like_count", "dislike_count", "comment_count", "age_limit", "height", "width", "fps", "tbr", "abr", "vbr", "asr", "audio_channels", "filesize", "filesize_approx", "playlist_index", "playlist_autonumber", "playlist_count", "n_entries" };
    private sealed record Condition(string Field, string? Operator, bool Negated, bool MissingAllowed, string Value, Regex? Pattern);
    private readonly Condition[][] _alternatives;
    internal NativeMetadataFilter(NativeDownloadRequest request)
    {
        var filters = (string.IsNullOrEmpty(request.MatchFilter) ? [] : new[] { request.MatchFilter }).Concat(request.MatchFilters).ToArray();
        if (filters.Length > 64 || filters.Any(filter => filter is null || filter.Length > 16384) || filters.Sum(filter => filter.Length) > 65536)
            throw new NativeSelectionException("The metadata filter exceeds its supported expression bounds.");
        _alternatives = filters.Select(Parse).ToArray();
    }
    internal bool Matches(PageMetadata page, NativeDownloadRequest request, CancellationToken ct, bool incomplete)
    {
        if (_alternatives.Length == 0) return true;
        foreach (var alternative in _alternatives)
        {
            var accepted = true;
            foreach (var condition in alternative)
            {
                ct.ThrowIfCancellationRequested();
                if (!Evaluate(condition, MetadataFacts.Field(condition.Field, page, request), incomplete)) { accepted = false; break; }
            }
            if (accepted) return true;
        }
        return false;
    }
    private static Condition[] Parse(string expression)
    {
        if (expression == "-") throw new NativeSelectionException("Interactive per-item approval requires additional native application support.");
        var parts = new List<string>(); var start = 0;
        for (var index = 0; index < expression.Length; index++)
            if (expression[index] == '&' && (index == 0 || expression[index - 1] != '\\')) { parts.Add(expression[start..index]); start = index + 1; }
        parts.Add(expression[start..]);
        if (parts.Count > 128) throw new NativeSelectionException("The metadata filter contains too many conditions.");
        return parts.Select(part => ParseCondition(part.Replace(@"\&", "&", StringComparison.Ordinal).Trim())).ToArray();
    }
    private static Condition ParseCondition(string text)
    {
        var unary = Regex.Match(text, @"\A(?<not>!)?\s*(?<field>[a-z_][a-z_0-9]*)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (unary.Success) return new(unary.Groups["field"].Value, null, unary.Groups["not"].Success, false, "", null);
        var compare = Regex.Match(text, @"\A(?<field>[a-z_][a-z_0-9]*)\s*(?<not>!\s*)?(?<op>\^=|\$=|\*=|~=|<=|>=|<|>|=)\s*(?<missing>\?)?\s*(?<value>.+)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!compare.Success) throw new NativeSelectionException("The metadata filter contains an invalid field condition.");
        var field = compare.Groups["field"].Value; var op = compare.Groups["op"].Value; var value = compare.Groups["value"].Value.Trim();
        if (value.Length == 0) throw new NativeSelectionException("A metadata comparison requires a value.");
        if (value[0] is '\'' or '"')
        {
            var quote = value[0]; if (value.Length < 2 || value[^1] != quote) throw new NativeSelectionException("A metadata comparison contains an unfinished quoted value.");
            value = value[1..^1].Replace("\\" + quote, quote.ToString(), StringComparison.Ordinal);
        }
        Regex? pattern = null;
        if (op == "~=")
        {
            try { pattern = new Regex(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { throw new NativeSelectionException("The metadata regular expression is not supported."); }
        }
        if (NumericFields.Contains(field))
        {
            if (op is "^=" or "$=" or "*=" or "~=") throw new NativeSelectionException("This metadata operator requires a string field.");
            _ = Number(value);
        }
        return new(field, op, compare.Groups["not"].Success, compare.Groups["missing"].Success, value, pattern);
    }
    private static bool Evaluate(Condition condition, object? actual, bool incomplete)
    {
        if (actual is null) return incomplete || (condition.Operator is null ? condition.Negated : condition.MissingAllowed);
        if (condition.Operator is null) return actual is bool boolean ? condition.Negated ? !boolean : boolean : !condition.Negated;
        bool result;
        if (actual is string text)
        {
            var order = string.CompareOrdinal(text, condition.Value);
            try
            {
                result = condition.Operator switch
                {
                    "=" => order == 0, "<" => order < 0, "<=" => order <= 0, ">" => order > 0, ">=" => order >= 0,
                    "*=" => text.Contains(condition.Value, StringComparison.Ordinal), "^=" => text.StartsWith(condition.Value, StringComparison.Ordinal),
                    "$=" => text.EndsWith(condition.Value, StringComparison.Ordinal), "~=" => condition.Pattern!.IsMatch(text), _ => throw new InvalidOperationException()
                };
            }
            catch (RegexMatchTimeoutException) { throw new NativeSelectionException("The metadata regular expression exceeded its evaluation time limit."); }
        }
        else if (actual is bool or byte or short or int or long or float or double or decimal)
        {
            if (condition.Operator is "*=" or "^=" or "$=" or "~=") throw new NativeSelectionException("This metadata operator requires a string field.");
            decimal number;
            try { number = Convert.ToDecimal(actual, CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is OverflowException or FormatException) { throw new NativeSelectionException("The numeric metadata value cannot be compared."); }
            var expected = Number(condition.Value);
            result = condition.Operator switch { "=" => number == expected, "<" => number < expected, "<=" => number <= expected, ">" => number > expected, ">=" => number >= expected, _ => false };
        }
        else if (condition.Operator == "*=" && actual is JsonElement { ValueKind: JsonValueKind.Array } array)
            result = array.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == condition.Value);
        else if (condition.Operator == "=") result = false;
        else throw new NativeSelectionException("This metadata value does not support the requested comparison.");
        return condition.Negated ? !result : result;
    }
    private static decimal Number(string text)
    {
        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return integer;
        var scaled = Regex.Match(text, @"\A(?<value>[0-9]+(?:[.,][0-9]+)?)\s*(?<unit>[A-Za-z]*)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        try
        {
            if (scaled.Success)
            {
                var amount = decimal.Parse(scaled.Groups["value"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                var unit = scaled.Groups["unit"].Value; var radix = 1; var power = 0;
                if (unit.Length == 0) unit = "B";
                if (unit is not ("B" or "b" or "bytes"))
                {
                    var names = new[] { "kilo", "mega", "giga", "tera", "peta", "exa", "zetta", "yotta" };
                    var binary = new[] { "kibi", "mebi", "gibi", "tebi", "pebi", "exbi", "zebi", "yobi" };
                    var named = Array.FindIndex(names, name => unit == name + "bytes");
                    var namedBinary = Array.FindIndex(binary, name => unit == name + "bytes");
                    if (named >= 0) { power = named + 1; radix = 1000; }
                    else if (namedBinary >= 0) { power = namedBinary + 1; radix = 1024; }
                    else
                    {
                        if (unit.Length is 1 or 2 && unit[^1] != 'B' && unit[^1] != 'b') unit += "B";
                        power = "KMGTPEZY".IndexOf(char.ToUpperInvariant(unit[0])) + 1;
                        var suffix = unit[1..];
                        if (power == 0 || suffix is not ("B" or "b" or "iB")) throw new NativeSelectionException("The metadata comparison uses an unsupported numeric unit.");
                        radix = suffix == "iB" || char.IsLower(unit[0]) && suffix == "B" ? 1024 : 1000;
                    }
                }
                for (var i = 0; i < power; i++) amount = checked(amount * radix);
                return decimal.Round(amount, 0, MidpointRounding.ToEven);
            }
            if (text.Contains(':') && Providers.ProviderJson.Duration(text) is { } duration) return (decimal)duration;
        }
        catch (Exception ex) when (ex is OverflowException or FormatException) { }
        throw new NativeSelectionException("The metadata comparison requires a supported numeric value, unit or duration.");
    }
}
