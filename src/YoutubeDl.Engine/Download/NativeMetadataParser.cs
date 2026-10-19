using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

/// <summary>Ordered, bounded text metadata rules; transport and verification facts remain provider-owned.</summary>
internal static class NativeMetadataParser
{
    private sealed record Rule(string Input, bool FieldInput, string? CopyField, Regex? Pattern, string[] Fields);
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "id", "extractor", "extractor_key", "url", "webpage_url", "original_url", "webpage_url_domain",
        "duration", "timestamp", "upload_date", "view_count", "age_limit", "is_live", "availability",
        "formats", "requested_formats", "requested_downloads", "entries", "chapters", "subtitles", "automatic_captions",
        "thumbnail", "thumbnails", "http_headers", "cookies", "additional_urls", "filepath", "filename", "ext",
        "playlist", "playlist_title", "playlist_id", "playlist_index", "playlist_autonumber", "playlist_count", "n_entries",
        "format", "format_id", "format_note", "width", "height", "fps", "filesize", "resolution",
        "has_drm", "drm", "protocol", "player_url", "__postprocessors", "__files_to_move"
    };
    private static readonly NativeDownloadRequest ValidationRequest = new() { Url = new("https://metadata.example/"), OutputDirectory = "." };
    internal static void Validate(IReadOnlyList<string> rules) => Compile(rules);

    internal static PageMetadata Apply(NativeDownloadRequest request, PageMetadata page, Action<string>? log, CancellationToken ct)
    {
        if (request.MetadataParseRules.Count == 0) return page;
        var rules = Compile(request.MetadataParseRules);
        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();
            var source = rule.FieldInput ? MetadataFacts.Field(rule.Input, page, request) : null;
            if (rule.FieldInput && source is null)
            {
                log?.Invoke("[native] metadata source is missing; rule skipped"); continue;
            }
            if (source is System.Text.Json.JsonElement json && json.ValueKind is System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object ||
                source is System.Collections.IEnumerable and not string)
                throw new NativeMetadataParseException("Metadata parsing of structured values needs additional native support.");
            string input;
            try { input = rule.FieldInput ? Convert.ToString(source, CultureInfo.InvariantCulture) ?? "" : NativeOutputTemplate.RenderMetadataText(rule.Input, request, page); }
            catch (ArgumentException) { throw new NativeMetadataParseException("A metadata input template exceeded its limits or could not format its source value."); }
            catch (NotSupportedException) { throw new NativeMetadataParseException("A metadata input template needs additional native support for its source value."); }
            if (input.Length > 1024 * 1024 || input.Contains('\0')) throw new NativeMetadataParseException("Metadata parsing input exceeds its supported limits.");
            var updates = new Dictionary<string, string?>(page.ParsedMetadata, StringComparer.Ordinal);
            if (rule.CopyField is not null) updates[rule.CopyField] = input;
            else
            {
                Match match;
                try { match = rule.Pattern!.Match(input); }
                catch (RegexMatchTimeoutException) { throw new NativeMetadataParseException("A metadata parsing expression exceeded its execution limit."); }
                ct.ThrowIfCancellationRequested();
                if (!match.Success) { log?.Invoke("[native] metadata pattern did not match; rule skipped"); continue; }
                foreach (var name in rule.Fields) updates[name] = match.Groups[name].Success ? match.Groups[name].Value : null;
            }
            if (updates.Values.Sum(value => (long)(value?.Length ?? 0)) > 4 * 1024 * 1024)
                throw new NativeMetadataParseException("Combined parsed metadata exceeds its supported size.");
            page = page with
            {
                Title = updates.TryGetValue("title", out var title) ? title ?? "" : page.Title,
                Description = updates.TryGetValue("description", out var description) ? description : page.Description,
                Uploader = updates.TryGetValue("uploader", out var uploader) ? uploader : page.Uploader,
                ParsedMetadata = new ReadOnlyDictionary<string, string?>(updates)
            };
        }
        return page;
    }

    internal static Dictionary<string, string> EmbeddedTags(NativeDownloadRequest request, PageMetadata page)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"] = page.Title, ["artist"] = page.Uploader ?? "" };
        if (page.ParsedMetadata.Count == 0) return tags;
        string? Text(string field) { var value = MetadataFacts.Field(field, page, request); return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture); }
        tags["title"] = Text("track") ?? page.Title;
        tags["artist"] = Text("artist") ?? Text("creator") ?? page.Uploader ?? "";
        foreach (var (tag, field) in new[] { ("date", "upload_date"), ("description", "description"), ("synopsis", "description"), ("purl", "webpage_url"),
            ("comment", "webpage_url"), ("track", "track_number"), ("composer", "composer"), ("genre", "genre"), ("album", "album"),
            ("album_artist", "album_artist"), ("disc", "disc_number"), ("show", "series"), ("season_number", "season_number"),
            ("episode_id", "episode_id"), ("episode_sort", "episode_number") })
            if (Text(field) is { } value) tags[tag] = value;
        foreach (var pair in page.ParsedMetadata.Where(pair => pair.Key.StartsWith("meta_", StringComparison.Ordinal)))
            tags[pair.Key[5..]] = pair.Value ?? "";
        return tags;
    }

    private static Rule[] Compile(IReadOnlyList<string> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count > 32) throw new ArgumentException("At most 32 metadata parsing rules are supported.");
        return rules.Select(CompileRule).ToArray();
    }
    private static Rule CompileRule(string raw)
    {
        if (raw is null || raw.Length > 8192 || raw.Contains('\0')) throw new ArgumentException("A metadata parsing rule exceeds its supported limits.");
        var colon = raw.IndexOf(':');
        if (colon < 0) throw new ArgumentException("Use FROM:TO for a metadata parsing rule.");
        var prefix = raw[..colon];
        if (prefix == "pre_process") { raw = raw[(colon + 1)..]; colon = raw.IndexOf(':'); }
        else if (prefix is "after_filter" or "video" or "before_dl" or "post_process" or "after_move" or "after_video" or "playlist")
            throw new NotSupportedException("This metadata parsing stage needs additional native support; use pre_process.");
        if (colon < 0 || colon == raw.Length - 1) throw new ArgumentException("Use FROM:TO for a metadata parsing rule.");
        var input = raw[..colon]; var output = raw[(colon + 1)..]; var fieldInput = Identifier(input);
        if (!fieldInput)
        {
            try { _ = NativeOutputTemplate.RenderMetadataText(input, ValidationRequest, new PageMetadata()); }
            catch (NotSupportedException) { throw new NotSupportedException("This metadata input template needs additional native support."); }
            catch (ArgumentException) { throw new ArgumentException("The metadata input template is invalid or exceeds its supported limits."); }
        }
        if (Identifier(output)) { ValidateField(output); return new(input, fieldInput, output, null, [output]); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pattern = output.Contains("%(", StringComparison.Ordinal) ? TemplatePattern(output, names) : RegexPattern(output, names);
        if (names.Count == 0 || names.Count > 64) throw new ArgumentException("A metadata parsing pattern must have one to 64 distinct named fields.");
        try
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));
            var actualNames = regex.GetGroupNames().Where(Identifier).ToArray();
            if (!names.SetEquals(actualNames)) throw new NotSupportedException("Metadata regex comments or capture syntax need additional native support.");
            return new(input, fieldInput, null, regex, actualNames);
        }
        catch (ArgumentException) { throw new ArgumentException("The metadata parsing expression is invalid or uses unsupported regex syntax."); }
    }
    private static string TemplatePattern(string template, HashSet<string> names)
    {
        var pattern = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '%') { pattern.Append(Regex.Escape(template[index].ToString())); continue; }
            if (index + 1 < template.Length && template[index + 1] == '%') { pattern.Append('%'); index++; continue; }
            if (index + 1 >= template.Length || template[index + 1] != '(') throw new ArgumentException("Use %(field)s captures or %% in metadata output patterns.");
            var end = template.IndexOf(')', index + 2);
            if (end < 0 || end + 1 >= template.Length || template[end + 1] != 's') throw new NotSupportedException("Metadata output patterns support only %(field)s captures.");
            var name = template[(index + 2)..end]; AddField(name, names); pattern.Append("(?<").Append(name).Append(">.+)"); index = end + 1;
        }
        return pattern.ToString();
    }
    private static string RegexPattern(string source, HashSet<string> names)
    {
        var output = new StringBuilder(); var characterClass = false;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\\') { output.Append(source[index]); if (++index < source.Length) output.Append(source[index]); continue; }
            if (source[index] == '[') characterClass = true;
            if (source[index] == ']') characterClass = false;
            if (!characterClass && source.AsSpan(index).StartsWith("(?P<", StringComparison.Ordinal))
            {
                var end = source.IndexOf('>', index + 4); if (end < 0) throw new ArgumentException("A metadata capture name is unfinished.");
                var name = source[(index + 4)..end]; AddField(name, names); output.Append("(?<").Append(name).Append('>'); index = end; continue;
            }
            if (!characterClass && source.AsSpan(index).StartsWith("(?P=", StringComparison.Ordinal))
            {
                var end = source.IndexOf(')', index + 4); if (end < 0 || !Identifier(source[(index + 4)..end])) throw new ArgumentException("A metadata named reference is invalid.");
                output.Append("\\k<").Append(source[(index + 4)..end]).Append('>'); index = end; continue;
            }
            if (!characterClass && source.AsSpan(index).StartsWith("(?<", StringComparison.Ordinal) && index + 3 < source.Length && source[index + 3] is not ('=' or '!'))
                throw new NotSupportedException("Use Python-style (?P<field>...) named metadata captures.");
            if (!characterClass && source.AsSpan(index).StartsWith("(?'", StringComparison.Ordinal))
                throw new NotSupportedException("Use Python-style named metadata captures.");
            output.Append(source[index]);
        }
        return output.ToString();
    }
    private static void AddField(string name, HashSet<string> names)
    {
        ValidateField(name); if (!names.Add(name)) throw new ArgumentException("A metadata parsing pattern repeats a capture field.");
    }
    private static void ValidateField(string name)
    {
        if (!Identifier(name)) throw new ArgumentException("Metadata capture fields must be simple names.");
        if (Reserved.Contains(name) || name.StartsWith("__", StringComparison.Ordinal) || Regex.IsMatch(name, @"\Ameta[0-9]+_", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new NotSupportedException("Parsing into source, structural, selection or stream-specific metadata fields needs additional native support.");
        if (name == "meta_") throw new ArgumentException("An embedded metadata override needs a tag name.");
    }
    private static bool Identifier(string value) => value.Length is > 0 and <= 128 && (char.IsAsciiLetter(value[0]) || value[0] == '_') && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}

public sealed class NativeMetadataParseException(string message) : Exception(message);
