using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

internal sealed class NativeDateRange
{
    private readonly DateOnly _first;
    private readonly DateOnly _last;
    internal NativeDateRange(NativeDownloadRequest request, DateOnly today)
    {
        if (!string.IsNullOrEmpty(request.Date)) _first = _last = Parse(request.Date, today);
        else
        {
            _first = string.IsNullOrEmpty(request.DateAfter) ? DateOnly.MinValue : Parse(request.DateAfter, today);
            _last = string.IsNullOrEmpty(request.DateBefore) ? DateOnly.MaxValue : Parse(request.DateBefore, today);
        }
        if (_first > _last) throw new NativeSelectionException("The earliest upload date must not be after the latest date.");
    }
    internal bool Contains(DateOnly date) => date >= _first && date <= _last;
    internal static DateOnly Parse(string text, DateOnly today)
    {
        if (text.Length > 64) throw new NativeSelectionException("The upload date expression exceeds its supported length.");
        if (DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact;
        var match = Regex.Match(text, @"\A(?<base>now|today|yesterday)(?:-(?<count>[0-9]+)(?<unit>day|week|month|year)s?)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new NativeSelectionException("Use YYYYMMDD or a relative upload date such as today-2weeks.");
        try
        {
            var basis = match.Groups["base"].Value == "yesterday" ? today.AddDays(-1) : today;
            if (!match.Groups["count"].Success) return basis;
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)) throw new OverflowException();
            return match.Groups["unit"].Value switch
            {
                "day" => basis.AddDays(-amount), "week" => basis.AddDays(checked(-amount * 7)),
                "month" => basis.AddMonths(-amount), "year" => basis.AddYears(-amount), _ => throw new InvalidOperationException()
            };
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        { throw new NativeSelectionException("The relative upload date exceeds the supported calendar range."); }
    }
}
