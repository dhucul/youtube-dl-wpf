using System.Text;
using YoutubeDl.Engine.Protocols;

namespace YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubeAudioTags
{
    internal static bool Enabled(string value, string key)
    {
        if (value.Length is 0 or > 4096) return false;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
            foreach (var field in ProtobufReader.Read(bytes, maximumBytes: 4096, maximumFields: 64).Where(field => field.Number == 1 && field.WireType == 2))
            {
                var fields = ProtobufReader.Read(field.Bytes, maximumBytes: 4096, maximumFields: 16);
                var names = fields.Where(row => row.Number == 1 && row.WireType == 2).ToArray();
                var values = fields.Where(row => row.Number == 2 && row.WireType == 2).ToArray();
                if (names.Length == 1 && values.Length == 1 && names[0].Bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(key)) && values[0].Bytes.Span.SequenceEqual("1"u8)) return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or NotSupportedException) { }
        return false;
    }
}
