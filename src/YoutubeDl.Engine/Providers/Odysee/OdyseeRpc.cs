using System.Text.Json;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Odysee;

internal static class OdyseeRpc
{
    internal static async Task<JsonElement> CallAsync(string method, object parameters, MediaHttp http, CancellationToken ct)
    {
        if (method is not ("resolve" or "claim_search" or "get")) throw new ArgumentException("Unsupported read-only Odysee operation.");
        var json = await http.PostJsonAsync(new("https://api.na-backend.odysee.com/api/v1/proxy?m=" + method),
            new { jsonrpc = "2.0", id = 1, method, @params = parameters }, 8 * 1024 * 1024, null, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (Text(root, "jsonrpc") != "2.0" || Integer(root, "id") != 1) throw new InvalidDataException("Odysee returned an unrelated RPC response.");
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) throw new NotSupportedException("Odysee could not complete the requested " + method + " operation.");
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Odysee did not return the expected metadata object.");
        return result.Clone();
    }
    internal static void CheckSearch(JsonElement result)
    {
        var blocked = Object(result, "blocked");
        if (Long(blocked, "total") is > 0 || Array(blocked, "channels").Length > 0) throw new NotSupportedException("Odysee withheld part of this result; the native engine cannot present it as a complete collection.");
        if (!result.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Odysee did not return a claim list.");
    }
}
