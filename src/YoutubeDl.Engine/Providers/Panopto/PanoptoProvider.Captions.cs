using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Panopto;

public sealed partial class PanoptoProvider
{
    private static IReadOnlyList<ChapterMarker> Chapters(JsonElement delivery, double duration)
    {
        var timestamps = Array(delivery, "Timestamps");
        if (timestamps.Length > 1000) throw new InvalidDataException("Panopto returned too many chapter events.");
        var points = timestamps.Where(item => True(item, "ShowInTableOfContents") && Text(item, "EventTargetType") is "PowerPoint" or "Primary")
            .Select(item => (Time: Number(item, "Time"), Title: Text(item, "Caption"))).Where(item => item.Time is >= 0 && item.Time < duration && !string.IsNullOrWhiteSpace(item.Title))
            .OrderBy(item => item.Time).GroupBy(item => Math.Round(item.Time!.Value * 1000)).Select(group => group.First()).ToArray();
        return points.Select((point, index) => new ChapterMarker { Title = WebUtility.HtmlDecode(point.Title!), StartSeconds = point.Time!.Value,
            EndSeconds = index + 1 < points.Length ? points[index + 1].Time!.Value : duration }).ToArray();
    }
    private static readonly string[] Languages = ["en-US", "en-GB", "es-MX", "es-ES", "de", "fr", "nl", "th", "zh-Hans", "zh-Hant", "ko", "ja", "ru", "pt", "pl", "en-AU", "da", "fi", "hu", "no", "sv", "it", "cy", "ca", "gl", "eu", "ar", "hi", "fr-CA", "he", "vi", "el"];
    private static int LanguageId(string language) => System.Array.IndexOf(Languages, language);
    private static IReadOnlyList<SubtitleTrack> CaptionTracks(JsonElement delivery, PanoptoAddress address, List<string> warnings)
    {
        var result = new List<SubtitleTrack>();
        var captions = Array(delivery, "AvailableCaptions");
        if (captions.Length > 64) throw new InvalidDataException("Panopto returned too many caption languages.");
        foreach (var caption in captions)
        {
            if (Integer(caption, "Language") is not { } id || id >= Languages.Length) { warnings.Add("An unknown Panopto caption language was excluded."); continue; }
            if (result.Any(track => track.Language == Languages[id])) continue;
            // The disclaimer flag is not sufficient to establish how a caption was authored.
            result.Add(new() { Url = address.Api, Language = Languages[id], Extension = "vtt" });
        }
        return result;
    }
    internal static string RenderCaptions(JsonElement root, double duration, string sessionId)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 10000) throw new InvalidDataException("Panopto returned an unsupported caption list.");
        var output = new StringBuilder("WEBVTT\n\n"); var index = 0;
        foreach (var cue in root.EnumerateArray())
        {
            var declared = Text(cue, "SessionID");
            if (declared is not null && declared != Guid.Empty.ToString("D") && !declared.Equals(sessionId, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Panopto captions belong to another session.");
            if (Number(cue, "Time") is not { } start || Number(cue, "CaptionDuration") is not > 0 || Text(cue, "Caption") is not { } text || string.IsNullOrWhiteSpace(text) || text.Length > 16384 || text.Any(ch => ch == '\0'))
                throw new InvalidDataException("Panopto returned malformed caption timing or text.");
            var end = start + Number(cue, "CaptionDuration")!.Value;
            if (start >= duration || end > duration + 0.25) throw new InvalidDataException("Panopto captions exceed the recording timeline.");
            end = Math.Min(end, duration);
            var from = checked((long)Math.Round(start * 1000, MidpointRounding.AwayFromZero)); var to = checked((long)Math.Round(end * 1000, MidpointRounding.AwayFromZero));
            if (to <= from) throw new InvalidDataException("Panopto caption is shorter than millisecond precision.");
            text = string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
            output.Append(++index).Append('\n').Append(Time(from)).Append(" --> ").Append(Time(to)).Append('\n')
                .Append(WebUtility.HtmlEncode(text).Replace("-->", "--&gt;", StringComparison.Ordinal)).Append("\n\n");
        }
        return output.ToString();
        static string Time(long ms) => string.Create(CultureInfo.InvariantCulture, $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}");
    }
}
