using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

/// <summary>Expands typed media fields into a filename without evaluating scripts.</summary>
public static class NativeOutputTemplate
{
    private sealed record Token(string Literal, string? Field = null, string Flags = "", int Width = 0, int? Precision = null, char Conversion = 's');
    public static void Validate(string template) => Parse(template);

    public static string Render(NativeDownloadRequest request, PageMetadata page, MediaCandidate format, string extension)
    {
        var template = request.OutputTemplate;
        if (string.IsNullOrWhiteSpace(template))
        {
            var title = string.IsNullOrWhiteSpace(request.FileName) ? page.Title : request.FileName;
            return Path.Combine(Path.GetFullPath(request.OutputDirectory), Sanitize(title, request.RestrictFilenames) + "." + extension);
        }
        var expanded = Expand(template, request, page, format, extension, sanitize: true).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var pathRoot = Path.GetPathRoot(expanded) ?? "";
        var relative = expanded[pathRoot.Length..];
        var components = relative.Split(Path.DirectorySeparatorChar);
        if (components.Length == 0 || string.IsNullOrWhiteSpace(components[^1]) || components[^1] is "." or "..") throw new ArgumentException("The output template does not identify a file.");
        for (var index = 0; index < components.Length; index++)
            if (components[index] is not ("." or "..")) components[index] = Sanitize(components[index], request.RestrictFilenames);
        expanded = pathRoot + string.Join(Path.DirectorySeparatorChar, components);
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(request.OutputDirectory, expanded));
    }

    internal static string RenderMetadataText(string template, NativeDownloadRequest request, PageMetadata page) =>
        Expand(template, request, page, new MediaCandidate { Url = request.Url, Kind = MediaKind.Unknown }, "NA", sanitize: false);

    private static string Expand(string template, NativeDownloadRequest request, PageMetadata page, MediaCandidate format, string extension, bool sanitize)
    {
        var output = new StringBuilder();
        foreach (var token in Parse(template, filename: sanitize))
        {
            if (token.Field is null) { output.Append(token.Literal); continue; }
            var parts = token.Field.Split('|', 2);
            var field = parts[0];
            var replacementParts = field.Split('&', 2);
            object? value = null;
            foreach (var alternative in replacementParts[0].Split(','))
            {
                if (!Regex.IsMatch(alternative, @"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new NotSupportedException("Native output expressions are pending for: " + alternative);
                value = Field(alternative, request, page, format, extension);
                if (value is not null && value is not "") break;
            }
            var missing = value is null or "";
            if (missing) value = parts.Length == 2 ? parts[1] : "NA";
            else if (replacementParts.Length == 2)
            {
                if (replacementParts[1].Replace("{}", "", StringComparison.Ordinal).Contains('{') || replacementParts[1].Replace("{}", "", StringComparison.Ordinal).Contains('}'))
                    throw new NotSupportedException("Native output replacements currently support {} placeholders.");
                value = replacementParts[1].Replace("{}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }
            var text = ConvertValue(value!, token, missing);
            if (text.Length > (sanitize ? 4096 : 1024 * 1024)) throw new ArgumentException("An expanded metadata field exceeds the supported size.");
            // Metadata may supply a filename component, never directory separators or a path root.
            output.Append(sanitize ? Sanitize(text, request.RestrictFilenames, allowEmpty: true) : text);
            if (!sanitize && output.Length > 1024 * 1024) throw new ArgumentException("Expanded metadata exceeds the supported size.");
        }
        return output.ToString();
    }

    private static object? Field(string name, NativeDownloadRequest request, PageMetadata page, MediaCandidate format, string extension)
    {
        if (page.ParsedMetadata.TryGetValue(name, out var parsed)) return parsed;
        var reported = page.ReportedFormats.FirstOrDefault(item => item.Id == format.Id);
        return name switch
        {
            "id" => page.Id ?? request.Url.Host,
            "title" or "fulltitle" => page.Title,
            "ext" => extension,
            "uploader" or "creator" or "channel" => page.Uploader,
            "extractor" or "extractor_key" => page.Extractor,
            "webpage_url" or "original_url" => request.Url.AbsoluteUri,
            "format_id" => format.Id,
            "format" or "format_note" => format.Label,
            "height" => format.Height,
            "width" => format.Width,
            "fps" => reported?.FramesPerSecond,
            "filesize" => reported?.FileSize,
            "duration" => page.DurationSeconds,
            "upload_date" or "timestamp" or "view_count" or "age_limit" => MetadataFacts.Field(name, page, request),
            "resolution" => format.Width is not null && format.Height is not null ? format.Width + "x" + format.Height : null,
            "playlist" or "playlist_title" => request.PlaylistTitle,
            "playlist_id" => request.PlaylistId,
            "playlist_index" => request.PlaylistIndex,
            "playlist_autonumber" => request.PlaylistAutoNumber,
            "playlist_count" or "n_entries" => request.PlaylistCount,
            _ => MetadataFacts.Field(name, page, request)
        };
    }
    private static string ConvertValue(object value, Token token, bool missing)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (!missing && token.Conversion is 'd' or 'i' or 'f')
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw new ArgumentException("The output template requested numeric formatting for a nonnumeric field.");
            text = token.Conversion == 'f' ? number.ToString("F" + (token.Precision ?? 6), CultureInfo.InvariantCulture)
                : Math.Truncate(number).ToString("F0", CultureInfo.InvariantCulture);
            if (number >= 0 && token.Flags.Contains('+')) text = "+" + text;
        }
        else if (token.Precision is { } precision)
        {
            var result = new StringBuilder();
            var count = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                var length = token.Conversion == 'B' ? rune.Utf8SequenceLength : 1;
                if (count + length > precision) break;
                result.Append(rune.ToString()); count += length;
            }
            text = result.ToString();
        }
        if (token.Width > text.Length)
        {
            var padding = new string(token.Flags.Contains('0') && !missing && token.Conversion is 'd' or 'i' or 'f' ? '0' : ' ', token.Width - text.Length);
            text = token.Flags.Contains('-') ? text + padding : padding[0] == '0' && (text.StartsWith('-') || text.StartsWith('+')) ? text[..1] + padding + text[1..] : padding + text;
        }
        return text;
    }
    private static IReadOnlyList<Token> Parse(string template, bool filename = true)
    {
        if (template.Length > 4096) throw new ArgumentException("The output template exceeds the supported length.");
        if (filename && template == "-") throw new NotSupportedException("Native media output to stdout is not implemented yet.");
        var tokens = new List<Token>();
        var literal = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '%') { literal.Append(template[index]); continue; }
            if (index + 1 < template.Length && template[index + 1] == '%') { literal.Append('%'); index++; continue; }
            if (index + 1 >= template.Length || template[index + 1] != '(') throw new ArgumentException("Use %% for a literal percent in an output template.");
            var end = template.IndexOf(')', index + 2);
            if (end < 0) throw new ArgumentException("The output template contains an unfinished field.");
            var expression = template[(index + 2)..end];
            if (expression.Length == 0) throw new NotSupportedException("Native output of the entire metadata object is not implemented yet.");
            var match = Regex.Match(template[(end + 1)..], @"\A(?<flags>[-+ 0]*)(?<width>[0-9]*)(?:\.(?<precision>[0-9]+))?(?<conversion>[sSBdif])",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) throw new NotSupportedException("This native output-template conversion is not implemented yet.");
            var width = ParseLimit(match.Groups["width"].Value);
            var precision = match.Groups["precision"].Success ? ParseLimit(match.Groups["precision"].Value) : (int?)null;
            tokens.Add(new(literal.ToString())); literal.Clear();
            tokens.Add(new("", expression, match.Groups["flags"].Value, width, precision, match.Groups["conversion"].Value[0]));
            index = end + match.Length;
        }
        tokens.Add(new(literal.ToString()));
        return tokens;
    }
    private static int ParseLimit(string value) => value.Length == 0 ? 0 : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result <= 240
        ? result : throw new ArgumentException("Output width and precision must be at most 240.");

    public static string Sanitize(string value, bool restrict, bool allowEmpty = false)
    {
        if (value.Length == 0) return allowEmpty ? "" : "download";
        var output = new StringBuilder();
        foreach (var rune in (restrict ? value.Normalize(NormalizationForm.FormKD) : value).EnumerateRunes())
        {
            if (restrict && Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            var invalid = rune.Value < 32 || rune.Value == 127 || "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal);
            if (restrict) invalid |= !(rune.IsAscii && (char.IsAsciiLetterOrDigit((char)rune.Value) || "._-".Contains((char)rune.Value)));
            output.Append(invalid ? "_" : rune.ToString());
        }
        var cleaned = output.ToString();
        if (!allowEmpty) cleaned = cleaned.Trim().TrimEnd('.', ' ');
        if (cleaned is "." or "..") cleaned = "_";
        if (cleaned.Length > 240)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length > 12) extension = "";
            var shortened = new StringBuilder();
            foreach (var rune in cleaned[..(cleaned.Length - extension.Length)].EnumerateRunes())
            { if (shortened.Length + rune.Utf16SequenceLength > 240 - extension.Length) break; shortened.Append(rune.ToString()); }
            cleaned = shortened + extension;
        }
        var stem = cleaned.Split('.')[0];
        if (Regex.IsMatch(stem, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) cleaned = "_" + cleaned;
        return cleaned.Length == 0 && !allowEmpty ? "download" : cleaned;
    }
}
