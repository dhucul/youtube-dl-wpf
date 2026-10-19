using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace YoutubeDl.Engine.Discovery;

/// <summary>Reads the versioned address wrapper used by the publisher's public xplayer.</summary>
internal static class XplayerLinkDecoder
{
    internal static string Decode(string value, Uri page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (value.Length > 32778) throw new InvalidDataException("The encoded playback address exceeds its limit.");
        var whole = value.Length > 0 && value.All(character => char.IsAsciiHexDigit(character));
        var start = -1; var end = -1;
        if (!whole)
        {
            var scheme = value.IndexOf("://", StringComparison.Ordinal);
            var slash = value.IndexOf('/', scheme < 0 ? 0 : scheme + 3);
            if (slash < 0) return value;
            start = slash + 1; var nextSlash = value.IndexOf('/', start); var comma = value.IndexOf(',', start);
            end = nextSlash < 0 ? comma : comma < 0 ? nextSlash : Math.Min(nextSlash, comma);
            if (end <= start || !Hex(value[start..end])) return value;
        }
        if (!(page.Host.Equals("xhamster.com", StringComparison.OrdinalIgnoreCase) || page.Host.EndsWith(".xhamster.com", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("This player uses encoded playback addresses outside a supported publisher.");
        var encoded = whole ? value : value[start..end];
        if ((encoded.Length & 1) != 0 || encoded.Length < 12) throw new InvalidDataException("The encoded playback address is truncated.");
        var bytes = Convert.FromHexString(encoded); var version = bytes[0];
        if (version is < 1 or > 7)
        {
            if (!whole) return value;
            throw new NotSupportedException("The player returned an unsupported address encoding version.");
        }
        var state = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, 4));
        var decoded = new byte[bytes.Length - 5];
        for (var index = 0; index < decoded.Length; index++)
        {
            if ((index & 255) == 0) ct.ThrowIfCancellationRequested();
            decoded[index] = (byte)(bytes[index + 5] ^ Next(version, ref state));
            if (decoded[index] is < 32 or > 126) throw new InvalidDataException("The encoded playback address is not a supported ASCII URL.");
        }
        var text = Encoding.ASCII.GetString(decoded);
        var result = whole ? text : value[..start] + text + value[end..];
        if (result.Length > 16384 || !Uri.TryCreate(result, UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort ||
            url.UserInfo.Length != 0 || url.Fragment.Length != 0 || !url.Host.EndsWith(".xhcdn.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The decoded playback address is outside the publisher's HTTPS media service.");
        return result;
    }

    private static bool Hex(string value) => value.Length >= 12 && value.All(character => char.IsAsciiHexDigit(character));

    private static byte Next(byte version, ref uint state)
    {
        unchecked
        {
            uint output;
            switch (version)
            {
                case 1: state = state * 1664525U + 0x3c6ef35fU; output = state; break;
                case 2: state ^= state << 13; state ^= state >> 17; state ^= state << 5; output = state; break;
                case 3:
                    state += 0x9e3779b9U; output = state; output ^= output >> 16; output *= 0x85ebca77U;
                    output ^= output >> 13; output *= 0xc2b2ae3dU; output ^= output >> 16; break;
                case 4:
                    state += 0x6d2b79f5U; output = BitOperations.RotateLeft(state, 7) + 0x9e3779b9U;
                    output ^= output >> 11; output *= 0x27d4eb2dU; break;
                case 5: state ^= state << 7; state ^= state >> 9; state ^= state << 8; state += 0xa5a5a5a5U; output = state; break;
                case 6: state = state * 0x2c9277b5U + 0xac564b05U; output = (state ^ (state >> 18)) >> (int)((state >> 27) & 31); break;
                case 7:
                    state += 0x9e3779b9U; output = (state ^ (state << 5)) * 0x7feb352dU;
                    output ^= output >> 15; output *= 0x846ca68bU; break;
                default: throw new NotSupportedException("The player returned an unsupported address encoding version.");
            }
            return (byte)output;
        }
    }
}
