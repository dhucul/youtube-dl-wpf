using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private sealed record DownloadedTrack(string Path, IReadOnlyList<SubtitleTrack> Subtitles, double? ManifestDurationSeconds = null, bool Merged = false);
    private static async Task<DownloadedTrack> DownloadHlsWithAudioAsync(MediaHttp http, Uri playlistUrl, string destination,
        NativeDownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct, IReadOnlyList<SubtitleTrack> existingSubtitles, MediaCandidate? descriptor,ManifestPreparation manifests)
    {
        var downloader = new HlsDownloader(http) { RecoveryScope=request.AdaptiveRecoveryScope,RetainCheckpointAfterTransfer=request.AdaptiveRecoveryScope is not null, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NormalizePesCounterResets = descriptor?.NormalizeHlsPesCounterResets == true, NormalizeTransportContinuity = descriptor?.NormalizeHlsTransportContinuity == true };
        var playlist = await manifests.ReadHlsAsync(http,playlistUrl, ct).ConfigureAwait(false);
        CheckHlsFeatures(playlist);
        HlsAudioRendition? rendition = null;
        var master = playlist;
        HlsVariant? variant = null;
        var hadMaster = playlist.IsMaster;
        if (playlist.IsMaster)
        {
            variant = SelectVariant(playlist, descriptor?.IsAudioOnly == true ? request with { AudioOnly = true, MaximumHeight = null } : request);
            if (variant.AudioGroup is not null)
            {
                rendition = playlist.AudioRenditions.Where(audio => audio.Group == variant.AudioGroup).OrderByDescending(audio => audio.Default).FirstOrDefault()
                    ?? throw new InvalidDataException("The HLS master playlist refers to a missing audio group.");
            }
            playlist = await manifests.ReadHlsAsync(http,variant.Url, ct).ConfigureAwait(false);
            CheckHlsFeatures(playlist);
        }
        if (playlist.IsMaster) throw new NotSupportedException("Nested HLS master playlists are not supported yet.");
        var subtitles = HlsSubtitleResolver.Prepare(master, variant, existingSubtitles, request);
        if (!hadMaster && !request.AudioOnly && descriptor?.IsAudioOnly != true &&
            (request.MaximumHeight is > 0 && !(descriptor?.Height <= request.MaximumHeight) || request.MaximumFrameRate > 0 && !(descriptor?.FramesPerSecond <= request.MaximumFrameRate) || !FormatFacts.CodecMatches(descriptor?.VideoCodec, request.VideoCodec)))
            throw new InvalidOperationException("This HLS media playlist does not expose the requested codec/frame-rate facts. Use a master playlist or a format with known metadata.");
        var limiter = new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit));
        using var liveCaptions=subtitles.Live(http,request,limiter,destination);
        if (rendition?.Url is null || request.VideoOnly)
        {
            if (playlist.MediaSegments.Any(segment => segment.Initialization is not null)) destination = Path.ChangeExtension(destination, descriptor?.IsAudioOnly == true ? "m4a" : "mp4");
            else if (descriptor is { IsAudioOnly: true, AudioCodec: "mp3" }) destination = Path.ChangeExtension(destination, "mp3");
            else destination = Path.ChangeExtension(destination, "ts");
            if(!playlist.IsComplete||request.LiveDurationSeconds is not null||request.LiveFromStart||liveCaptions.HasRecovery)
            {
                var useLive=!playlist.IsComplete||playlist.MediaSegments.All(segment=>segment.ProgramDateTime is not null);
                var recording=await new HlsLiveRecorder(http){Observe=useLive?liveCaptions.ObserveAsync:null}.DownloadAsync(playlist,destination,request,progress,limiter,ct).ConfigureAwait(false);playlist=recording.Playlist;
                if(!useLive)return new(destination,await subtitles.ResolveAsync(http,destination,playlist,request,ct).ConfigureAwait(false),ManifestDuration(playlist));
                IReadOnlyList<decimal> clocks=!liveCaptions.RequiresMediaClocks?[]:recording.Timing.OriginalPeriodStarts.Count>0?recording.Timing.OriginalPeriodStarts:[await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath!,destination,ct).ConfigureAwait(false)];
                return new(destination,liveCaptions.Finish(playlist,clocks,ct:ct),ManifestDuration(playlist));
            }
            else await downloader.DownloadAsync(playlist, destination, progress, ct, limiter, request.FragmentRetries ?? request.Retries, request.FfmpegPath, concurrentFragments: request.ConcurrentFragments).ConfigureAwait(false);
            return new(destination, await subtitles.ResolveAsync(http, destination, playlist, request, ct).ConfigureAwait(false), ManifestDuration(playlist));
        }
        var audioPlaylist = await manifests.ReadHlsAsync(http,rendition.Url, ct).ConfigureAwait(false);
        CheckHlsFeatures(audioPlaylist);
        if (audioPlaylist.IsMaster) throw new NotSupportedException("The selected HLS audio rendition is not a media playlist.");
        if (request.AudioOnly)
        {
            destination = Path.ChangeExtension(destination, audioPlaylist.MediaSegments.Any(segment => segment.Initialization is not null) ? "m4a" : "ts");
            if(!audioPlaylist.IsComplete||request.LiveDurationSeconds is not null||request.LiveFromStart||liveCaptions.HasRecovery)
            {
                var useLive=!audioPlaylist.IsComplete||audioPlaylist.MediaSegments.All(segment=>segment.ProgramDateTime is not null);
                var recording=await new HlsLiveRecorder(http){Observe=useLive?liveCaptions.ObserveAsync:null}.DownloadAsync(audioPlaylist,destination,request,progress,limiter,ct).ConfigureAwait(false);audioPlaylist=recording.Playlist;
                if(!useLive)return new(destination,await subtitles.ResolveAsync(http,destination,audioPlaylist,request,ct).ConfigureAwait(false),ManifestDuration(audioPlaylist));
                IReadOnlyList<decimal> clocks=!liveCaptions.RequiresMediaClocks?[]:recording.Timing.OriginalPeriodStarts.Count>0?recording.Timing.OriginalPeriodStarts:[await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath!,destination,ct).ConfigureAwait(false)];
                return new(destination,liveCaptions.Finish(audioPlaylist,clocks,ct:ct),ManifestDuration(audioPlaylist));
            }
            else await downloader.DownloadAsync(audioPlaylist, destination, progress, ct, limiter, request.FragmentRetries ?? request.Retries, request.FfmpegPath, concurrentFragments: request.ConcurrentFragments).ConfigureAwait(false);
            return new(destination, await subtitles.ResolveAsync(http, destination, audioPlaylist, request, ct).ConfigureAwait(false), ManifestDuration(audioPlaylist));
        }
        if (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath))
            throw new InvalidOperationException("ffmpeg is required to combine separate HLS video and audio.");
        if(!playlist.IsComplete||!audioPlaylist.IsComplete||request.LiveDurationSeconds is not null||request.LiveFromStart||liveCaptions.HasRecovery)
        {
            var paired=await PairedLiveRecorder.DownloadAsync(http,playlist,audioPlaylist,destination,request,limiter,progress,ct,observeVideo:liveCaptions.ObserveAsync).ConfigureAwait(false);
            var zero=paired.Video.MediaSegments[0].ProgramDateTime!.Value<paired.Audio.MediaSegments[0].ProgramDateTime!.Value?paired.Video.MediaSegments[0].ProgramDateTime!.Value:paired.Audio.MediaSegments[0].ProgramDateTime!.Value;
            return new(paired.Path,liveCaptions.Finish(paired.Video,paired.VideoClocks,zero,paired.End,ct),paired.DurationSeconds,Merged:true);
        }
        if (!string.IsNullOrWhiteSpace(request.MergeOutputFormat)) FfmpegTool.RequireProbe(request.FfmpegPath, "choose the HLS merge container");
        destination = Path.ChangeExtension(destination, string.IsNullOrWhiteSpace(request.Container) ? "mp4" : request.Container);
        var video = destination + ".hls-video.source";
        var audioPath = destination + ".hls-audio.source";
        var merged = Path.Combine(Path.GetDirectoryName(destination)!, ".hls-merge-" + Guid.NewGuid().ToString("N") + Path.GetExtension(destination));
        IReadOnlyList<SubtitleTrack> resolvedSubtitles;
        var videoPeriods = playlist.MediaSegments.GroupBy(segment => segment.DiscontinuitySequence).ToArray();
        var audioPeriods = audioPlaylist.MediaSegments.GroupBy(segment => segment.DiscontinuitySequence).ToArray();
        IReadOnlyList<double>? sharedDurations = null;
        if (videoPeriods.Length > 1 || audioPeriods.Length > 1)
        {
            if (!videoPeriods.Select(period => period.Key).SequenceEqual(audioPeriods.Select(period => period.Key)) || playlist.MediaSegments.Any(segment => segment.DurationSeconds is not > 0))
                throw new NotSupportedException("The HLS audio and video timeline periods cannot be aligned.");
            sharedDurations = videoPeriods.Select(period => period.Sum(segment => segment.DurationSeconds!.Value)).ToArray();
        }
        try
        {
            var videoTiming = await downloader.DownloadAsync(playlist, video, progress, ct, limiter, request.FragmentRetries ?? request.Retries, request.FfmpegPath, concurrentFragments: request.ConcurrentFragments).ConfigureAwait(false);
            await new NativeDownloadDelay(request).WaitAsync(request.DownloadDelayLog, ct).ConfigureAwait(false);
            var audioTiming = await downloader.DownloadAsync(audioPlaylist, audioPath, null, ct, limiter, request.FragmentRetries ?? request.Retries, request.FfmpegPath, sharedDurations, request.ConcurrentFragments).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(request.MergeOutputFormat))
            {
                var extension = await FfmpegTool.SelectMergeExtensionAsync(request.FfmpegPath, video, audioPath, request.MergeOutputFormat, "mp4", ct).ConfigureAwait(false);
                destination = Path.ChangeExtension(destination, extension);
                merged = Path.ChangeExtension(merged, extension);
            }
            resolvedSubtitles = await subtitles.ResolveAsync(http, video, playlist, request, ct).ConfigureAwait(false);
            if (videoTiming.OriginalPeriodStarts.Count > 0 || audioTiming.OriginalPeriodStarts.Count > 0)
            {
                var videoStarts = videoTiming.OriginalPeriodStarts.Count > 0 ? videoTiming.OriginalPeriodStarts : [await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath, video, ct).ConfigureAwait(false)];
                var audioStarts = audioTiming.OriginalPeriodStarts.Count > 0 ? audioTiming.OriginalPeriodStarts : [await FfmpegTool.ReadStartTimeAsync(request.FfmpegPath, audioPath, ct).ConfigureAwait(false)];
                var offset = videoStarts[0] - audioStarts[0];
                if (videoStarts.Count != audioStarts.Count || videoStarts.Zip(audioStarts).Any(pair => Math.Abs(pair.First - pair.Second - offset) > 0.001m))
                    throw new NotSupportedException("The HLS audio/video clock relationship changes between periods; additional synchronization is required.");
                await FfmpegTool.MergeHlsAsync(request.FfmpegPath, video, audioPath, merged, offset, ct).ConfigureAwait(false);
            }
            else await FfmpegTool.MergeAsync(request.FfmpegPath, video, audioPath, merged, ct).ConfigureAwait(false);
            if (!File.Exists(merged) || new FileInfo(merged).Length == 0) throw new InvalidDataException("The HLS merge did not produce a media file.");
            ct.ThrowIfCancellationRequested();
            File.Move(merged, destination, overwrite: true);
            if (request.RetainedMedia is { } retained)
            {
                await retained.CaptureAsync(video, "video", playlist.MediaSegments.Any(segment => segment.Initialization is not null) ? "mp4" : "ts", descriptor, ct).ConfigureAwait(false);
                await retained.CaptureAsync(audioPath, "audio", audioPlaylist.MediaSegments.Any(segment => segment.Initialization is not null) ? "m4a" : "ts", null, ct).ConfigureAwait(false);
            }
            TryDelete(video); TryDelete(audioPath);
        }
        finally { TryDelete(merged); }
        return new(destination, resolvedSubtitles, ManifestDuration(playlist), Merged: true);
    }
    private static double? ManifestDuration(HlsPlaylist playlist)
    {
        if (!playlist.IsComplete || playlist.MediaSegments.Count == 0 || playlist.MediaSegments.Any(segment => segment.DurationSeconds is not > 0)) return null;
        var duration = playlist.MediaSegments.Sum(segment => segment.DurationSeconds!.Value);
        return double.IsFinite(duration) ? duration : null;
    }
    private static void CheckHlsFeatures(HlsPlaylist playlist)
    {
        if (playlist.UnsupportedFeatures.Count > 0) throw new NotSupportedException("HLS support is pending for: " + string.Join(", ", playlist.UnsupportedFeatures));
    }
}


