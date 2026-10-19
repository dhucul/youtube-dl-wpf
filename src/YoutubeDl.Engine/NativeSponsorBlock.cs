using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine;

internal sealed record SponsorSegment(decimal Start, decimal End, string Category, string Action, string Id);
internal sealed record SponsorSpan(decimal Start, decimal End) { internal decimal Duration => End - Start; }
internal sealed record SponsorBlockPlan(string Recipe, string Video, decimal Duration, IReadOnlyList<SponsorSegment> Segments, string Digest)
{
    internal IReadOnlyList<SponsorSpan> Kept(NativeDownloadRequest request)
    {
        var remove = NativeSponsorBlock.Categories(request.SponsorBlockRemove, true);
        var cuts = Segments.Where(s => remove.Contains(s.Category) && s.Action == "skip").Select(s => s with { Start = Math.Min(s.Start, Duration), End = Math.Min(s.End, Duration) }).OrderBy(s => s.Start).ThenBy(s => s.End).ToArray();
        var kept = new List<SponsorSpan>(); decimal position = 0;
        foreach (var cut in cuts) { if (cut.Start > position) kept.Add(new(position, cut.Start)); position = Math.Max(position, cut.End); }
        if (position < Duration) kept.Add(new(position, Duration));
        if (kept.Count == 0) throw new NotSupportedException("SponsorBlock would remove the complete recording.");
        return kept;
    }
    internal bool RequestsCuts(NativeDownloadRequest request) => Segments.Any(s => s.Action == "skip" && s.End > s.Start && NativeSponsorBlock.Categories(request.SponsorBlockRemove, true).Contains(s.Category));
    internal bool Removes(NativeDownloadRequest request) => Kept(request).Sum(s => s.Duration) < Duration;
    internal IEnumerable<(decimal Start, decimal End)> Map(decimal start, decimal end, NativeDownloadRequest request)
    {
        decimal offset = 0;
        foreach (var span in Kept(request)) { var a = Math.Max(start, span.Start); var b = Math.Min(end, span.End); if (b > a) yield return (offset + a - span.Start, offset + b - span.Start); offset += span.Duration; }
    }
    internal PageMetadata Metadata(PageMetadata page, NativeDownloadRequest request)
    {
        var marks = NativeSponsorBlock.Categories(request.SponsorBlockMark, false); var removed = NativeSponsorBlock.Categories(request.SponsorBlockRemove, true);
        var entries = page.Chapters.Select(c => ((decimal)c.StartSeconds, (decimal)c.EndSeconds, c.Title)).ToList();
        foreach (var s in Segments.Where(s => marks.Contains(s.Category) && !removed.Contains(s.Category))) entries.Add((s.Start, s.End > s.Start ? s.End : Math.Min(Duration, s.Start + 0.001m), "[SponsorBlock]: " + s.Category));
        var boundaries = entries.SelectMany(e => new[] { Math.Clamp(e.Item1, 0, Duration), Math.Clamp(e.Item2, 0, Duration) }).Distinct().Order().ToArray();
        var chapters = new List<ChapterMarker>();
        for (var i = 0; i + 1 < boundaries.Length; i++)
        {
            var start = boundaries[i]; var end = boundaries[i + 1]; var labels = entries.Where(e => e.Item1 < end && e.Item2 > start).Select(e => e.Title).Distinct().ToArray(); if (labels.Length == 0) continue;
            foreach (var interval in Map(start, end, request)) chapters.Add(new() { StartSeconds = (double)interval.Start, EndSeconds = (double)interval.End, Title = string.Join(" / ", labels) });
            if (chapters.Count > 1000) throw new InvalidDataException("SponsorBlock chapter composition exceeds its limit.");
        }
        return page with { DurationSeconds = (double)Kept(request).Sum(s => s.Duration), Chapters = chapters };
    }
    internal string Captions(string text, string extension, NativeDownloadRequest request, CancellationToken ct)
    {
        if (extension is "ttml" or "dfxp") { text = TtmlReader.Read(text, ct).Render("vtt"); extension = "vtt"; }
        if (extension is not ("vtt" or "srt")) throw new NotSupportedException("SponsorBlock caption editing currently supports VTT, SRT and converted TTML.");
        var output = new StringBuilder(extension == "vtt" ? "WEBVTT\n\n" : ""); var ordinal = 0;
        foreach (var block in Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[ \t]*\n", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            ct.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(block) || block.StartsWith("WEBVTT", StringComparison.Ordinal)) continue;
            if (extension == "vtt" && (block.StartsWith("STYLE\n", StringComparison.Ordinal) || block.StartsWith("REGION\n", StringComparison.Ordinal) || block.StartsWith("NOTE", StringComparison.Ordinal))) { output.Append(block).Append("\n\n"); continue; }
            var lines = block.Trim('\n').Split('\n'); var index = lines[0].Contains("-->", StringComparison.Ordinal) ? 0 : 1;
            if (index >= lines.Length) throw new InvalidDataException("The caption omitted its timed interval.");
            var match = Regex.Match(lines[index], @"\A(?<a>[0-9:.,]+)\s+-->\s+(?<b>[0-9:.,]+)(?<settings>.*)\z");
            if (!match.Success) throw new InvalidDataException("The caption timing is not supported for SponsorBlock editing.");
            var a = Clock(match.Groups["a"].Value); var b = Clock(match.Groups["b"].Value); if (b <= a) throw new InvalidDataException("The caption interval is reversed.");
            foreach (var interval in Map(a, b, request))
            {
                if (++ordinal > 50000) throw new InvalidDataException("SponsorBlock caption editing exceeds its cue limit.");
                if (extension == "srt") output.Append(ordinal).Append('\n');
                output.Append(TtmlClock.Stamp(interval.Start, extension == "srt" ? ',' : '.')).Append(" --> ").Append(TtmlClock.Stamp(interval.End, extension == "srt" ? ',' : '.')).Append(match.Groups["settings"].Value).Append('\n').Append(string.Join('\n', lines.Skip(index + 1))).Append("\n\n");
            }
            if (output.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("SponsorBlock caption editing exceeds its output limit.");
        }
        return output.ToString();
    }
    private static decimal Clock(string text)
    {
        var parts = text.Replace(',', '.').Split(':'); if (parts.Length is < 2 or > 3) throw new InvalidDataException("The caption clock is invalid."); decimal value = 0;
        for (var i = 0; i < parts.Length; i++) { if (!decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n < 0 || i > 0 && n >= 60 || i < parts.Length - 1 && n != decimal.Truncate(n) || n > 1000000) throw new InvalidDataException("The caption clock is invalid."); value = value * 60 + n; }
        return value;
    }
}

public static class NativeSponsorBlock
{
    private static readonly string[] Names = ["sponsor", "intro", "outro", "selfpromo", "preview", "filler", "interaction", "music_offtopic", "hook", "poi_highlight", "chapter"];
    public static void Validate(string? mark, string? remove) { _ = Categories(mark, false); _ = Categories(remove, true); }
    internal static bool Enabled(NativeDownloadRequest request) => Categories(request.SponsorBlockMark, false).Count + Categories(request.SponsorBlockRemove, true).Count > 0;
    internal static HashSet<string> Categories(string? value, bool remove)
    {
        var set = new HashSet<string>(StringComparer.Ordinal); if (string.IsNullOrWhiteSpace(value)) return set;
        if (value.Length > 1024) throw new ArgumentException("SponsorBlock categories exceed their limit.");
        foreach (var raw in value.Split(',', StringSplitOptions.TrimEntries))
        {
            var exclude = raw.StartsWith('-'); var name = exclude ? raw[1..] : raw;
            if (name is "all" or "default")
            {
                foreach (var category in Names.Where(n => !remove || n is not ("poi_highlight" or "chapter"))) { if (exclude) set.Remove(category); else set.Add(category); }
                if (remove && name == "default" && !exclude) set.Remove("filler");
            }
            else { if (!Names.Contains(name) || remove && name is "poi_highlight" or "chapter") throw new ArgumentException("Choose supported SponsorBlock categories."); if (exclude) set.Remove(name); else set.Add(name); }
        }
        return set;
    }
    internal static async Task<SponsorBlockPlan> ReadAsync(NativeDownloadRequest request, PageMetadata page, MediaHttp http, string rawDirectory, CancellationToken ct)
    {
        if (page.Extractor != "youtube" || page.Id is null || !Regex.IsMatch(page.Id, @"\A[A-Za-z0-9_-]{11}\z") || page.IsLive || page.DurationSeconds is not (> 0 and <= 86400)) throw new NotSupportedException("Native SponsorBlock currently requires a complete recorded YouTube video with declared duration.");
        var recipe = DownloadWorkspace.Recipe(request); var file = Path.Combine(rawDirectory, ".sponsorblock-plan.json"); PublicationTransaction.Regular(file);
        SponsorBlockPlan plan;
        if (File.Exists(file))
        {
            if (new FileInfo(file).Length > 262144) throw new InvalidDataException("The saved SponsorBlock plan exceeds its limit.");
            plan = JsonSerializer.Deserialize<SponsorBlockPlan>(await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false)) ?? throw new InvalidDataException("The SponsorBlock plan is empty.");
        }
        else
        {
            var prefix = Hash(page.Id)[..4].ToLowerInvariant(); var categories = Categories(request.SponsorBlockMark, false).Union(Categories(request.SponsorBlockRemove, true)).Order(StringComparer.Ordinal).ToArray();
            var endpoint = new Uri("https://sponsor.ajay.app/api/skipSegments/" + prefix + "?categories=" + Uri.EscapeDataString(JsonSerializer.Serialize(categories)) + "&actionTypes=" + Uri.EscapeDataString("[\"skip\",\"poi\",\"chapter\"]"));
            using var service = http.CreatePublicServiceSession(); var segments = new List<SponsorSegment>();
            try
            {
                var document = await service.GetDocumentAsync(endpoint, 2 * 1024 * 1024, ct).ConfigureAwait(false); if (document.Url != endpoint) throw new InvalidDataException("SponsorBlock redirected its service response.");
                using var json = JsonDocument.Parse(document.Text); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 4096) throw new InvalidDataException("SponsorBlock returned an invalid prefix inventory.");
                var matches = root.EnumerateArray().Where(row => row.TryGetProperty("videoID", out var id) && id.GetString() == page.Id).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("SponsorBlock returned duplicate video identities.");
                if (matches.Length == 1)
                {
                    var rows = matches[0].GetProperty("segments"); if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 500) throw new InvalidDataException("SponsorBlock returned an invalid segment inventory.");
                    foreach (var row in rows.EnumerateArray())
                    {
                        var interval = row.GetProperty("segment"); var category = row.GetProperty("category").GetString()!; var action = row.GetProperty("actionType").GetString()!; var id = row.GetProperty("UUID").GetString()!;
                        if (interval.ValueKind != JsonValueKind.Array || interval.GetArrayLength() != 2 || !interval[0].TryGetDecimal(out var start) || !interval[1].TryGetDecimal(out var end) || start < 0 || end < start || end > (decimal)page.DurationSeconds.Value + 1 || !categories.Contains(category) || action is not ("skip" or "poi" or "chapter") || string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl)) throw new InvalidDataException("SponsorBlock returned invalid interval or category facts.");
                        if (row.TryGetProperty("videoDuration", out var duration) && (!duration.TryGetDouble(out var n) || !double.IsFinite(n) || n < 0 || n != 0 && Math.Abs(n - page.DurationSeconds.Value) > 1.1)) throw new InvalidDataException("SponsorBlock segments refer to a different video duration.");
                        segments.Add(new(start, end, category, action, id));
                    }
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { throw new InvalidDataException("SponsorBlock returned an invalid response schema.", ex); }
            plan = new(recipe, page.Id, (decimal)page.DurationSeconds.Value, segments, ""); plan = plan with { Digest = Hash(JsonSerializer.Serialize(plan)) };
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await PublicationFaults.WriteAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(plan), "sponsor-plan", ct).ConfigureAwait(false); File.Move(temporary, file); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (plan.Recipe != recipe || plan.Video != page.Id || Math.Abs(plan.Duration - (decimal)page.DurationSeconds.Value) > 0.001m || plan.Segments is null || plan.Segments.Count > 500 || plan.Digest != Hash(JsonSerializer.Serialize(plan with { Digest = "" }))) throw new InvalidDataException("The saved SponsorBlock plan changed or belongs to another recording.");
        var accepted = Categories(request.SponsorBlockMark, false).Union(Categories(request.SponsorBlockRemove, true)).ToHashSet(StringComparer.Ordinal);
        if (plan.Segments.Any(s => s is null || s.Start < 0 || s.End < s.Start || s.End > plan.Duration + 1 || s.Action is not ("skip" or "poi" or "chapter") || s.Action == "skip" && s.End <= s.Start || !accepted.Contains(s.Category) || string.IsNullOrWhiteSpace(s.Id) || s.Id.Length > 128 || s.Id.Any(char.IsControl)) || plan.Segments.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != plan.Segments.Count) throw new InvalidDataException("The SponsorBlock plan has invalid or ambiguous segment facts.");
        _ = plan.Kept(request); return plan;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
