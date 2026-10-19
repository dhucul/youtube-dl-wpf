using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

internal sealed record AudioOutputPlan(int StreamIndex, string Extension, string Encoder, string ExpectedCodec,
    int Channels, int SampleRate, IReadOnlyList<string> QualityArguments)
{
    internal static string Normalize(string? format) => string.IsNullOrWhiteSpace(format) ? "best" : format.Trim().ToLowerInvariant();
    internal static string? ExtensionFor(string? format) => Normalize(format) switch
    {
        "best" or "copy" => null,
        "aac" => "aac", "m4a" or "alac" => "m4a", "vorbis" or "ogg" => "ogg",
        "mp3" => "mp3", "opus" => "opus", "flac" => "flac", "wav" => "wav",
        _ => throw new ArgumentException("Choose best, copy, MP3, M4A, AAC, Opus, Vorbis, FLAC, WAV or ALAC for audio output.")
    };
    internal static void Validate(string? format, string? quality)
    {
        _ = ExtensionFor(format);
        _ = ParseQuality(quality);
    }
    // Matroska/WebM carry codec delay and discard padding separately from their timestamps.
    // Retaining that container preserves decoded boundaries for copied AAC, Opus and Vorbis.
    internal static string AutomaticExtension(string codec,string container,string requested)=>container.Split(',').Any(value=>value is "matroska" or "webm")&&codec is "aac" or "opus" or "vorbis"?"mka":codec switch
    {
        "aac" or "alac"=>"m4a","mp3"=>"mp3","opus"=>"opus","vorbis"=>"ogg","flac"=>"flac",
        var value when value.StartsWith("pcm_",StringComparison.Ordinal)=>value is not ("pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" or "pcm_f64le" or "pcm_u8" or "pcm_alaw" or "pcm_mulaw")?"mka":"wav",
        _=>"mka"
    };
    internal static (decimal? Level, int? Bitrate) ParseQuality(string? quality)
    {
        quality = string.IsNullOrWhiteSpace(quality) ? "5" : quality.Trim();
        if (decimal.TryParse(quality, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 9)
            return (level, null);
        var match = Regex.Match(quality, @"\A(?<rate>[0-9]{1,7}(?:\.[0-9]{1,3})?)(?<unit>[kKmM])\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (match.Success && decimal.TryParse(match.Groups["rate"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate))
        {
            var bits = rate * (match.Groups["unit"].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? 1000000 : 1000);
            if (bits is >= 8000 and <= 10000000 && bits == decimal.Truncate(bits)) return (null, (int)bits);
        }
        throw new ArgumentException("Audio quality must be a level from 0 (highest) to 9 (lowest), or a bitrate such as 192K.");
    }
    internal static IReadOnlyList<string> EncoderQuality(string encoder, string? quality, int channels)
    {
        var parsed = ParseQuality(quality);
        if (encoder is "copy" or "flac" or "alac" || encoder.StartsWith("pcm_", StringComparison.Ordinal)) return [];
        if (parsed.Bitrate is { } bitrate)
            return encoder == "libopus" ? ["-b:a", bitrate.ToString(CultureInfo.InvariantCulture), "-vbr", "off"] : ["-b:a", bitrate.ToString(CultureInfo.InvariantCulture)];
        var level = parsed.Level!.Value;
        if (encoder == "libopus")
        {
            // Opus exposes a VBR target, not an ffmpeg qscale. Preserve the UI's highest-to-lowest direction.
            decimal[] stereo = [256, 224, 192, 160, 144, 128, 112, 96, 80, 64];
            var lower = (int)decimal.Floor(level); var upper = Math.Min(9, lower + 1);
            var rate = stereo[lower] + (stereo[upper] - stereo[lower]) * (level - lower);
            var bits = (int)decimal.Round(Math.Clamp(rate * Math.Max(1, channels) / 2 * 1000, 6000, 512000));
            return ["-b:a", bits.ToString(CultureInfo.InvariantCulture), "-vbr", "on"];
        }
        var q = encoder switch { "libvorbis" => 10 - level * 10 / 9, "aac" => 2 - level * 1.9m / 9, _ => level };
        return ["-q:a", q.ToString("0.######", CultureInfo.InvariantCulture)];
    }
}
