using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Download;

public sealed class DashDownloader(MediaHttp http)
{
    public bool NoContinue { get; init; }
    internal string? RecoveryScope { get; init; }
    public bool NoPart { get; init; }
    public string BufferSize { get; init; } = "";
    public bool NoResizeBuffer { get; init; }
    public string HttpChunkSize { get; init; } = "";
    internal Action<string>? TransferLog { get; init; }
    public bool NoMtime { get; init; }
    public string MergeOutputFormat { get; init; } = "";
    public bool PreferFreeFormats { get; init; }
    internal bool Merged { get; private set; }
    internal IReadOnlyList<SubtitleTrack> Subtitles { get; set; } = [];
    internal RetainedMediaCollector? RetainedMedia { get; init; }
    internal Func<CancellationToken, Task>? BeforeAdditionalTrack { get; init; }
    public async Task<DashManifest> ReadAsync(Uri manifestUrl, CancellationToken ct, bool liveProjection = false)
    {
        var document = await http.GetDocumentAsync(manifestUrl, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var manifest=DashManifestParser.Parse(document.Text,document.EffectiveUrl??document.Url,liveProjection);
        foreach(var r in manifest.Representations.Concat(manifest.CaptionTracks.Select(c=>c.Representation)).Concat(manifest.Periods.SelectMany(p=>p.Representations.Concat(p.Captions.Select(c=>c.Representation)))))
        {if(r.Initialization is { } initialization)PlaybackResourceExpiry.RequireFresh(initialization);foreach(var segment in r.Segments)PlaybackResourceExpiry.RequireFresh(segment.Url);if(r.Segments.Count==0)PlaybackResourceExpiry.RequireFresh(r.Url);}
        await http.ObservePlaybackManifestAsync(document,ct).ConfigureAwait(false);
        return Canonicalize(manifest);
    }
    private DashManifest Canonicalize(DashManifest manifest)
    {
        var copies=new Dictionary<DashRepresentation,DashRepresentation>(ReferenceEqualityComparer.Instance);
        DashRepresentation Representation(DashRepresentation r)
        {
            if(copies.TryGetValue(r,out var copy))return copy;
            return copies[r]=new(){Id=r.Id,Url=http.CanonicalizePlaybackUri(r.Url),MimeType=r.MimeType,Height=r.Height,Width=r.Width,AdaptationIdentity=r.AdaptationIdentity,Language=r.Language,Roles=r.Roles,Channels=r.Channels,Viewpoints=r.Viewpoints,Accessibility=r.Accessibility,SamplingRate=r.SamplingRate,DefaultAudio=r.DefaultAudio,Bandwidth=r.Bandwidth,Codecs=r.Codecs,FramesPerSecond=r.FramesPerSecond,Initialization=r.Initialization is null?null:http.CanonicalizePlaybackUri(r.Initialization),InitializationRange=r.InitializationRange,
                Segments=r.Segments.Select(s=>s with {Url=http.CanonicalizePlaybackUri(s.Url)}).ToArray(),SegmentBase=r.SegmentBase,UnavailableReason=r.UnavailableReason,InlineInitialization=r.InlineInitialization};
        }
        DashCaption Caption(DashCaption c)=>c with {Representation=Representation(c.Representation)};
        return new(){SourceUrl=manifest.SourceUrl is null?null:http.CanonicalizePlaybackUri(manifest.SourceUrl),IsDynamic=manifest.IsDynamic,LiveProjection=manifest.LiveProjection,MinimumUpdatePeriodSeconds=manifest.MinimumUpdatePeriodSeconds,AvailabilityStartTime=manifest.AvailabilityStartTime,PublishTime=manifest.PublishTime,PeriodIdentity=manifest.PeriodIdentity,PeriodStartSeconds=manifest.PeriodStartSeconds,
            Periods=manifest.Periods.Select(p=>p with {Representations=p.Representations.Select(Representation).ToArray(),Captions=p.Captions.Select(Caption).ToArray()}).ToArray(),Representations=manifest.Representations.Select(Representation).ToArray(),Subtitles=manifest.Subtitles.Select(http.CanonicalizePlaybackUri).ToArray(),HasSegmentedSubtitles=manifest.HasSegmentedSubtitles,CaptionTracks=manifest.CaptionTracks.Select(Caption).ToArray()};
    }
    public static IReadOnlyList<MediaCandidate> ToCandidates(DashManifest manifest) => manifest.Representations.Where(item => item.UnavailableReason is null).Select(item => new MediaCandidate
    {
        Id = item.Id, Url = item.Url, Height = item.Height, Width=item.Width, Bandwidth = item.Bandwidth, MimeType = item.MimeType,
        AudioLanguage=item.IsAudio?item.Language:null,IsDefaultAudio=item.IsAudio&&item.DefaultAudio,
        Extension = Extension(item), Kind = MediaKind.Progressive, Source = "dash", Label = item.Id,
        HasVideo = item.IsVideo, HasAudio = item.IsAudio, FramesPerSecond = item.FramesPerSecond,
        VideoCodec = item.IsVideo ? item.Codecs?.Split(',')[0] : null, AudioCodec = item.IsAudio ? item.Codecs?.Split(',')[0] : null
    }).ToArray();

    public async Task<string> DownloadAsync(DashManifest manifest, string destination, QualityPreference quality, int? maximumHeight,
        bool audioOnly, string? ffmpeg, ByteRateLimiter? limiter, IProgress<DownloadProgress>? progress, CancellationToken ct, int retries, bool videoOnly = false,
        int maximumFrameRate = 0, string videoCodec = "any", int concurrentFragments = 1, int? fragmentRetries = null, NativeDownloadRequest? liveRequest = null)
    {
        Merged = false;
        if (manifest.IsDynamic && liveRequest?.AllowDynamicDash == false) throw new NotSupportedException("Dynamic DASH playback is disabled by the selected option.");
        if(manifest.IsDynamic||manifest.LiveProjection&&liveRequest is {LiveDurationSeconds:not null} or {LiveFromStart:true})
        {
            var request=liveRequest??new NativeDownloadRequest {Url=manifest.SourceUrl!,OutputDirectory=Path.GetDirectoryName(destination)!,Quality=quality,MaximumHeight=maximumHeight,AudioOnly=audioOnly,VideoOnly=videoOnly,FfmpegPath=ffmpeg,Retries=retries,FragmentRetries=fragmentRetries,ConcurrentFragments=concurrentFragments,MaximumFrameRate=maximumFrameRate,VideoCodec=videoCodec};
            var result=await new DashLiveRecorder(http).DownloadAsync(manifest,destination,request,limiter??new(0),progress,ct,Subtitles).ConfigureAwait(false);Merged=result.Merged;Subtitles=result.Subtitles;return result.Path;
        }
        if(liveRequest is {LiveDurationSeconds:not null} or {LiveFromStart:true})throw new NotSupportedException("DASH recording controls require a dynamic presentation.");
        if(liveRequest?.LiveDashSelection is not null)throw new NotSupportedException("Live DASH selection preferences require a live presentation.");
        OrderedFragmentTransfer.Validate(concurrentFragments);
        var plan=SelectPlan(manifest,new NativeDownloadRequest {Url=manifest.SourceUrl??new("https://dash.example/"),OutputDirectory=Path.GetDirectoryName(destination)!,Quality=quality,
            MaximumHeight=maximumHeight,AudioOnly=audioOnly,VideoOnly=videoOnly,MaximumFrameRate=maximumFrameRate,VideoCodec=videoCodec,MergeOutputFormat=MergeOutputFormat,PreferFreeFormats=PreferFreeFormats});
        var selected = plan.Primary;
        var video = manifest.Representations.Single(item => item.Id == selected.Id);
        var audio = plan.Audio is { } audioFormat ? manifest.Representations.Single(item => item.Id == audioFormat.Id) : null;
        var extension = plan.Extension;
        destination = Path.ChangeExtension(destination, extension);
        if (audio is null)
        {
            await DownloadRepresentationAsync(video, destination, limiter, progress, retries, fragmentRetries ?? retries, concurrentFragments, ct).ConfigureAwait(false);
            return destination;
        }
        if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) throw new InvalidOperationException("ffmpeg is required to combine the selected DASH video and audio.");
        if (!string.IsNullOrWhiteSpace(MergeOutputFormat)) FfmpegTool.RequireProbe(ffmpeg, "choose the DASH merge container");
        var videoPath = destination + ".dash-video.mp4";
        var audioPath = destination + ".dash-audio.m4a";
        var merged = Path.Combine(Path.GetDirectoryName(destination)!, ".dash-merge-" + Guid.NewGuid().ToString("N") + "." + extension);
        try
        {
            await DownloadRepresentationAsync(video, videoPath, limiter, progress, retries, fragmentRetries ?? retries, concurrentFragments, ct).ConfigureAwait(false);
            if (BeforeAdditionalTrack is { } beforeAudio) await beforeAudio(ct).ConfigureAwait(false);
            await DownloadRepresentationAsync(audio, audioPath, limiter, null, retries, fragmentRetries ?? retries, concurrentFragments, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(MergeOutputFormat))
            {
                extension = await FfmpegTool.SelectMergeExtensionAsync(ffmpeg, videoPath, audioPath, MergeOutputFormat, extension, ct).ConfigureAwait(false);
                destination = Path.ChangeExtension(destination, extension); merged = Path.ChangeExtension(merged, extension);
            }
            await FfmpegTool.MergeAsync(ffmpeg, videoPath, audioPath, merged, ct).ConfigureAwait(false);
            if (!File.Exists(merged) || new FileInfo(merged).Length == 0) throw new InvalidDataException("The DASH merge did not produce a media file.");
            ct.ThrowIfCancellationRequested();
            File.Move(merged, destination, overwrite: true);
            Merged = true;
            if (RetainedMedia is { } retained)
            {
                await retained.CaptureAsync(videoPath, "video", Extension(video) ?? "mp4", selected, ct).ConfigureAwait(false);
                await retained.CaptureAsync(audioPath, "audio", Extension(audio) ?? "m4a", plan.Audio, ct).ConfigureAwait(false);
            }
            TryDelete(videoPath); TryDelete(audioPath);
        }
        finally { TryDelete(merged); }
        return destination;
    }
    internal static MediaDownloadPlan SelectPlan(DashManifest manifest,NativeDownloadRequest request)
    {
        var candidates=ToCandidates(manifest);if(candidates.Count==0)throw new InvalidDataException("The DASH manifest has no supported representation.");
        if(!request.VideoOnly&&manifest.Representations.Any(item=>item.IsAudio)&&!candidates.Any(item=>item.IsAudioOnly)||
            !request.AudioOnly&&manifest.Representations.Any(item=>item.IsVideo)&&!candidates.Any(item=>item.HasVideo==true))throw new NotSupportedException("The DASH manifest requires an unsupported audio or video representation.");
        return MediaDownloadPlan.Create(new(){Media=candidates},request with {Url=candidates[0].Url,FormatId=null,FormatSelector="",Container="",
            VideoOnly=request.VideoOnly||!request.AudioOnly&&!candidates.Any(candidate=>candidate.IsAudioOnly)});
    }

    private async Task DownloadRepresentationAsync(DashRepresentation representation, string destination, ByteRateLimiter? limiter,
        IProgress<DownloadProgress>? progress, int retries, int fragmentRetries, int concurrentFragments, CancellationToken ct)
    {
        if (representation.SegmentBase is not null)
        {
            await IndexedDashDownloader.DownloadAsync(http, representation, destination, limiter, progress, fragmentRetries, ct, concurrentFragments: concurrentFragments, noPart: NoPart, bufferSize: BufferSize, noResizeBuffer: NoResizeBuffer, httpChunkSize: HttpChunkSize,noContinue:NoContinue,recoveryScope:RecoveryScope).ConfigureAwait(false);
            return;
        }
        if (representation.Segments.Count == 0)
        {
            await new FileDownloader(http) { NoContinue = NoContinue, NoPart = NoPart, BufferSize = BufferSize, NoResizeBuffer = NoResizeBuffer, HttpChunkSize = HttpChunkSize, TransferLog = TransferLog, NoMtime = NoMtime }.DownloadAsync(representation.Url, destination, limiter, progress, ct, retries).ConfigureAwait(false);
            return;
        }
        var initialization = representation.Initialization is { } url ? new HlsInitialization(url, representation.InitializationRange, null) : null;
        if(representation.InlineInitialization is { } bytes)initialization=new(representation.Url,null,null){InlineData=bytes};
        var segments = representation.Segments.Select((segment, index) => new HlsSegment(segment.Url, (ulong)index, segment.Range, null, initialization,segment.DurationSeconds)).ToArray();
        await new HlsDownloader(http) { RecoveryScope=System.Text.Json.JsonSerializer.Serialize(new{Scope=RecoveryScope,representation.Id,representation.Codecs,representation.MimeType,representation.Height,representation.FramesPerSecond}),RetainCheckpointAfterTransfer=RecoveryScope is not null, NoContinue = NoContinue, NoPart = NoPart, BufferSize = BufferSize, NoResizeBuffer = NoResizeBuffer, HttpChunkSize = HttpChunkSize, TransferLog = TransferLog }.DownloadAsync(new HlsPlaylist
        {
            IsComplete = true, Segments = representation.Segments.Select(segment => segment.Url).ToArray(), MediaSegments = segments
        }, destination, progress, ct, limiter, fragmentRetries, concurrentFragments: concurrentFragments).ConfigureAwait(false);
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static string? Extension(DashRepresentation representation) => representation.MimeType.Split(';')[0] switch
    {
        "video/mp4" => "mp4", "audio/mp4" => "m4a", "video/webm" or "audio/webm" => "webm",
        _ => PageMediaExtractor.ExtensionFrom(representation.Url, representation.MimeType)
    };
}



