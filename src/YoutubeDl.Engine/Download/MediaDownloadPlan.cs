using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

public sealed record MediaDownloadPlan(MediaCandidate Primary, MediaCandidate? Audio, string Extension)
{
    public bool NeedsMerge => Audio is not null;

    public static MediaDownloadPlan Create(PageMetadata page, NativeDownloadRequest request)
    {
        if (request.AudioOnly && request.VideoOnly) throw new ArgumentException("Choose either audio only or video only.");
        if (!string.IsNullOrWhiteSpace(request.FormatId) && !string.IsNullOrWhiteSpace(request.FormatSelector))
            throw new ArgumentException("Choose a literal format ID or a format selector, not both.");
        var candidates = page.Media.Where(candidate => FormatFacts.MatchesConstraints(page, candidate, request)).ToArray();
        MediaCandidate selected;
        MediaCandidate? audio = null;
        var explicitSelector = !string.IsNullOrWhiteSpace(request.FormatSelector);
        if (explicitSelector)
        {
            var selection = NativeFormatSelector.Select(page, request.FormatSelector, request);
            if (selection.Count > 2) throw new NotSupportedException("Native multi-stream merging is not implemented yet.");
            selected = selection.FirstOrDefault(FormatFacts.Video) ?? selection[0];
            audio = selection.FirstOrDefault(candidate => candidate != selected);
            if (audio is not null && (!FormatFacts.Video(selected) || FormatFacts.Video(audio) || !FormatFacts.Audio(audio)))
                throw new NotSupportedException("The native merger requires one video and one audio format.");
        }
        else if (!string.IsNullOrWhiteSpace(request.FormatId))
        {
            selected = page.Media.FirstOrDefault(candidate => candidate.Id == request.FormatId)
                ?? throw new NotSupportedException(page.ReportedFormats.FirstOrDefault(format => format.Id == request.FormatId)?.UnavailableReason
                    ?? "The requested format is not available in the native engine.");
            if (!FormatFacts.MatchesConstraints(page, selected, request)) throw new InvalidOperationException("The requested format does not satisfy the video constraints.");
        }
        else
        {
            // Classify the source before filtering. A height/codec constraint that
            // removes its video must not turn a video request into audio-only success.
            var audioContent = !request.VideoOnly && page.Media.Count > 0 && page.Media.All(candidate => candidate.IsAudioOnly) && !page.ReportedFormats.Any(format => format.HasVideo == true);
            selected = QualitySelector.Select(candidates, request.Quality, request.MaximumHeight, request.AudioOnly || audioContent, request.PreferFreeFormats);
            if (!request.AudioOnly && !audioContent && selected.IsAudioOnly)
                throw new InvalidOperationException("No video format satisfies the requested constraints.");
        }
        if (request.VideoOnly && selected.IsAudioOnly) throw new InvalidOperationException("The selected format does not contain video.");
        if (!request.AudioOnly && !selected.IsAudioOnly && request.MaximumHeight is > 0 && selected.Kind is MediaKind.Progressive or MediaKind.HttpSequence &&
            (selected.Height is null || selected.Height > request.MaximumHeight))
            throw new InvalidOperationException("The selected format does not satisfy the maximum video height.");
        if (request.AudioOnly && selected.HasAudio == false && audio is null) throw new InvalidOperationException("The selected format does not contain audio.");
        if (!explicitSelector && !request.AudioOnly && !request.VideoOnly && !selected.IsCompleteMedia && selected.HasVideo == true && selected.HasAudio == false)
        {
            var tracks = candidates.Where(candidate => candidate.IsAudioOnly).ToArray();
            if (tracks.Length == 0) throw new NotSupportedException("No downloadable audio track is available for the selected video. A player challenge or transport may still need support.");
            var defaultTracks = tracks.Where(track => track.IsDefaultAudio).ToArray();
            if (defaultTracks.Length > 0) tracks = defaultTracks;
            var compatible = tracks.Where(track => Compatible(selected.Extension, track.Extension)).ToArray();
            if (!request.PreferFreeFormats && compatible.Length > 0) tracks = compatible;
            audio = request.PreferFreeFormats ? tracks.OrderByDescending(FreeFormatPreference.Rank).ThenByDescending(track => track.Bandwidth ?? 0).First() :
                tracks.OrderByDescending(track => track.Bandwidth ?? 0).First();
        }
        var extension = selected.Kind == MediaKind.Hls ? "ts" : selected.Extension ?? "mp4";
        if (audio is not null && !Compatible(selected.Extension, audio.Extension)) extension = "mkv";
        if (audio is not null) extension = MergeOutputFormats.Select(request.MergeOutputFormat, extension, selected.VideoCodec, audio.AudioCodec);
        if (!string.IsNullOrWhiteSpace(request.Container) && (audio is null || string.IsNullOrWhiteSpace(request.MergeOutputFormat))) extension = request.Container;
        if (!System.Text.RegularExpressions.Regex.IsMatch(extension, @"\A[A-Za-z0-9]{1,10}\z"))
            throw new ArgumentException("The output container is invalid.");
        return new(selected, audio, extension);
    }

    private static bool Compatible(string? video, string? audio) => video switch
    {
        "mp4" => audio is "m4a" or "aac" or "mp4", "webm" => audio is "webm" or "opus", _ => false
    };
}
