using System.Globalization;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

internal static class SettingsValidation
{
    public static void ThrowIfInvalid(AppSettings settings)
    {
        var g = settings.General;
        var n = settings.Network;
        var s = settings.Selection;

        if (g.MaxConcurrentDownloads is < 1 or > 8)
            throw new ArgumentException("Maximum concurrent downloads must be between 1 and 8.");
        if (n.SocketTimeout is < 0 or > 3600)
            throw new ArgumentException("Socket timeout must be between 0 and 3600 seconds.");
        if (n.Retries is < 0 or > 1000 || n.FragmentRetries is < 0 or > 1000)
            throw new ArgumentException("Retry counts must be between 0 and 1000.");
        if (n.ConcurrentFragments is < 1 or > 32)
            throw new ArgumentException("Concurrent fragments must be between 1 and 32.");
        if (n.SleepInterval < 0 || n.MaxSleepInterval < 0)
            throw new ArgumentException("Sleep intervals cannot be negative.");
        if (n.MaxSleepInterval > 0 && n.SleepInterval == 0)
            throw new ArgumentException("A maximum sleep interval requires a minimum sleep interval.");
        if (n.MaxSleepInterval > 0 && n.MaxSleepInterval < n.SleepInterval)
            throw new ArgumentException("Maximum sleep interval cannot be less than the minimum.");
        if (!IsValidRateLimit(n.RateLimit))
            throw new ArgumentException("The global speed limit is invalid.");

        if (s.PlaylistStart < 0 || s.PlaylistEnd < 0 ||
            (s.PlaylistStart > 0 && s.PlaylistEnd > 0 && s.PlaylistEnd < s.PlaylistStart))
            throw new ArgumentException("Playlist start/end values are invalid.");
        if (s.MaxDownloads < 0 || s.MinViews < 0 || s.MaxViews < 0)
            throw new ArgumentException("Selection counts cannot be negative.");
        if (s.MinViews > 0 && s.MaxViews > 0 && s.MaxViews < s.MinViews)
            throw new ArgumentException("Maximum views cannot be less than minimum views.");
        if (s.AgeLimit is < 0 or > 99)
            throw new ArgumentException("Age limit must be between 0 and 99.");

        ValidateExtraArguments(g.ExtraArgs, "global extra arguments");
    }

    public static void ValidateExtraArguments(string value, string fieldName)
    {
        try
        {
            _ = ProcessRunner.SplitArguments(value);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"The {fieldName} are malformed: {ex.Message}", ex);
        }
    }

    public static bool IsValidRateLimit(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return true;
        if (char.IsLetter(text[^1]))
        {
            if ("KMGTP".IndexOf(char.ToUpperInvariant(text[^1])) < 0) return false;
            text = text[..^1];
        }
        return double.TryParse(
            text,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var amount) && double.IsFinite(amount) && amount > 0;
    }
}
