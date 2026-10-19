using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Pinterest;

/// <summary>Reads literal arguments in public completed-request declarations; executes no JavaScript.</summary>
internal static class PinterestRelayReader
{
    private static readonly string[] Fields = ["entityId", "id", "__typename", "isHidden", "videos", "storyPinData", "carouselData", "embed", "title", "gridTitle", "description",
        "closeupUnifiedTitle", "closeupUnifiedDescription", "pinner", "nativeCreator", "images_orig", "images_736x"];
    internal static JsonDocument Read(string html, string id)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        var scripts = Regex.Matches(clean, @"<script\b[^>]*\bdata-relay-completed-request\s*=\s*([""'])true\1[^>]*>(?<body>.*?)</script\s*>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        if (scripts.Count is < 1 or > 128) throw new NotSupportedException("Pinterest did not expose a supported complete public pin response.");
        var result = new JsonObject(); var records = 0;
        foreach (Match script in scripts)
        {
            var body = script.Groups["body"].Value.Trim();
            const string prefix = "window.__PWS_RELAY_REGISTER_COMPLETED_REQUEST__(";
            if (!body.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Pinterest returned an unsupported completed-response declaration.");
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body[prefix.Length..]);
                var reader = new Utf8JsonReader(bytes, new() { MaxDepth = 48 });
                using var literal = JsonDocument.ParseValue(ref reader);
                if (literal.RootElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("Pinterest returned a nonliteral public query identity.");
                using var query = JsonDocument.Parse(Uri.UnescapeDataString(literal.RootElement.GetString()!), new() { MaxDepth = 32 });
                var tail = bytes.AsSpan((int)reader.BytesConsumed).TrimStartAscii();
                if (tail.Length == 0 || tail[0] != ',') throw new InvalidDataException("Pinterest omitted its public response argument.");
                var responseReader = new Utf8JsonReader(tail[1..], new() { MaxDepth = 48 });
                using var response = JsonDocument.ParseValue(ref responseReader);
                if (!tail[(1 + (int)responseReader.BytesConsumed)..].TrimAscii().SequenceEqual(");"u8))
                    throw new InvalidDataException("Pinterest returned executable or extra public response arguments.");
                if (ProviderJson.Text(ProviderJson.Object(query.RootElement, "variables"), "pinId") != id) continue;
                if (ProviderJson.Array(response.RootElement, "errors").Length > 0) throw new NotSupportedException("Pinterest refused its public pin response.");
                var extensions = ProviderJson.Object(response.RootElement, "extensions");
                if (extensions.TryGetPropertySafe("is_final", out var complete) && complete.ValueKind != JsonValueKind.True)
                    throw new NotSupportedException("Pinterest did not provide a completed pin response.");
                var pin = ProviderJson.Object(ProviderJson.Object(ProviderJson.Object(response.RootElement, "data"), "v3GetPinQueryv2"), "data");
                if (ProviderJson.Text(pin, "entityId") != id || ProviderJson.Text(pin, "__typename") != "Pin")
                    throw new InvalidDataException("Pinterest returned another pin identity or an unsupported record.");
                var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("Pin:" + id));
                if (ProviderJson.Text(pin, "id") != expected) throw new InvalidDataException("Pinterest returned conflicting native/global pin identities.");
                var selected = new JsonObject();
                foreach (var field in Fields)
                    if (pin.TryGetProperty(field, out var value))
                    {
                        // Keep separate story projections until page/block and
                        // supplied media identities have been checked by the reader.
                        if (field == "storyPinData" && value.ValueKind == JsonValueKind.Object)
                        {
                            if (result["__storyProjections"] is not JsonArray projections) result["__storyProjections"] = projections = new JsonArray();
                            projections.Add(JsonNode.Parse(value.GetRawText()));
                        }
                        else if (field == "carouselData" && value.ValueKind == JsonValueKind.Object)
                            selected[field] = new JsonObject { ["unsupportedComposition"] = true };
                        else selected[field] = JsonNode.Parse(value.GetRawText());
                    }
                Merge(result, selected); records++;
            }
            catch (JsonException) { throw new InvalidDataException("Pinterest returned invalid public query/response JSON."); }
        }
        if (records == 0) throw new NotSupportedException("Pinterest supplied no completed response for the requested pin.");
        return JsonDocument.Parse(result.ToJsonString(), new() { MaxDepth = 48 });
    }
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is null) continue;
            if (!target.TryGetPropertyValue(property.Key, out var existing) || existing is null) target[property.Key] = property.Value.DeepClone();
            else if (existing is JsonObject oldObject && property.Value is JsonObject newObject) Merge(oldObject, newObject);
            else if (!JsonNode.DeepEquals(existing, property.Value)) throw new InvalidDataException("Pinterest returned conflicting pin metadata/media declarations.");
        }
    }
    private static bool TryGetPropertySafe(this JsonElement element, string name, out JsonElement value)
    { value = default; return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value); }
    private static ReadOnlySpan<byte> TrimStartAscii(this ReadOnlySpan<byte> value)
    { while (value.Length > 0 && value[0] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') value = value[1..]; return value; }
    private static ReadOnlySpan<byte> TrimAscii(this ReadOnlySpan<byte> value)
    { value = value.TrimStartAscii(); while (value.Length > 0 && value[^1] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t') value = value[..^1]; return value; }
}
