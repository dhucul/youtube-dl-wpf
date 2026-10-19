using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Providers;

namespace YoutubeDl.Engine.Discovery;

/// <summary>Reads local Schema.org media declarations without loading remote contexts or executing scripts.</summary>
internal sealed partial class StructuredMediaReader
{
    private sealed record Node(JsonElement Value, int Block, bool Schema, bool Auxiliary);
    private readonly Uri _page;
    private readonly CancellationToken _ct;
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<string, Node> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _conflicts = new(StringComparer.Ordinal);
    private StructuredMediaReader(Uri page, CancellationToken ct) { _page = page; _ct = ct; }

    internal static (PageMetadata? Media, string RemainingHtml) Read(string html, Uri page, int maximumEntries, CancellationToken ct)
    {
        var reader = new StructuredMediaReader(page, ct); var cleaned = new StringBuilder(); var position = 0; var block = 0; var templates = 0;
        var documents = new List<JsonDocument>();
        try
        {
            foreach (Match token in Regex.Matches(html, @"<!--.*?-->|<textarea\b[^>]*>.*?</textarea\s*>|</?template\b[^>]*>|<script\b(?<attributes>[^>]{0,8192})>(?<body>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                ct.ThrowIfCancellationRequested(); if (templates == 0) cleaned.Append(html, position, token.Index - position); position = token.Index + token.Length;
                if (token.Value.StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (token.Value.StartsWith("<textarea", StringComparison.OrdinalIgnoreCase)) continue;
                if (token.Value.StartsWith("<template", StringComparison.OrdinalIgnoreCase)) { templates++; continue; }
                if (token.Value.StartsWith("</template", StringComparison.OrdinalIgnoreCase)) { templates = Math.Max(0, templates - 1); continue; }
                var type = Regex.Match(token.Groups["attributes"].Value, @"\btype\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (!type.Success || !type.Groups["value"].Value.Split(';')[0].Trim().Equals("application/ld+json", StringComparison.OrdinalIgnoreCase)) { if (templates == 0) cleaned.Append(token.Value); continue; }
                if (templates > 0) continue;
                if (++block > 64 || token.Groups["body"].Length > 2 * 1024 * 1024) throw new InvalidDataException("Structured media metadata exceeds the supported document limit.");
                JsonDocument document;
                try { document = JsonDocument.Parse(token.Groups["body"].Value); }
                catch (JsonException) { continue; } // Malformed metadata never falls back to loose URL extraction.
                documents.Add(document); reader.Index(document.RootElement, block, false, false);
            }
            if (templates == 0) cleaned.Append(html, position, html.Length - position);
            return (reader.Build(maximumEntries), cleaned.ToString());
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }
    private void Index(JsonElement value, int block, bool schema, bool auxiliary)
    {
        _ct.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) Index(child, block, schema, auxiliary); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        if (_nodes.Count >= 10000) throw new InvalidDataException("Structured media metadata contains too many objects.");
        if (value.TryGetProperty("@context", out var context)) schema = SchemaContext(context, schema);
        var node = new Node(value, block, schema, auxiliary); _nodes.Add(node);
        if (Text(value, "@id") is { } id && value.EnumerateObject().Any(property => property.Name != "@id"))
        {
            var key = Key(id, block);
            if (_ids.TryGetValue(key, out var prior))
            { if (!JsonElement.DeepEquals(prior.Value, value)) _conflicts.Add(key); }
            else _ids[key] = node;
        }
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == "@context") continue;
            var childAuxiliary = auxiliary || property.Name is "encoding" or "associatedMedia" or "thumbnail" or "image" or "logo" or "caption" or "trailer" or "potentialAction" or "comment" or "review" or "mentions";
            Index(property.Value, block, schema, childAuxiliary);
        }
    }
    private PageMetadata? Build(int maximumEntries)
    {
        var encodings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parent in _nodes)
            foreach (var property in new[] { "encoding", "associatedMedia" })
                if (parent.Value.TryGetProperty(property, out var values))
                    foreach (var value in Values(values))
                        if ((value.ValueKind == JsonValueKind.String ? value.GetString() : Text(value, "@id")) is { } id) encodings.Add(Key(id, parent.Block));
        var all = _nodes.Where(node => MediaType(node) is not null && !node.Auxiliary && (Text(node.Value, "@id") is not { } id || !encodings.Contains(Key(id, node.Block)))).ToArray();
        if (all.Length == 0) return null;
        var selected = new List<Node>(); var seen = new HashSet<string>(StringComparer.Ordinal); var missingMain = false;
        var anchored = _page.Fragment.Length > 0 && _ids.TryGetValue(_page.AbsoluteUri, out var target) && MediaType(target) is not null ? target : null;
        if (anchored is not null) Add(anchored);
        foreach (var node in _nodes.Where(node => anchored is null && !node.Auxiliary && (HasType(node, "WebPage") || HasType(node, "Article") || HasType(node, "NewsArticle"))))
        {
            if (Text(node.Value, "url") is { } pageUrl && Url(pageUrl) is { } declared && !SamePage(declared, _page)) continue;
            if (node.Value.TryGetProperty("mainEntity", out var main)) Select(main, node, 0, new HashSet<string>(StringComparer.Ordinal));
        }
        if (missingMain) return new() { Extractor = "generic:jsonld", SourcePageUrl = _page, Warnings = ["The page's declared main media reference could not be resolved locally."] };
        if (selected.Count == 0) foreach (var node in all) Add(node);
        if (selected.Count > maximumEntries) throw new InvalidDataException("The structured media collection exceeds the configured limit; it was not truncated.");
        var pages = selected.Select(ReadMedia).ToArray();
        if (pages.Length == 1) return pages[0];
        var entries = pages.Select((page, index) => new NativePlaylistEntry(index + 1, page.Id, page.Title,
            page.Media.Count > 0 ? _page : page.EmbeddedPages.Count == 1 ? page.EmbeddedPages[0] : _page, page.DurationSeconds, page.ThumbnailUrl,
            page.Media.Count > 0 || page.EmbeddedPages.Count == 1,
            page.Media.Count > 0 || page.EmbeddedPages.Count != 1 ? page : null)).ToArray();
        return new() { Id = Hash(_page.AbsoluteUri), Extractor = "generic:jsonld", IsPlaylist = true, Entries = entries, SourcePageUrl = _page };

        void Add(Node node)
        {
            var key = Text(node.Value, "@id") is { } id ? Key(id, node.Block) : node.Value.GetRawText();
            if (_conflicts.Contains(key)) throw new InvalidDataException("Structured media contains conflicting definitions for one identifier.");
            if (seen.Add(key)) selected.Add(node);
        }
        void Select(JsonElement value, Node parent, int depth, HashSet<string> visiting)
        {
            if (depth > 16) throw new InvalidDataException("Structured main-entity references are too deeply nested.");
            if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) Select(item, parent, depth + 1, visiting); return; }
            var node = Resolve(value, parent);
            if (node is null || value.ValueKind == JsonValueKind.Object && Text(value, "@id") is { } reference && value.EnumerateObject().Count() == 1 && !_ids.ContainsKey(Key(reference, parent.Block)))
            { missingMain = true; return; }
            var key = Text(node.Value, "@id") is { } id ? Key(id, node.Block) : node.Value.GetRawText();
            if (!visiting.Add(key)) throw new InvalidDataException("Structured main-entity references are recursive.");
            try
            {
                if (MediaType(node) is not null) { Add(node); return; }
                foreach (var name in new[] { "mainEntity", "video", "audio" }) if (node.Value.TryGetProperty(name, out var child)) Select(child, node, depth + 1, visiting);
            }
            finally { visiting.Remove(key); }
        }
    }
    private Node? Resolve(JsonElement value, Node parent)
    {
        var id = value.ValueKind == JsonValueKind.String ? value.GetString() : Text(value, "@id");
        if (id is not null)
        {
            var key = Key(id, parent.Block);
            if (_conflicts.Contains(key)) throw new InvalidDataException("Structured media references conflicting definitions for one identifier.");
            if (_ids.TryGetValue(key, out var found)) return found;
        }
        return value.ValueKind == JsonValueKind.Object ? new(value, parent.Block, value.TryGetProperty("@context", out var context) ? SchemaContext(context, parent.Schema) : parent.Schema, false) : null;
    }
    private string Key(string id, int block)
    {
        if (id.Length > 4096 || id.Any(char.IsControl)) throw new InvalidDataException("A structured media identifier is invalid or excessive.");
        return id.StartsWith("_:", StringComparison.Ordinal) ? _page.AbsoluteUri + "#block:" + block + ":" + id : Uri.TryCreate(_page, id, out var uri) ? uri.AbsoluteUri : _page.AbsoluteUri + "#" + id;
    }
    private static bool SchemaContext(JsonElement value, bool inherited = false)
    {
        if (value.ValueKind == JsonValueKind.Array)
        { foreach (var context in value.EnumerateArray()) inherited = SchemaContext(context, inherited); return inherited; }
        if (value.ValueKind == JsonValueKind.Object) return value.TryGetProperty("@vocab", out var vocabulary) ? SchemaContext(vocabulary) : inherited;
        return value.ValueKind == JsonValueKind.String && value.GetString()?.TrimEnd('/') is "https://schema.org" or "http://schema.org";
    }
    private static bool HasType(Node node, string name)
    {
        if (!node.Value.TryGetProperty("@type", out var type)) return false;
        return type.ValueKind == JsonValueKind.String ? Matches(type.GetString()) : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && Matches(item.GetString()));
        bool Matches(string? value) => value == "https://schema.org/" + name || value == "http://schema.org/" + name || node.Schema && value == name;
    }
    private static string? MediaType(Node node)
    {
        var video = HasType(node, "VideoObject"); var audio = HasType(node, "AudioObject");
        if (video && audio) throw new InvalidDataException("A structured media object declares conflicting audio/video roles.");
        return video ? "video" : audio ? "audio" : null;
    }
    private static string? Text(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var item)) return null;
        return item.ValueKind == JsonValueKind.String ? item.GetString() : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("@value", out var literal) && literal.ValueKind == JsonValueKind.String ? literal.GetString() : null;
    }
    private Uri? Url(string? text) => !string.IsNullOrWhiteSpace(text) && Uri.TryCreate(_page, text, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : null;
    private static bool SamePage(Uri first, Uri second) => new UriBuilder(first) { Fragment = "" }.Uri == new UriBuilder(second) { Fragment = "" }.Uri;
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
