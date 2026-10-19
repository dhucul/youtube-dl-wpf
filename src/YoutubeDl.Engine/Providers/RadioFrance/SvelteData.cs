using System.Text.Json;

namespace YoutubeDl.Engine.Providers.RadioFrance;

/// <summary>Reads bounded JSON references from the publisher's data route; no JavaScript runs.</summary>
internal static class SvelteData
{
    internal static JsonElement[] Decode(JsonElement envelope)
    {
        if (ProviderJson.Text(envelope, "type") != "data") throw new InvalidDataException("Radio France returned a non-data response.");
        var result = new List<JsonElement>();
        foreach (var node in PublicProviderApi.Rows(envelope, "nodes", 32))
        {
            if (node.ValueKind == JsonValueKind.Null) continue;
            if (ProviderJson.Text(node, "type") != "data" || !node.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 100000) throw new InvalidDataException("Radio France returned an incomplete JSON data node.");
            using var memory = new MemoryStream(); using (var writer = new Utf8JsonWriter(memory))
            {
                var visiting = new HashSet<int>(); var values = 0; Write(0, 0); writer.Flush();
                void Write(int index, int depth)
                {
                    if (++values > 100000 || depth > 64 || memory.Length + writer.BytesPending > 8 * 1024 * 1024) throw new InvalidDataException("Radio France exceeded the bounded JSON reference graph.");
                    if (index is -1 or -2) { writer.WriteNullValue(); return; }
                    if (index < 0 || index >= data.GetArrayLength() || !visiting.Add(index)) throw new InvalidDataException("Radio France returned a cyclic or invalid JSON reference.");
                    var value = data[index];
                    switch (value.ValueKind)
                    {
                        case JsonValueKind.Object:
                            writer.WriteStartObject(); var names = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var field in value.EnumerateObject()) { if (!names.Add(field.Name) || field.Name.Length > 512) throw new InvalidDataException("Radio France repeated a JSON field."); writer.WritePropertyName(field.Name); Write(Reference(field.Value), depth + 1); }
                            writer.WriteEndObject(); break;
                        case JsonValueKind.Array:
                            if (value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.String)
                            {
                                if (value.GetArrayLength() != 2 || value[0].GetString() != "Date" || value[1].ValueKind != JsonValueKind.String || PublicProviderApi.Date(value[1].GetString()) is null) throw new NotSupportedException("Radio France returned an unsupported tagged JSON value."); writer.WriteStringValue(value[1].GetString());
                            }
                            else { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(Reference(item), depth + 1); writer.WriteEndArray(); }
                            break;
                        case JsonValueKind.String:
                            if (value.GetString()!.Length > 1024 * 1024) throw new InvalidDataException("Radio France returned an oversized JSON string."); value.WriteTo(writer); break;
                        case JsonValueKind.Number: case JsonValueKind.True: case JsonValueKind.False: case JsonValueKind.Null: value.WriteTo(writer); break;
                        default: throw new InvalidDataException("Radio France returned an unsupported JSON value.");
                    }
                    visiting.Remove(index);
                }
            }
            if (memory.Length > 8 * 1024 * 1024) throw new InvalidDataException("Radio France expanded an oversized JSON data node.");
            using var document = JsonDocument.Parse(memory.ToArray()); result.Add(document.RootElement.Clone());
        }
        return result.ToArray();
    }
    private static int Reference(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index) ? index : throw new InvalidDataException("Radio France omitted a JSON reference index.");
}
