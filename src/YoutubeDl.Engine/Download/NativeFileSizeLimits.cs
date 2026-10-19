using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Download;

/// <summary>Bounds the original downloaded media before conversion or publication.</summary>
internal sealed class NativeFileSizeLimits
{
    internal long? Minimum { get; }
    internal long? Maximum { get; }
    internal bool Active => Minimum is not null || Maximum is not null;
    internal NativeFileSizeLimits(NativeDownloadRequest request) : this(Parse(request.MinimumFileSize), Parse(request.MaximumFileSize)) { }
    internal NativeFileSizeLimits(long? minimum,long? maximum)
    {
        if(minimum is <0||maximum is <0||minimum is not null&&maximum is not null&&minimum>maximum)throw new NativeSelectionException("The file-size bounds are invalid or inverted.");
        Minimum=minimum;Maximum=maximum;
    }
    internal void Check(long size)
    {
        if(size<0)throw new InvalidDataException("The media size cannot be negative.");
        if(Minimum is {} minimum&&size<minimum)throw new NativeFileSizeException("The original media is smaller than the requested minimum file size.");
        if(Maximum is {} maximum&&size>maximum)throw new NativeFileSizeException("The original media is larger than the requested maximum file size.");
    }
    internal void CheckMaximum(long size)
    { if(Maximum is {} maximum&&size>maximum)throw new NativeFileSizeException("The original media exceeded the requested maximum file size during transfer."); }
    internal static long? Parse(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(text.Length>128)throw new NativeSelectionException("The file-size expression exceeds its supported length.");
        var match=Regex.Match(text.Trim(),@"\A(?<value>[0-9]+(?:\.[0-9]+)?)(?<unit>[KMGTPEZYkmgtpezy]?)(?:i?[Bb])?\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(!match.Success)throw new NativeSelectionException("Use a nonnegative byte size with an optional K, M, G, T, P, E, Z or Y suffix.");
        try
        {
            var number=decimal.Parse(match.Groups["value"].Value,CultureInfo.InvariantCulture);var unit=match.Groups["unit"].Value;
            var power=unit.Length==0?0:"KMGTPEZY".IndexOf(char.ToUpperInvariant(unit[0]))+1;
            for(var index=0;index<power;index++)number=checked(number*1024);
            number=decimal.Round(number,0,MidpointRounding.ToEven);if(number>long.MaxValue)throw new OverflowException();return (long)number;
        }
        catch(Exception ex)when(ex is OverflowException or FormatException){throw new NativeSelectionException("The file-size limit exceeds the supported byte range.");}
    }
}

/// <summary>A size refusal is terminal for this transfer and must not be retried.</summary>
public sealed class NativeFileSizeException(string message):IOException(message);
