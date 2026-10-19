using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bilibili;

public sealed partial class BilibiliProvider
{
    private static IReadOnlyList<MediaCandidate> ReadMedia(JsonElement data, long cid, List<string> warnings)
    {
        var result = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var dash = Object(data, "dash");
        if (dash.ValueKind == JsonValueKind.Object)
        {
            CheckRestrictions(dash);
            var videos = Array(dash, "video"); var audios = Array(dash, "audio");
            if (videos.Length == 0 || videos.Length + audios.Length > 256) throw new InvalidDataException("Bilibili returned an invalid adaptive format inventory.");
            foreach (var (file, video) in videos.Select(item => (item, true)).Concat(audios.Select(item => (item, false))))
            {
                CheckRestrictions(file);
                var mime = Alias(file, "mimeType", "mime_type"); var codec = Text(file, "codecs");
                if (mime != (video ? "video/mp4" : "audio/mp4") || codec is null ||
                    !(video ? Regex.IsMatch(codec, @"\A(?:avc[13]|hev1|hvc1|av01)\.[A-Za-z0-9.]+\z") : Regex.IsMatch(codec, @"\Amp4a\.[0-9.]+\z")))
                { warnings.Add("An offered Bilibili codec or media type requires additional native support."); continue; }
                var id = Long(file, "id"); var codecid = Long(file, "codecid");
                if (id is not > 0 || video && codecid is null) throw new InvalidDataException("Bilibili omitted a rendition identity.");
                var formatId = (video ? "video-" : "audio-") + id.Value.ToString(CultureInfo.InvariantCulture) + (video ? "-" + codecid!.Value.ToString(CultureInfo.InvariantCulture) : "");
                if (!ids.Add(formatId)) throw new InvalidDataException("Bilibili repeated a rendition identity.");
                var url = Resource(Alias(file, "baseUrl", "base_url") ?? throw new InvalidDataException("Bilibili omitted a rendition URL."));
                var identity = Regex.Match(url.AbsolutePath, @"\A/upgcxcode/[0-9]+/[0-9]+/(?<cid>[0-9]+)/\k<cid>_[A-Za-z0-9_-]+\.m4s\z", RegexOptions.CultureInvariant);
                if (!identity.Success || identity.Groups["cid"].Value != cid.ToString(CultureInfo.InvariantCulture))
                    throw new NotSupportedException("Bilibili supplied an adaptive resource layout that could not be bound to this part.");
                var fps = Alias(file, "frameRate", "frame_rate");
                result.Add(new() { Id = formatId, TimelineId = "bilibili:" + cid.ToString(CultureInfo.InvariantCulture), VerificationDemuxer = "mov", VerificationDurationSeconds = Number(data, "timelength") / 1000, Url = url, Kind = MediaKind.Progressive, Extension = video ? "mp4" : "m4a", MimeType = mime,
                    HasVideo = video, HasAudio = !video, VideoCodec = video ? codec : null, AudioCodec = video ? null : codec,
                    Height = video && Integer(file, "height") is > 0 and var height ? height : null,
                    Width = video && Integer(file, "width") is > 0 and var width ? width : null,
                    Bandwidth = Number(file, "bandwidth") is > 0 and var bandwidth ? bandwidth : null,
                    FramesPerSecond = video ? FrameRate(fps) : null, Source = "bilibili" });
            }
            if (!result.Any(item => item.HasVideo == true)) throw new NotSupportedException("Bilibili did not expose a supported video rendition for this upload.");
            if (Active(Object(dash, "dolby"), "audio") || Active(dash, "flac")) warnings.Add("Bilibili Dolby and FLAC renditions require additional native support.");
        }
        else
        {
            var files = Array(data, "durl");
            // durl is an ordered chunk list, never a list of alternative qualities.
            if (files.Length is < 1 or > 4096) throw new InvalidDataException("Bilibili did not expose a bounded progressive file inventory.");
            var parts = new List<MediaFilePart>(); var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                CheckRestrictions(file);
                if (Integer(file, "order") != parts.Count + 1 || Number(file, "length") is not > 0)
                    throw new InvalidDataException("Bilibili returned missing, repeated or invalid progressive chunk positions.");
                var url = Resource(Text(file, "url") ?? throw new InvalidDataException("Bilibili omitted a progressive URL."));
                var extension = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
                if (extension is not ("mp4" or "flv")) throw new NotSupportedException("This Bilibili progressive container requires additional native support.");
                if (parts.Count > 0 && extension != parts[0].Extension || !urls.Add(url.AbsoluteUri))
                    throw new InvalidDataException("Bilibili repeated a chunk or changed progressive containers.");
                parts.Add(new(url, extension, Number(file, "length")!.Value / 1000, Long(file, "size") is > 0 and var size ? size : null));
            }
            if (!double.IsFinite(parts.Sum(part => part.DurationSeconds)) || Math.Abs(parts.Sum(part => part.DurationSeconds) * 1000 - Number(data, "timelength")!.Value) > 1)
                throw new InvalidDataException("Bilibili returned an incomplete progressive duration inventory.");
            var first = parts[0];
            result.Add(new() { Id = "progressive", Url = first.Url, Kind = parts.Count == 1 ? MediaKind.Progressive : MediaKind.HttpSequence,
                Parts = parts.Count == 1 ? [] : parts, Extension = first.Extension, VerificationDemuxer = first.Extension == "mp4" ? "mov" : "flv",
                VerificationDurationSeconds = Number(data, "timelength") / 1000, HasVideo = true,
                FileSize = parts.Count == 1 ? first.FileSize : null, IsCompleteMedia = true, Source = "bilibili" });
        }
        if (result.Count == 0) throw new NotSupportedException("Bilibili did not expose any supported clear media resources.");
        return result;
    }
    private static string? Alias(JsonElement file, string camel, string snake)
    {
        var first = Text(file, camel); var second = Text(file, snake);
        if (first is not null && second is not null && first != second) throw new InvalidDataException("Bilibili returned conflicting rendition fields.");
        return first ?? second;
    }
    private static double? FrameRate(string? text)
    {
        if (text is null) return null;
        var parts = text.Split('/');
        if (parts.Length is < 1 or > 2 || !double.TryParse(parts[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = 1d;
        if (parts.Length == 2 && !double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out denominator)) return null;
        var rate = numerator / denominator;
        return rate > 0 && double.IsFinite(rate) ? rate : null;
    }
}
