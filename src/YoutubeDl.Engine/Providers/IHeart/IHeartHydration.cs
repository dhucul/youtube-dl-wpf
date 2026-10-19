using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.IHeart;

/// <summary>Reads literal indexed router data; never evaluates publisher JavaScript.</summary>
internal static class IHeartHydration
{
    private const string Marker = "window.__reactRouterContext.streamController.enqueue(";
    internal static Uri Api(string html, CancellationToken ct)
    {
        if (html.Length > 5 * 1024 * 1024) throw new InvalidDataException("iHeart returned excessive website data.");
        string? payload = null;
        foreach (Match script in Regex.Matches(html, @"<script(?:\s[^>]*)?>(?<body>[\s\S]*?)</script>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
        {
            ct.ThrowIfCancellationRequested(); var body = script.Groups["body"].Value.Trim();
            if (!body.StartsWith(Marker, StringComparison.Ordinal)) continue;
            if (payload is not null) throw new InvalidDataException("iHeart returned ambiguous website configuration data.");
            var start = Marker.Length; if (start >= body.Length || body[start] != '"') throw new InvalidDataException("iHeart configuration is not a literal JSON string.");
            var end = start + 1;
            for (; end < body.Length; end++)
            {
                if (body[end] == '\\') { end++; continue; }
                if (body[end] == '"') break;
            }
            if (end >= body.Length || body[(end + 1)..].Trim() != ");") throw new InvalidDataException("iHeart configuration uses an unsupported computed script layout.");
            try { payload = JsonSerializer.Deserialize<string>(body[start..(end + 1)]); }
            catch (JsonException) { throw new InvalidDataException("iHeart configuration is not a supported JSON string."); }
        }
        if (payload is null) throw new NotSupportedException("This iHeart website layout requires additional native configuration support.");
        using var document = Parse(payload, ct); var table = document.RootElement;
        if (table.ValueKind != JsonValueKind.Array || table.GetArrayLength() is < 2 or > 100000) throw new InvalidDataException("iHeart returned an invalid indexed configuration table.");
        var loader = Member(0, "loaderData"); var root = Member(loader, "root"); var config = Member(root, "config"); var api = Member(config, "api");
        var amp = Member(api, "amp"); var client = Text(Member(amp, "clientEndpoint")); var declared = Text(Member(api, "apiUrl"));
        if (client != declared || !Uri.TryCreate(client, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" || !endpoint.IsDefaultPort ||
            endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !Regex.IsMatch(endpoint.Host, @"\A(?:[a-z]{2}\.)?api\.iheart\.com\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("iHeart did not advertise a consistent public API origin.");
        return endpoint;

        JsonElement Node(int index) => index >= 0 && index < table.GetArrayLength() ? table[index] : throw new InvalidDataException("iHeart configuration has an invalid data reference.");
        string Text(int index) => Node(index).ValueKind == JsonValueKind.String ? Node(index).GetString()! : throw new InvalidDataException("iHeart configuration contains an invalid literal string.");
        int Member(int index, string name)
        {
            var node = Node(index); if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("iHeart configuration has an invalid object reference.");
            int? found = null; var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!property.Name.StartsWith('_') || !int.TryParse(property.Name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var key)) throw new InvalidDataException("iHeart configuration contains an unsupported indexed key.");
                var keyName = Text(key); if (!names.Add(keyName)) throw new InvalidDataException("iHeart configuration repeats a resolved field name.");
                if (keyName != name) continue;
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value)) throw new InvalidDataException("iHeart configuration contains an invalid indexed value.");
                found = value;
            }
            return found ?? throw new InvalidDataException("iHeart configuration omitted a required API field.");
        }
    }

    internal static JsonDocument Parse(string text, CancellationToken ct)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(text, new() { MaxDepth = 64 }); }
        catch (JsonException) { throw new InvalidDataException("iHeart returned malformed JSON metadata."); }
        try
        {
            var pending = new Stack<JsonElement>(); pending.Push(document.RootElement); var count = 0;
            while (pending.TryPop(out var node))
            {
                ct.ThrowIfCancellationRequested(); if (++count > 200000) throw new InvalidDataException("iHeart returned excessive metadata fields.");
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var field in node.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("iHeart returned repeated JSON fields."); pending.Push(field.Value); }
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) pending.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
