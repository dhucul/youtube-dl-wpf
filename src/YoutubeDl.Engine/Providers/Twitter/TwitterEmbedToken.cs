using System.Globalization;

namespace YoutubeDl.Engine.Providers.Twitter;

/// <summary>Numeric implementation of the public embed's browser-radix checksum.</summary>
internal static class TwitterEmbedToken
{
    internal static string Create(string id)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var value = double.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture) / 1e15 * Math.PI;
        var integer = (ulong)Math.Floor(value); var fraction = value - integer;
        var uncertainty = Math.Max((Math.BitIncrement(value) - value) / 2, double.Epsilon);
        var digits = new List<int>();
        while (fraction >= uncertainty && digits.Count < 128)
        {
            fraction *= 36; uncertainty *= 36; var digit = (int)fraction; digits.Add(digit); fraction -= digit;
            if ((fraction > 0.5 || fraction == 0.5 && (digit & 1) != 0) && fraction + uncertainty > 1)
            {
                var position = digits.Count - 1;
                while (position >= 0 && digits[position] == 35) { digits.RemoveAt(position); position--; }
                if (position < 0) integer++; else digits[position]++;
                break;
            }
        }
        if (digits.Count >= 128) throw new InvalidOperationException("The public embed checksum exceeds its numeric bounds.");
        var whole = new List<char>();
        do { whole.Add(alphabet[(int)(integer % 36)]); integer /= 36; } while (integer > 0);
        whole.Reverse();
        var text = new string(whole.ToArray()) + (digits.Count > 0 ? "." + new string(digits.Select(digit => alphabet[digit]).ToArray()) : "");
        return text.Replace("0", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
    }
}
