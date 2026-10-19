using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace YoutubeDl.Engine.Media;

internal sealed record TtmlCue(decimal Start, decimal End, string Text);
internal sealed record TtmlDocument(IReadOnlyList<TtmlCue> Cues)
{
    internal string Render(string format)
    {
        if (format is not ("vtt" or "srt")) throw new ArgumentException("Choose VTT or SRT for native TTML rendering.");
        var output = new StringBuilder(format == "vtt" ? "WEBVTT\n\n" : "");
        for (var index = 0; index < Cues.Count; index++)
        {
            var cue = Cues[index];
            if (format == "srt") output.Append(index + 1).Append('\n');
            output.Append(TtmlClock.Stamp(cue.Start, format == "srt" ? ',' : '.')).Append(" --> ")
                .Append(TtmlClock.Stamp(cue.End, format == "srt" ? ',' : '.')).Append('\n').Append(cue.Text).Append("\n\n");
            if (output.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("The converted TTML document exceeds its output limit.");
        }
        var result = output.ToString();
        if (Encoding.UTF8.GetByteCount(result) > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("The converted TTML document exceeds its output limit.");
        return result;
    }
}

internal static class TtmlReader
{
    internal static TtmlDocument Read(string xml, CancellationToken ct, decimal? windowEnd = null, bool roundTimes = true, bool allowEmpty = false)
    {
        ct.ThrowIfCancellationRequested();
        if (windowEnd is <= 0 or > TtmlClock.MaximumSeconds) throw new InvalidDataException("The TTML sample window is invalid.");
        if (Encoding.UTF8.GetByteCount(xml) > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("The TTML document exceeds its input limit.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = FfmpegTool.MaximumSubtitleBytes, IgnoreComments = true };
        try
        {
            // Bound structure and cancellation before constructing the in-memory tree.
            using (var bounded = XmlReader.Create(new StringReader(xml), settings))
            {
                var count = 0;
                while (bounded.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (++count > 50000 || bounded.Depth > 64) throw new InvalidDataException("The TTML document exceeds its structure limit.");
                }
            }
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            if (root is null || root.Name.LocalName != "tt" || !TtmlClock.IsDocumentNamespace(root.Name.NamespaceName)) throw new InvalidDataException("The subtitle document is not supported TTML/DFXP.");
            var state = new State(root, ct, windowEnd, roundTimes, allowEmpty);
            return state.Read(root);
        }
        catch (XmlException ex) { throw new InvalidDataException("The TTML document is not valid bounded XML.", ex); }
    }

    private sealed class Node(string name, decimal start, decimal? end, TtmlStyle style)
    {
        internal string Name { get; } = name;
        internal decimal Start { get; } = start;
        internal decimal? End { get; set; } = end;
        internal TtmlStyle Style { get; } = style;
        internal List<object> Content { get; } = [];
    }
    private readonly record struct Glyph(char Character, TtmlStyle Style);

    private sealed class State(XElement root, CancellationToken ct, decimal? windowEnd, bool roundTimes, bool allowEmpty)
    {
        private readonly TtmlClock _clock = new(root);
        private readonly TtmlStyles _styles = new(root);
        private readonly List<Node> _paragraphs = [];
        private int _work;

        internal TtmlDocument Read(XElement root)
        {
            var bodies = root.Elements().Where(element => element.Name.LocalName == "body" && TtmlClock.IsDocumentNamespace(element.Name.NamespaceName)).ToArray();
            if (bodies.Length != 1) throw new InvalidDataException("TTML requires one supported body element.");
            if (root.Attributes().Any(attribute => attribute.Name.LocalName is "begin" or "end" or "dur")) throw new NotSupportedException("Timing on the TTML root requires additional support.");
            Build(bodies[0], 0, windowEnd, _styles.Apply(root, new()), 0);
            var cues = new List<TtmlCue>(); var payloadBytes = 0;
            foreach (var paragraph in _paragraphs)
            {
                Touch();
                if (paragraph.Style.DisplayNone) continue;
                if (paragraph.End is not { } end) throw new NotSupportedException("TTML text has an unbounded end time; supply a finite timed document.");
                if (end <= paragraph.Start) continue;
                var boundaries = new SortedSet<decimal> { paragraph.Start, end };
                Boundaries(paragraph, paragraph.Start, end, boundaries);
                var points = boundaries.ToArray();
                TtmlCue? previous = null;
                for (var index = 0; index + 1 < points.Length; index++)
                {
                    var glyphs = new List<Glyph>();
                    Render(paragraph, points[index], glyphs);
                    var text = Text(glyphs);
                    if (text.Length == 0) { previous = null; continue; }
                    var start = roundTimes ? decimal.Round(points[index], 3, MidpointRounding.AwayFromZero) : points[index];
                    var finish = roundTimes ? decimal.Round(points[index + 1], 3, MidpointRounding.AwayFromZero) : points[index + 1];
                    if (finish <= start) throw new NotSupportedException("A TTML cue is shorter than the output's millisecond precision.");
                    if (previous is not null && previous.End == start && previous.Text == text)
                    {
                        previous = previous with { End = finish }; cues[^1] = previous;
                    }
                    else
                    {
                        if (cues.Count >= 10000) throw new InvalidDataException("TTML conversion exceeds the cue limit.");
                        payloadBytes += Encoding.UTF8.GetByteCount(text);
                        if (payloadBytes > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("TTML cue text exceeds its aggregate output limit.");
                        previous = new(start, finish, text); cues.Add(previous);
                    }
                }
            }
            if (cues.Count == 0 && !allowEmpty) throw new InvalidDataException("TTML conversion found no finite visible caption cues.");
            return new(cues.OrderBy(cue => cue.Start).ToArray());
        }

        private Node Build(XElement element, decimal reference, decimal? parentEnd, TtmlStyle inherited, int depth)
        {
            Touch();
            if (depth > 64) throw new InvalidDataException("The TTML timing tree is too deeply nested.");
            if (!TtmlClock.IsDocumentNamespace(element.Name.NamespaceName) || element.Name.LocalName is not ("body" or "div" or "p" or "span" or "br"))
                throw new NotSupportedException("The TTML content includes an unsupported element or animation.");
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.LocalName is "condition" or "backgroundImage" or "repeatCount" or "repeatDur" or "fill")
                    throw new NotSupportedException("Conditional, image, repeated or frozen TTML content requires additional support.");
                if (attribute.Name.LocalName is "begin" or "end" or "dur" or "timeContainer" && attribute.Name.NamespaceName.Length > 0)
                    throw new NotSupportedException("The TTML timing attribute uses an unsupported namespace.");
            }
            var name = element.Name.LocalName;
            var begin = (string?)element.Attribute("begin"); var endValue = (string?)element.Attribute("end"); var duration = (string?)element.Attribute("dur");
            if (name == "br" && (begin is not null || endValue is not null || duration is not null)) throw new NotSupportedException("Timed TTML line breaks require additional support.");
            var start = Add(reference, begin is null ? 0 : _clock.Parse(begin));
            var localEnd = endValue is null ? (decimal?)null : Add(reference, _clock.Parse(endValue));
            if (localEnd < start) throw new InvalidDataException("A TTML end time precedes its start.");
            if (duration is not null) localEnd = Min(localEnd, Add(start, _clock.Parse(duration)));
            var node = new Node(name, start, Min(localEnd, parentEnd), _styles.Apply(element, inherited));
            var mode = (string?)element.Attribute("timeContainer") ?? "par";
            if (mode is not ("par" or "seq")) throw new NotSupportedException("The TTML time container is unsupported.");
            decimal? cursor = start;
            foreach (var content in element.Nodes())
            {
                Touch();
                if (content is XText text)
                {
                    if (name is "p" or "span") node.Content.Add(text.Value);
                    else if (!string.IsNullOrWhiteSpace(text.Value)) throw new InvalidDataException("TTML text must belong to a paragraph or span.");
                }
                else if (content is XElement child)
                {
                    if (child.Name.LocalName == "metadata" && TtmlClock.IsDocumentNamespace(child.Name.NamespaceName) || child.Name.NamespaceName.EndsWith("#metadata", StringComparison.Ordinal)) continue;
                    if (name == "br" || name is "p" or "span" && child.Name.LocalName is not ("span" or "br")) throw new NotSupportedException("The TTML inline structure is unsupported.");
                    var referenceBegin = mode == "seq" && child.Name.LocalName != "br" ? cursor ?? throw new NotSupportedException("A sequential TTML child has an unbounded predecessor.") : start;
                    var resolved = Build(child, referenceBegin, node.End, node.Style, depth + 1);
                    node.Content.Add(resolved);
                    if (mode == "seq" && child.Name.LocalName != "br") cursor = resolved.End;
                }
            }
            var children = node.Content.OfType<Node>().Where(child => child.Name != "br").ToArray();
            if (localEnd is null && (name is "body" or "div" || children.Length > 0 && !node.Content.OfType<string>().Any(text => !string.IsNullOrWhiteSpace(text))))
            {
                decimal? implicitEnd = children.Any(child => child.End is null) ? null : children.Length == 0 ? start : children.Max(child => child.End!.Value);
                node.End = Min(node.End, implicitEnd);
            }
            if (name == "p") _paragraphs.Add(node);
            return node;
        }

        private void Boundaries(Node node, decimal start, decimal end, SortedSet<decimal> result)
        {
            Touch();
            if (node.Start > start && node.Start < end) result.Add(node.Start);
            if (node.End is { } finish && finish > start && finish < end) result.Add(finish);
            foreach (var child in node.Content.OfType<Node>()) Boundaries(child, start, end, result);
        }
        private void Render(Node node, decimal instant, List<Glyph> glyphs)
        {
            Touch();
            if (instant < node.Start || node.End <= instant || node.Style.DisplayNone) return;
            if (node.Name == "br") { glyphs.Add(new('\n', node.Style)); return; }
            foreach (var content in node.Content)
            {
                if (content is Node child) Render(child, instant, glyphs);
                else if (content is string text && !node.Style.Hidden && !node.Style.Transparent)
                    foreach (var character in text)
                    {
                        Touch();
                        if ((glyphs.Count & 4095) == 0) ct.ThrowIfCancellationRequested();
                        if (glyphs.Count >= FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("A TTML cue exceeds its text limit.");
                        var value = !node.Style.Preserve && character is '\r' or '\n' or '\t' ? ' ' : character;
                        glyphs.Add(new(value, node.Style));
                    }
            }
        }

        private static string Text(List<Glyph> raw)
        {
            var glyphs = new List<Glyph>();
            foreach (var glyph in raw)
            {
                if (glyph.Character == '\n')
                {
                    while (glyphs.Count > 0 && glyphs[^1].Character == ' ' && !glyphs[^1].Style.Preserve) glyphs.RemoveAt(glyphs.Count - 1);
                    glyphs.Add(glyph); continue;
                }
                if (!glyph.Style.Preserve && glyph.Character == ' ' && (glyphs.Count == 0 || glyphs[^1].Character is ' ' or '\n')) continue;
                glyphs.Add(glyph);
            }
            while (glyphs.Count > 0 && glyphs[^1].Character == ' ' && !glyphs[^1].Style.Preserve) glyphs.RemoveAt(glyphs.Count - 1);
            if (!glyphs.Any(glyph => !char.IsWhiteSpace(glyph.Character))) return "";
            var output = new StringBuilder(); var current = (Bold: false, Italic: false, Underline: false);
            foreach (var glyph in glyphs)
            {
                var next = (glyph.Style.Bold, glyph.Style.Italic, glyph.Style.Underline);
                if (next != current) { Close(current); Open(next); current = next; }
                output.Append(glyph.Character switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => glyph.Character.ToString() });
                if (output.Length > FfmpegTool.MaximumSubtitleBytes) throw new InvalidDataException("A rendered TTML cue exceeds its output limit.");
            }
            Close(current);
            // Empty payload lines terminate WebVTT/SRT cues; a nonbreaking space
            // preserves an intentional blank rendered line without ending the cue.
            return string.Join('\n', output.ToString().Split('\n').Select(line => line.Length == 0 ? "\u00a0" : line));
            void Open((bool Bold, bool Italic, bool Underline) style) { if (style.Bold) output.Append("<b>"); if (style.Italic) output.Append("<i>"); if (style.Underline) output.Append("<u>"); }
            void Close((bool Bold, bool Italic, bool Underline) style) { if (style.Underline) output.Append("</u>"); if (style.Italic) output.Append("</i>"); if (style.Bold) output.Append("</b>"); }
        }
        private void Touch() { ct.ThrowIfCancellationRequested(); if (++_work > 8_000_000) throw new InvalidDataException("TTML conversion exceeds its processing limit."); }
        private static decimal Add(decimal first, decimal second) => first + second is var result && result <= TtmlClock.MaximumSeconds ? result : throw new InvalidDataException("TTML nesting exceeds its timestamp range.");
        private static decimal? Min(decimal? first, decimal? second) => first is null ? second : second is null ? first : Math.Min(first.Value, second.Value);
    }
}
