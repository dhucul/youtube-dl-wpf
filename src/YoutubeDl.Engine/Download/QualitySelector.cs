using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

public enum QualityPreference
{
    Best,
    Worst,
    MaximumHeight
}

/// <summary>Picks one discovered stream from the quality the user already chose.</summary>
public static class QualitySelector
{
    public static MediaCandidate Select(
        IReadOnlyList<MediaCandidate> candidates,
        QualityPreference preference,
        int? maximumHeight,
        bool audioOnly,
        bool preferFreeFormats = false)
    {
        if (candidates.Count == 0)
            throw new InvalidOperationException("No downloadable media was found on the page.");

        var pool = audioOnly
            ? candidates.Where(static item => item.IsAudioOnly).ToList()
            : candidates.Where(static item => !item.IsAudioOnly).ToList();
        if (pool.Count == 0) pool = candidates.Where(item => audioOnly ? item.HasAudio != false : item.HasVideo != false).ToList();
        if (pool.Count == 0) throw new InvalidOperationException("No format contains the requested media type.");
        if (!audioOnly && maximumHeight is > 0)
        {
            pool = pool.Where(item => item.Height <= maximumHeight || item.Kind is MediaKind.Hls or MediaKind.Dash).ToList();
            if (pool.Count == 0) throw new InvalidOperationException("No stream satisfies the maximum video height.");
        }

        if (preferFreeFormats)
        {
            var ordered = pool.OrderByDescending(item => item.Height ?? 0).ThenByDescending(item => item.FramesPerSecond ?? 0)
                .ThenByDescending(FreeFormatPreference.Rank).ThenByDescending(item => item.Rank);
            return (preference == QualityPreference.Worst ? ordered.Reverse() : ordered).OrderBy(item => item.IsFallback).First();
        }
        if (preference == QualityPreference.Worst)
            return pool.OrderBy(static item => item.IsFallback).ThenBy(static item => item.Rank).First();

        return pool.OrderBy(static item => item.IsFallback).ThenByDescending(static item => item.Rank).First();
    }
}
