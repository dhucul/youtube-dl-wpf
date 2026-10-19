using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private sealed record MovieChapterPlan(int[] Streams, IReadOnlyList<ChapterMarker> Chapters);
    private static async Task<MovieChapterPlan?> PrepareMovieChaptersAsync(string ffmpeg, string input, CancellationToken ct)
    {
        if (Path.GetExtension(input).ToLowerInvariant() is not (".mp4" or ".mov" or ".m4v" or ".m4a" or ".3gp")) return null;
        IReadOnlyList<ChapterMarker> chapters = [];
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "preserve QuickTime chapter tracks", "mov", inspectDocument: root => chapters = ReadMovieChapters(root)).ConfigureAwait(false);
        var candidates = streams.Where(stream => stream.Kind == "data" && stream.CodecTag == "text").ToArray();
        if (candidates.Length == 0) return null;
        var references = await Mp4ChapterReferences.ReadAsync(input, ct).ConfigureAwait(false);
        var identified = candidates.Where(stream => stream.TrackId is { } id && references.Contains(id)).ToArray();
        if (identified.Length != candidates.Length || chapters.Count == 0)
            throw new NotSupportedException("This MP4 text-data layout is not fully identified as a chapter track and cannot be safely recreated.");
        return new(identified.Select(stream => stream.Index).ToArray(), chapters);
    }
    private static void MapMovieChapters(List<string> arguments, MovieChapterPlan? plan)
    {
        if (plan is null) return;
        // Recreate only tracks identified by the movie's chap references. Copying
        // them as ordinary data duplicates chapters and fails in MOV containers.
        foreach (var index in plan.Streams) arguments.AddRange(["-map", "-0:" + index.ToString(CultureInfo.InvariantCulture)]);
    }
    private static async Task VerifyMovieChaptersAsync(string ffmpeg, string output, IReadOnlyList<ChapterMarker>? expected, CancellationToken ct)
    {
        if (expected is not { Count: > 0 }) return;
        IReadOnlyList<ChapterMarker> actual = [];
        _ = await ProbeStreamsAsync(ffmpeg, output, ct, "verify preserved chapters", inspectDocument: root => actual = ReadMovieChapters(root)).ConfigureAwait(false);
        if (actual.Count != expected.Count) throw new InvalidDataException("The processed file did not preserve its chapter count.");
        for (var index = 0; index < actual.Count; index++)
        {
            if (actual[index].Title != expected[index].Title) throw new InvalidDataException("The processed file changed the title of chapter " + (index + 1) + ".");
            if (Math.Abs(actual[index].StartSeconds - expected[index].StartSeconds) > 0.0015 || Math.Abs(actual[index].EndSeconds - expected[index].EndSeconds) > 0.0015)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The processed file changed chapter {index + 1} timing: expected {expected[index].StartSeconds:F6}-{expected[index].EndSeconds:F6}s, received {actual[index].StartSeconds:F6}-{actual[index].EndSeconds:F6}s."));
        }
        if (Path.GetExtension(output).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".m4a" or ".3gp")
            _ = await Mp4ChapterReferences.ReadAsync(output, ct).ConfigureAwait(false);
    }
    private static IReadOnlyList<ChapterMarker> ReadMovieChapters(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out var list)) return [];
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 1000) throw new InvalidDataException("The movie has an excessive chapter inventory.");
        var result = new List<ChapterMarker>();
        foreach (var chapter in list.EnumerateArray())
        {
            var start = Time(chapter, "start_time"); var end = Time(chapter, "end_time");
            var title = chapter.TryGetProperty("tags", out var tags) && tags.TryGetProperty("title", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : "";
            if (end <= start || result.Count > 0 && start < result[^1].EndSeconds - 0.0015 || title.Length > 4096 || title.Contains('\0'))
                throw new NotSupportedException("This movie chapter layout cannot be preserved as ordered finite chapter ranges.");
            result.Add(new() { Title = title, StartSeconds = start, EndSeconds = end });
        }
        return result;
        static double Time(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ?
            number : throw new InvalidDataException("The movie contains invalid chapter timing.");
    }
}
