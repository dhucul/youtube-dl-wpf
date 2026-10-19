using System.Globalization;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private static async Task<int[]> PrepareLegacyTracksAsync(string ffmpeg, string input, CancellationToken ct)
    {
        if (Path.GetExtension(input).ToLowerInvariant() is not (".mp4" or ".mov" or ".m4v" or ".m4a" or ".3gp")) return [];
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "identify legacy MP4 streaming/system tracks", "mov").ConfigureAwait(false);
        var candidates = streams.Where(stream => stream.Kind == "data" && stream.CodecTag is "rtp " or "mp4s").ToArray();
        if (candidates.Length == 0) return [];
        var movie = await Mp4ChapterReferences.ReadMovieAsync(input, ct).ConfigureAwait(false); var handlers = Mp4ChapterReferences.ParseTrackHandlers(movie);
        var removed = new List<int>();
        foreach (var stream in candidates)
        {
            if (stream.TrackId is not { } id || !handlers.TryGetValue(id, out var handler)) throw new InvalidDataException("A legacy MP4 data stream is not bound to its movie track identity.");
            if (Mp4ChapterReferences.IsLegacyAncillary(stream.CodecTag!, handler)) removed.Add(stream.Index);
        }
        return removed.ToArray();
    }
    private static void MapLegacyTracks(List<string> arguments, IReadOnlyList<int> streams)
    {
        // The native processed movie carries playback, captions and supported
        // metadata. RTP delivery hints and identified MPEG-4 system descriptors
        // cannot be copied by the muxer; retained originals keep their bytes.
        foreach (var index in streams) arguments.AddRange(["-map", "-0:" + index.ToString(CultureInfo.InvariantCulture)]);
    }
}
