using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.SoundCloud;

internal sealed record SoundCloudPage(string? ClientId, JsonElement Item, JsonElement User)
{
    internal static SoundCloudPage Read(string html)
    {
        if (html.Length > 8 * 1024 * 1024) throw new InvalidDataException("The SoundCloud page exceeds the extraction limit.");
        var match = Regex.Match(html, @"\b(?:window\s*\.\s*)?__sc_hydration\s*=\s*(?<value>\[)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new NotSupportedException("The SoundCloud page did not expose its playback data.");
        var bytes = Encoding.UTF8.GetBytes(html[match.Groups["value"].Index..]);
        var reader = new Utf8JsonReader(bytes);
        using var document = JsonDocument.ParseValue(ref reader);
        if (reader.BytesConsumed > 4 * 1024 * 1024 || document.RootElement.GetArrayLength() > 100)
            throw new InvalidDataException("The SoundCloud playback data exceeds the extraction limit.");
        string? client = null; JsonElement item = default; JsonElement user = default;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var kind = Text(entry, "hydratable"); var data = Object(entry, "data");
            if (kind == "apiClient")
            {
                if (client is not null || Text(data, "id") is not { } id || !Regex.IsMatch(id, @"\A[A-Za-z0-9]{32}\z"))
                    throw new InvalidDataException("The SoundCloud page returned an invalid or repeated public client identifier.");
                client = id;
            }
            if (kind == "user")
            {
                if (user.ValueKind != JsonValueKind.Undefined || data.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The SoundCloud page returned ambiguous profile data.");
                user = data.Clone();
            }
            if (kind is "sound" or "playlist")
            {
                if (item.ValueKind != JsonValueKind.Undefined || data.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The SoundCloud page returned ambiguous media data.");
                item = data.Clone();
            }
        }
        return new(client, item, user);
    }
}
