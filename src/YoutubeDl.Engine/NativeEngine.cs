using System.Net;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using YoutubeDl.Engine.Providers;

namespace YoutubeDl.Engine;

/// <summary>
/// In-process C# engine. Site-specific extraction is supplied by registered providers.
/// </summary>
public sealed partial class NativeEngine : IMediaEngine
{
    public const string Version = "0.228.1";

    private static readonly MediaProviderRegistry DefaultProviders = MediaProviderRegistry.CreateDefault();
    private readonly MediaProviderRegistry _providers;
    private readonly Func<NativeDownloadRequest, MediaHttp>? _httpFactory;
    private readonly Func<Uri,CancellationToken,Task>? _accountBrowserOpen;
    public IReadOnlyList<MediaProviderInfo> Providers => _providers.Providers;

    public NativeEngine(IEnumerable<IMediaProvider>? providers = null,
        Func<NativeDownloadRequest, MediaHttp>? httpFactory = null,
        Providers.YouTube.IYouTubePlayerRuntime? playerRuntime = null,
        Providers.YouTube.IYouTubePlaybackSource? youTubePlaybackSource = null, Providers.Newgrounds.INewgroundsPageSource? newgroundsPageSource = null,Func<Uri,CancellationToken,Task>? accountBrowserOpen=null)
    {
        _providers = providers is null ? playerRuntime is null && youTubePlaybackSource is null && newgroundsPageSource is null ? DefaultProviders : MediaProviderRegistry.CreateDefault(playerRuntime, youTubePlaybackSource, newgroundsPageSource) : new MediaProviderRegistry(providers);
        _httpFactory = httpFactory;
        _accountBrowserOpen=accountBrowserOpen;
    }

    /// <summary>Describes a provider's already resolved metadata without fetching its media or reopening its parent page.</summary>
    public static NativeMediaInfo Describe(Uri url, PageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!url.IsAbsoluteUri) throw new ArgumentException("The media address must be absolute.", nameof(url));
        return ToInfo(url, metadata);
    }

    public async Task<NativeMediaInfo> InspectAsync(NativeDownloadRequest request, CancellationToken ct)
    {
        request = NativeHttpHeaders.Normalize(request);
        ValidateRequest(request);
        using var extractionDelay = NativeExtractionDelay.Enter(request.RequestSleepSeconds);
        request = await NativeAuthentication.PrepareAsync(request, _providers.Resolve(request.Url), ct).ConfigureAwait(false);
        using var http = await CreateSessionAsync(request, ct).ConfigureAwait(false);
        request=await SignInAsync(request,http,ct).ConfigureAwait(false);
        var page = await LoadAsync(request, http, ct).ConfigureAwait(false);
        return ToInfo(request.Url, page with { Warnings = http.SessionWarnings.Concat(page.Warnings).Distinct(StringComparer.Ordinal).ToArray() }, CapturePlaylist(request, page));
    }

    public async Task<NativeDownloadResult> DownloadAsync(
        NativeDownloadRequest request,
        IProgress<DownloadProgress>? progress,
        Action<string>? log,
        CancellationToken ct, Action<string>? onFileCompleted = null)
    {
        request = NativeHttpHeaders.Normalize(request);
        using var processingOptions = NativeProcessingOptions.Enter(request);
        if (request.Quiet) log = null;
        if (request.NoProgress) progress = null;
        request = request with { SelectionState = new DownloadSelectionState(request), PlaylistTraversal = new PlaylistTraversalState(), PlaylistAncestors = [], PlaylistParents = [] };
        ValidateRequest(request);
        using var extractionDelay = NativeExtractionDelay.Enter(request.RequestSleepSeconds);
        request = await NativeAuthentication.PrepareAsync(request, _providers.Resolve(request.Url), ct).ConfigureAwait(false);
        ValidatePlaylistSnapshot(request);
        if (request.RemoveCache) { var removed = await NativePlayerScriptCache.RemoveAsync(request, ct).ConfigureAwait(false); log?.Invoke("[native] removed " + removed + " native public cache files"); }
        await DownloadWorkspace.ReconcileChangedAsync(request,ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.DownloadArchiveFile) && _providers.Resolve(request.Url).GetArchiveIdentity(request) is { } known &&
            await DownloadArchive.ContainsAsync(request.DownloadArchiveFile, known, ct).ConfigureAwait(false))
        {
            await DownloadWorkspace.FinalizeRecordedAsync(request).ConfigureAwait(false);
            log?.Invoke("[native] already recorded in the download archive");
            return new() { Succeeded = true, Skipped = true };
        }
        using var http = await CreateSessionAsync(request, ct).ConfigureAwait(false);
        request=await SignInAsync(request,http,ct).ConfigureAwait(false);
        foreach (var warning in request.NoWarnings ? [] : http.SessionWarnings) log?.Invoke("[native] " + warning);
        var page = request.PlaylistSnapshot is not null ? await RestorePlaylistAsync(request, http, ct).ConfigureAwait(false) :
            await LoadAsync(request with { ExpandPlaylist = request.ExpandPlaylist || request.DownloadPlaylist }, http, ct).ConfigureAwait(false);
        if (page.IsPlaylist)
        {
            if (!request.DownloadPlaylist) throw new InvalidOperationException("A playlist URL does not identify a single linked video.");
            return await DownloadPlaylistAsync(page, request, http, progress, log, ct, onFileCompleted).ConfigureAwait(false);
        }
        var result = await DownloadResolvedAsync(request, page, http, progress, log, ct).ConfigureAwait(false);
        await CompleteCallbacksAsync(result,onFileCompleted).ConfigureAwait(false);
        return result;
    }

    private static async Task<NativeDownloadResult> DownloadResolvedAsync(NativeDownloadRequest request, PageMetadata page, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        page = NativeMetadataParser.Apply(request, page, log, ct);
        foreach (var warning in request.NoWarnings ? [] : page.Warnings) log?.Invoke("[native] " + warning);
        if (request.SelectionState?.Rejection(page, request, ct) is { } rejection)
        {
            log?.Invoke("[native] skipped: " + rejection);
            return new() { Succeeded = true, Skipped = true, Filtered = true, Simulated = request.Simulate };
        }
        await DownloadWorkspace.ReconcileChangedAsync(request,ct).ConfigureAwait(false);
        if ((request.SkipDownload || request.Simulate || request.IgnoreNoFormatsError && page.Media.Count == 0) && !request.ForceWriteArchive)
        {
            IReadOnlyList<string> protectedPaths = [];
            if (!string.IsNullOrWhiteSpace(request.DownloadArchiveFile))
            {
                if (await DownloadArchive.ContainsAsync(request.DownloadArchiveFile, DownloadArchiveIdentity.FromMetadata(page, request.Url), ct).ConfigureAwait(false))
                    return new() { Succeeded = true, Skipped = true, Simulated = request.Simulate };
                protectedPaths = DownloadArchive.ProtectedOutputPaths(request.DownloadArchiveFile);
            }
            var metadataResult = await DownloadResolvedCoreAsync(request, page, http, progress, log, ct, protectedPaths).ConfigureAwait(false);
            request.SelectionState?.Record(metadataResult, request);
            if(metadataResult.Succeeded&&metadataResult.PublicationCompletion is { } metadataCompletion)await DownloadWorkspace.FinishAsync(metadataCompletion,null,archiveOnly:true).ConfigureAwait(false);
            return metadataResult;
        }
        using var archive = string.IsNullOrWhiteSpace(request.DownloadArchiveFile) ? null :
            await DownloadArchive.OpenAsync(request.DownloadArchiveFile, DownloadArchiveIdentity.FromMetadata(page, request.Url), ct, log).ConfigureAwait(false);
        if (archive?.AlreadyRecorded == true)
        {
            await DownloadWorkspace.FinalizeRecordedAsync(request).ConfigureAwait(false);
            log?.Invoke("[native] already recorded in the download archive");
            return new() { Succeeded = true, Skipped = true };
        }
        var result = await DownloadResolvedCoreAsync(request, page, http, progress, log, ct, archive?.ProtectedPaths ?? []).ConfigureAwait(false);
        request.SelectionState?.Record(result, request);
        if (result.Succeeded && archive is not null)
        {
            try { await archive.RecordAsync(result.PublicationCompletion).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new() { Succeeded = false, Skipped = result.Skipped, OutputPath = result.OutputPath, OutputPaths = result.OutputPaths,
                    PublicationCompletion=result.PublicationCompletion,
                    NewOutputPaths = result.NewOutputPaths,
                    SidecarPaths = result.SidecarPaths, NewSidecarPaths = result.NewSidecarPaths, Simulated = result.Simulated,
                    Error = "The job finished, but the download archive could not be updated: " + ex.Message };
            }
        }
        if(result.Succeeded&&result.PublicationCompletion is { } completion)await DownloadWorkspace.FinishAsync(completion,null,archiveOnly:true).ConfigureAwait(false);
        return result;
    }

    private static async Task<NativeDownloadResult> DownloadResolvedCoreAsync(NativeDownloadRequest request, PageMetadata page, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, IReadOnlyList<string> protectedPaths)
    {
        page = ThumbnailCatalog.Prepare(page);
        using var anonymous = page.PublicServicePlayback ? http.CreatePublicServiceSession(preserveForwardedFor: true) : page.PlaybackCookies is not null ? http.CreateCookieSession(page.PlaybackCookies) : page.AnonymousPlayback ? http.CreateAnonymousSession() : null;
        http = anonymous ?? http;
        var headers = page.PlaybackHeaders.Where(header =>
            !(header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.Referer)) &&
            !(header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.UserAgent))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        using var playback = headers.Count == 0 ? null : http.WithHeaders(headers);
        http = playback ?? http;
        using var scopedQueries = page.PlaybackQueries.Count == 0 ? null : http.WithQueries(page.PlaybackQueries);
        http = scopedQueries ?? http;
        using var authorized = page.PlaybackAuthorization is null ? null : http.WithAuthorization(page.PlaybackAuthorization);
        http = authorized ?? http;
        if (request.SkipDownload || request.Simulate || request.IgnoreNoFormatsError && page.Media.Count == 0)
            return await DownloadWithoutMediaAsync(request, page, http, log, protectedPaths, ct).ConfigureAwait(false);
        if (page.DrmRefused)
        {
            return new NativeDownloadResult { Succeeded = false, Error = page.RefusalReason };
        }

        if (request.EmbedChapters && page.ChapterRefusalReason is { } chapterError) throw new InvalidDataException(chapterError);
        if (page.Media.Count == 0)
        {
            return new NativeDownloadResult
            {
                Succeeded = false,
                Error = page.Warnings.Count > 0 ? string.Join(" ", page.Warnings) : "No downloadable media was found on this page."
            };
        }

        var plan = MediaDownloadPlan.Create(page, request);
        NativeExternalDownloader.ValidateMedia(request, plan.Primary, plan.Audio);
        if(request.LiveDashSelection is not null&&plan.Primary.Kind!=MediaKind.Dash)
            throw new NotSupportedException("Live DASH selection preferences require a DASH source.");
        if((request.LiveDurationSeconds is not null||request.LiveFromStart)&&plan.Primary.Kind is not (MediaKind.Hls or MediaKind.Dash))
            throw new NotSupportedException("These live recording controls require a native HLS or live DASH source.");
        var fileLimits=new NativeFileSizeLimits(request);
        var embedCover = request.EmbedThumbnail && !string.IsNullOrWhiteSpace(page.ThumbnailUrl);
        var expectedExtension = ExpectedExtension(request, plan);
        if (embedCover && !request.AudioOnly && string.IsNullOrWhiteSpace(request.Container) && string.IsNullOrWhiteSpace(request.RecodeVideo))
            expectedExtension = expectedExtension switch { "ts" => "mp4", "webm" => "mkv", _ => expectedExtension };
        var destination = NativeOutputTemplate.Render(request, page, plan.Primary, expectedExtension ?? "mp4");
        if (protectedPaths.Any(path => string.Equals(Path.GetFullPath(destination), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new IOException("The output filename conflicts with the download archive or its lock file.");
        using var destinationLease = await DestinationLock.AcquireAsync(destination, ct).ConfigureAwait(false);
        var inventoryAnchor=destination;var inventoryRequest=request;var manifests=new ManifestPreparation();
        using var workspace=await DownloadWorkspace.OpenAsync(destination,request.Url,protectedPaths,DownloadWorkspace.Recipe(request),ct,request.OutputDirectory,PublicationTransaction.ContextFor(request,page)).ConfigureAwait(false);
        if(await workspace.RecoveredAsync().ConfigureAwait(false) is { } recovered)
        {
            if(plan.Primary.Kind is MediaKind.Hls or MediaKind.Dash&&(request.LiveDurationSeconds is not null||request.LiveFromStart))
                await workspace.MigrateInventoryAsync(InventoryIntent(inventoryRequest,plan,inventoryAnchor,plan.NeedsMerge||request.AudioOnly||request.AddMetadata||request.EmbedSubs||request.EmbedThumbnail||request.EmbedChapters)).ConfigureAwait(false);
            return new(){Succeeded=true,Skipped=request.NoOverwrites||request.NoPostOverwrites,OutputPath=recovered.AllPaths.FirstOrDefault(),OutputPaths=recovered.AllPaths,NewOutputPaths=request.NoOverwrites||request.NoPostOverwrites?[]:recovered.NewPaths,
                SidecarPaths=recovered.CommittedSidecars,NewSidecarPaths=recovered.CommittedSidecars,PublicationCompletion=workspace.PendingCompletion};
        }
        NativeExec.CheckPending(workspace.RawDirectory, request);
        if(await ExistingRecordedOutputsAsync(request,plan,inventoryAnchor,protectedPaths,log,ct).ConfigureAwait(false) is { } recordedOutputs)return recordedOutputs;
        if(await ExistingOutputsAsync(request,page,plan,http,expectedExtension,protectedPaths,log,ct,manifests).ConfigureAwait(false) is { } existingOutputs)
            return existingOutputs;
        var estimatedRawExtension = plan.NeedsMerge ? plan.Extension : plan.Primary.Kind == MediaKind.Hls ? "ts" : plan.Primary.Extension ?? "mp4";
        var protectProcessed = request.NoPostOverwrites && (plan.NeedsMerge || expectedExtension is not null && WouldModify(request, page, estimatedRawExtension, expectedExtension, plan.Primary.HasAudio));
        var skipProcessing = (request.NoOverwrites || protectProcessed) && expectedExtension is not null && File.Exists(destination);
        if (skipProcessing && !request.KeepOriginal)
        {
            log?.Invoke("[native] already saved " + destination);
            return new() { Succeeded = true, Skipped = true, OutputPath = destination, OutputPaths = [destination] };
        }
        if (!skipProcessing && (NativeSponsorBlock.Enabled(request) || NativeProcessingOptions.Fixup(request.Fixup) is not ("" or "never")))
        {
            if (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath)) throw new FileNotFoundException("ffmpeg is required for the requested fixup or SponsorBlock processing.");
            FfmpegTool.RequireProbe(request.FfmpegPath, "prepare requested media processing");
        }
        if (!skipProcessing && NativeSponsorBlock.Enabled(request))
        {
            FfmpegTool.RequireProbe(request.FfmpegPath, "apply SponsorBlock");
            request = request with { SponsorEdits = await NativeSponsorBlock.ReadAsync(request, page, http, workspace.RawDirectory, ct).ConfigureAwait(false) };
            ChapterMetadataWriter.Render(request.SponsorEdits.Metadata(page, request).Chapters);
        }
        if (!skipProcessing) SubtitleFormats.ValidateSelection(request, SubtitleSelection.Select(page.Subtitles, request));
        if (!skipProcessing && request.EmbedChapters) ChapterMetadataWriter.Render(page.Chapters);
        if (!skipProcessing && request.AudioOnly) FfmpegTool.RequireProbe(request.FfmpegPath, "choose and verify the audio format");
        if (!skipProcessing && !string.IsNullOrWhiteSpace(request.RecodeVideo))
        {
            FfmpegTool.RequireProbe(request.FfmpegPath, "convert and verify the video");
            if (VideoRecodeProfile.Parse(request.RecodeVideo)!.Extension is "avi" or "flv" or "ogg" && request.EmbedChapters && page.Chapters.Count > 0)
                throw new NotSupportedException("The requested conversion container cannot preserve embedded chapters.");
        }
        if (!skipProcessing && embedCover)
        {
            if (expectedExtension is not null) FfmpegTool.ValidateCoverContainer(expectedExtension);
            FfmpegTool.RequireProbe(request.FfmpegPath, "embed cover images");
        }
        var selected = plan.Primary;
        if (selected.Kind == MediaKind.HttpSequence || plan.Audio?.Kind == MediaKind.HttpSequence)
            FfmpegTool.RequireProbe(request.FfmpegPath, "join the complete ordered media files");
        if ((selected.VerificationDemuxer is not null || plan.Audio?.VerificationDemuxer is not null || selected.Kind == MediaKind.HttpSequence || plan.Audio?.Kind == MediaKind.HttpSequence) &&
            (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath)))
            throw new InvalidOperationException("ffmpeg is required to verify the downloaded media before saving it.");
        if ((plan.NeedsMerge || !skipProcessing && (request.AudioOnly || request.VideoOnly && selected.HasAudio != false ||
            request.EmbedSubs || request.EmbedThumbnail || request.AddMetadata || request.EmbedChapters && page.Chapters.Count > 0 ||
            !string.IsNullOrWhiteSpace(request.Container) && selected.Extension != request.Container || !string.IsNullOrWhiteSpace(request.RecodeVideo))) &&
            (string.IsNullOrWhiteSpace(request.FfmpegPath) || !File.Exists(request.FfmpegPath)))
            throw new InvalidOperationException("ffmpeg is required for the requested media processing.");
        if (!skipProcessing && expectedExtension is null or "mp4" or "mov" or "m4v" &&
            (request.AddMetadata || request.EmbedSubs || request.EmbedChapters && page.Chapters.Count > 0 || !string.IsNullOrWhiteSpace(request.Container)))
            FfmpegTool.RequireProbe(request.FfmpegPath, "preserve MP4 timecode during processing");
        log?.Invoke($"[native] {selected.Kind} from {selected.Source}: {selected.Url.Host}");

        await new NativeDownloadDelay(request).WaitAsync(log, ct).ConfigureAwait(false);
        using var retained = request.KeepOriginal ? new RetainedMediaCollector(workspace.RawDirectory) : null;
        request = request with { RetainedMedia = retained, DownloadDelayLog = log,AdaptiveRecoveryScope=System.Text.Json.JsonSerializer.Serialize(new[]{DownloadArchiveIdentity.FromMetadata(page,request.Url).Record,selected.Source,selected.Id??"",plan.Audio?.Id??""}) };
        destination = workspace.RawPath("source." + (selected.Kind == MediaKind.Hls ? "ts" : selected.Extension ?? "mp4"));
        var mergedMedia = plan.NeedsMerge;
        if (selected.Kind == MediaKind.Sabr && (plan.Audio is null || plan.Audio.Kind == MediaKind.Sabr))
        {
            destination = Path.ChangeExtension(destination, plan.Extension);
            destination = await SabrDownloader.DownloadAsync(http, selected, plan.Audio, destination, request, progress, ct).ConfigureAwait(false);
        }
        else if (plan.NeedsMerge)
        {
            destination = Path.ChangeExtension(destination, plan.Extension);
            var pair = await DownloadPairAsync(http, plan, destination, request, progress, ct, page.Subtitles,manifests).ConfigureAwait(false);
            destination = pair.Path; page = page with { Subtitles = pair.Subtitles };
        }
        else if (selected.Kind == MediaKind.Hls)
        {
            var hls = await DownloadHlsWithAudioAsync(http, selected.Url, destination, request, progress, ct, page.Subtitles, selected,manifests).ConfigureAwait(false);
            destination = hls.Path; page = page with { Subtitles = hls.Subtitles };
            mergedMedia = hls.Merged;
            await VerifyHlsCandidateAsync(request, selected, hls, ct).ConfigureAwait(false);
        }
        else if (selected.Kind == MediaKind.HttpSequence)
        {
            log?.Invoke("[native] Downloading and verifying all ordered media files.");
            await HttpSequenceDownloader.DownloadAsync(http, selected, destination, request, progress, ct).ConfigureAwait(false);
        }
        else if (selected.Kind == MediaKind.Progressive)
        {
            var limiter = new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit));
            await new FileDownloader(http) { ExternalDownloader = request.ExternalDownloader, ExternalDownloaderArguments = request.ExternalDownloaderArguments, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NoMtime = request.NoMtime, MinimumBytes=fileLimits.Minimum, MaximumBytes=fileLimits.Maximum }.DownloadAsync(
                selected.Url, destination, limiter, progress, ct, request.Retries).ConfigureAwait(false);
            if (selected.NormalizeMp3ClipBoundary)
            {
                if (retained is not null) await retained.CaptureAsync(destination, "audio", "mp3", selected, ct).ConfigureAwait(false);
                if (await Mp3ClipBoundary.NormalizeAsync(destination, ct).ConfigureAwait(false)) log?.Invoke("[native] Validated complete MP3 frames and normalized the trailing clipping sync byte.");
            }
            if (selected.VerificationDemuxer is not null)
            {
                log?.Invoke("[native] Verifying downloaded media before saving it.");
                await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, destination, selected, ct).ConfigureAwait(false);
            }
        }
        else
        {
            var limiter = new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit));
            DashManifest manifest;
            try{manifest=await manifests.ReadDashAsync(http,selected.Url,ct,request.LiveDurationSeconds is not null||request.LiveFromStart).ConfigureAwait(false);}
            catch(ExpiredPlaybackResourcesException)when(page.Extractor=="kaltura"&&string.IsNullOrWhiteSpace(request.FormatSelector))
            {
                DashManifest? fresh=null;
                foreach(var alternative in page.Media.Where(m=>m.Kind==MediaKind.Dash&&m.Id!=selected.Id).Take(8))
                {try{fresh=await new DashDownloader(http).ReadAsync(alternative.Url,ct).ConfigureAwait(false);selected=alternative;log?.Invoke("[native] Selected another publisher-offered DASH profile with fresh links.");break;}catch(ExpiredPlaybackResourcesException){}}
                manifest=fresh??throw new ExpiredPlaybackResourcesException();
            }

            var liveProjection=manifest.IsDynamic||manifest.LiveProjection&&(request.LiveDurationSeconds is not null||request.LiveFromStart);
            page = page with { Subtitles = liveProjection ? page.Subtitles : await DashSubtitleResolver.ResolveAsync(manifest, page.Subtitles, request, http, workspace.RawDirectory, ct).ConfigureAwait(false) };
            var dash = new DashDownloader(http) { Subtitles=page.Subtitles,RetainedMedia = retained, RecoveryScope=request.AdaptiveRecoveryScope, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NoMtime = request.NoMtime, MergeOutputFormat = request.MergeOutputFormat, PreferFreeFormats = request.PreferFreeFormats,
                BeforeAdditionalTrack = token => new NativeDownloadDelay(request).WaitAsync(request.DownloadDelayLog, token) };
            // A literal selection of the outer manifest is already consumed. Its ID
            // is not an inner DASH representation selector.
            var dashRequest=(!string.IsNullOrWhiteSpace(request.FormatId)&&request.FormatId==selected.Id||!string.IsNullOrWhiteSpace(request.FormatSelector)&&request.FormatSelector==selected.Id)
                ?request with {FormatId=null,FormatSelector=""}:request;
            destination = await dash.DownloadAsync(
                manifest,
                destination, request.Quality, request.MaximumHeight, request.AudioOnly,
                request.FfmpegPath, limiter, progress, ct, request.Retries, request.VideoOnly, request.MaximumFrameRate, request.VideoCodec, request.ConcurrentFragments, request.FragmentRetries,dashRequest).ConfigureAwait(false);
            mergedMedia = dash.Merged; if(liveProjection)page=page with {Subtitles=dash.Subtitles};
            await VerifyDashCandidateAsync(request, selected, destination, ct).ConfigureAwait(false);
        }

        if(fileLimits.Active)fileLimits.Check(new FileInfo(destination).Length);
        AudioOutputPlan? audioPlan = null;
        if (request.AudioOnly && !skipProcessing)
        {
            audioPlan = await FfmpegTool.PlanAudioAsync(request.FfmpegPath!, destination, request.AudioFormat, request.AudioQuality, ct).ConfigureAwait(false);
            if (request.NoOverwrites)
            {
                var existing = NativeOutputTemplate.Render(request, page, selected, audioPlan.Extension);
                if (protectedPaths.Any(path => string.Equals(Path.GetFullPath(existing), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new IOException("The audio output filename conflicts with the download archive or its lock file.");
                using var audioPublication = await DestinationLock.AcquirePublicationAsync(Path.GetDirectoryName(existing)!, ct).ConfigureAwait(false);
                if (File.Exists(existing))
                {
                    if (request.KeepOriginal) skipProcessing = true;
                    else
                    {
                        workspace.CompleteWithoutPublishing();
                        log?.Invoke("[native] already saved " + existing);
                        return new() { Succeeded = true, Skipped = true, OutputPath = existing, OutputPaths = [existing] };
                    }
                }
            }
            if (!skipProcessing && embedCover) FfmpegTool.ValidateCoverContainer(audioPlan.Extension);
        }
        var raw = destination; var rawExtension = Path.GetExtension(raw).TrimStart('.');
        var primingPlan=mergedMedia&&!request.AudioOnly&&!request.VideoOnly?await Mp4AudioPriming.ReadPlanAsync(raw,ct).ConfigureAwait(false):null;
        var finalExtension = ProcessedExtension(request, page, rawExtension, audioPlan);
        var finalTarget = NativeOutputTemplate.Render(request, page, selected, finalExtension);
        protectProcessed = request.NoPostOverwrites && (mergedMedia || WouldModify(request, page, rawExtension, finalExtension, mergedMedia ? true : selected.HasAudio));
        if ((request.NoOverwrites || protectProcessed) && File.Exists(finalTarget)) skipProcessing = true;
        if (!skipProcessing && !string.IsNullOrWhiteSpace(request.RecodeVideo) && rawExtension != finalExtension)
            log?.Invoke("[native] Converting video to " + finalExtension + ".");
        await PublicationFaults.AtAsync("processing").ConfigureAwait(false);
        var processed = skipProcessing ? new ProcessedMedia(raw, [], WouldModify(request, page, rawExtension, finalExtension, plan.NeedsMerge ? true : selected.HasAudio)) :
            await PostProcessAsync(request, page, workspace.PrepareProcessing(raw), http, ct, plan.NeedsMerge ? true : selected.HasAudio, audioPlan, log).ConfigureAwait(false);
        if (!skipProcessing) finalTarget = NativeOutputTemplate.Render(request, page, selected, Path.GetExtension(processed.Path).TrimStart('.'));
        if(!processed.TimelineEdited&&primingPlan is { } priming&&Path.GetExtension(processed.Path) is ".mp4" or ".m4a" or ".mov")
            await Mp4AudioPriming.ApplyAsync(processed.Path,priming.Samples,priming.SampleRate,ct,priming.DelaySeconds).ConfigureAwait(false);
        var outputs = new List<StagedMediaOutput> { new(processed.Path, finalTarget, processed.Sidecars, protectProcessed, request.NoMtime ? null : File.GetLastWriteTimeUtc(raw)) };
        if (request.KeepOriginal && processed.Modified)
        {
            var originalTarget = NativeOutputTemplate.Render(request, page, selected, rawExtension);
            if (SamePath(originalTarget, finalTarget)) originalTarget = AddOutputSuffix(originalTarget, "original");
            outputs.Add(new(raw, originalTarget, [], ModifiedUtc: request.NoMtime ? null : File.GetLastWriteTimeUtc(raw)));
        }
        if (retained is not null)
        {
            var roles = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in retained.Items)
            {
                roles[item.Role] = roles.GetValueOrDefault(item.Role) + 1;
                var suffix = item.Role + (roles[item.Role] == 1 ? "" : "-" + roles[item.Role]);
                var target = NativeOutputTemplate.Render(request, page, item.Format ?? selected, item.Extension);
                outputs.Add(new(item.Path, AddOutputSuffix(target, suffix), [], ModifiedUtc: request.NoMtime ? null : item.ModifiedUtc));
            }
        }
        using var publicationLease = await DestinationLock.AcquirePublicationsAsync(outputs.Select(output => Path.GetDirectoryName(output.Target)!), ct).ConfigureAwait(false);
        if(plan.Primary.Kind is MediaKind.Hls or MediaKind.Dash&&(manifests.LiveDetected||inventoryRequest.LiveDurationSeconds is not null||inventoryRequest.LiveFromStart))
            workspace.SetInventoryIntent(InventoryIntent(inventoryRequest,plan,inventoryAnchor,mergedMedia||processed.Modified));
        var published = await workspace.PublishAsync(outputs, request.NoOverwrites, ct, paths => NativeExec.RunAsync(request, page, finalTarget, paths, workspace.RawDirectory, ct)).ConfigureAwait(false);
        foreach (var path in published.AllPaths) log?.Invoke((published.NewPaths.Contains(path) ? "[native] saved " : "[native] already saved ") + path);
        return new NativeDownloadResult { Succeeded = true, Skipped = published.NewPaths.Count == 0, OutputPath = finalTarget, OutputPaths = published.AllPaths, NewOutputPaths = published.NewPaths,PublicationCompletion=workspace.PendingCompletion,
            SidecarPaths = published.CommittedSidecars, NewSidecarPaths = published.CommittedSidecars };
    }

    private static IReadOnlyList<string> NewlySaved(NativeDownloadResult result) => result.NewOutputPaths ?? (result.Skipped ? [] : result.OutputPaths);
    private static async Task CompleteCallbacksAsync(NativeDownloadResult result,Action<string>? callback)
    {
        if(result.PublicationCompletion is { } completion)await DownloadWorkspace.FinishAsync(completion,result.OutputPaths.Count==0?null:callback).ConfigureAwait(false);
        else foreach(var path in NewlySaved(result))callback?.Invoke(path);
    }
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string AddOutputSuffix(string path, string suffix) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "." + suffix + Path.GetExtension(path));

    public NativeAuthenticationCapabilities GetAuthenticationCapabilities(Uri url) => _providers.Resolve(url).AuthenticationCapabilities;
    public bool SupportsGeoVerificationProxy(Uri url) => _providers.Resolve(url).SupportsGeoVerificationProxy;
    private async Task<NativeDownloadRequest> SignInAsync(NativeDownloadRequest request,MediaHttp http,CancellationToken ct)
    {
        if(!request.Authentication.OAuthLogin)return request;
        var credentials=await Authentication.NativeOAuthBrowser.SignInAsync(request.Authentication.OAuth!,http,ct,_accountBrowserOpen).ConfigureAwait(false);
        return request with {Authentication=request.Authentication with {OAuth=credentials,OAuthLogin=false}};
    }
    public void ValidateAuthenticationOptions(NativeDownloadRequest request) => NativeAuthentication.Validate(request,GetAuthenticationCapabilities(request.Url));

    public static bool CanHandle(Uri url) => DefaultProviders.CanHandle(url);

    private Task<PageMetadata> LoadAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) =>
        LoadEmbeddedAsync(request, http, ct, new HashSet<string>(StringComparer.Ordinal), 0);

    private async Task<PageMetadata> LoadEmbeddedAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct, HashSet<string> ancestors, int depth)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 5 || !ancestors.Add(request.Url.AbsoluteUri)) throw new InvalidDataException("The embedded media references are recursive or too deeply nested.");
        try
        {
            var provider = _providers.Resolve(request.Url);
            request = await NativeAuthentication.PrepareAsync(request, provider, ct).ConfigureAwait(false);
            PageMetadata extracted;
            using (NativeExtractionDelay.Extracting()) extracted = await provider.ExtractAsync(request, http, ct).ConfigureAwait(false);
            var page = ThumbnailCatalog.Prepare(extracted);
            if(request.Authentication.Hosting is { } hosting&&provider.Info.Id is "generic" or "consumer-hosting" or "nowness")
            {
                var matched=page.EmbeddedPages.Where(hosting.Matches).DistinctBy(u=>u.AbsoluteUri,StringComparer.Ordinal).ToArray();
                if(matched.Length!=1)throw new InvalidDataException("The page did not declare one unambiguous media item within the selected hosting credential scope.");
                return BindDelegation(page,await LoadEmbeddedAsync(request with {Url=matched[0],Referer=string.IsNullOrWhiteSpace(request.Referer)?(page.SourcePageUrl??request.Url).AbsoluteUri:request.Referer},http,ct,ancestors,depth+1).ConfigureAwait(false),matched[0]);
            }
            page = await NativeDynamicDash.FilterAsync(page, request, http, ct).ConfigureAwait(false);
            if (page.Media.Count > 0 || page.IsPlaylist) return page;
            var children = page.EmbeddedPages.Where(uri => _providers.CanHandle(uri) && _providers.Resolve(uri).Info.Id != "generic")
                .DistinctBy(uri => uri.AbsoluteUri, StringComparer.Ordinal).ToArray();
            if (children.Length == 0) return page;
            if (children.Length > request.MaximumPlaylistEntries) throw new InvalidDataException("The embedded media count exceeds the playlist limit.");
            if (children.Length == 1)
                return BindDelegation(page,await LoadEmbeddedAsync(request with { Url = children[0], Referer = string.IsNullOrWhiteSpace(request.Referer) ? (page.SourcePageUrl ?? request.Url).AbsoluteUri : request.Referer }, http, ct, ancestors, depth + 1).ConfigureAwait(false),children[0]);
            var entries = new List<NativePlaylistEntry>();
            foreach (var child in children)
            {
                try
                {
                    var info = await LoadEmbeddedAsync(request with { Url = child, ExpandPlaylist = false, Referer = string.IsNullOrWhiteSpace(request.Referer) ? (page.SourcePageUrl ?? request.Url).AbsoluteUri : request.Referer }, http, ct, ancestors, depth + 1).ConfigureAwait(false);
                    entries.Add(new(entries.Count + 1, info.Id, info.Title, child, info.DurationSeconds, info.ThumbnailUrl, true, info));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException or NotSupportedException)
                {
                    entries.Add(new(entries.Count + 1, null, "Unavailable embedded video", child, null, null, true,
                        new PageMetadata { Title = "Unavailable embedded video", Warnings = [ex.Message] }));
                }
            }
            return page with { IsPlaylist = true, Entries = entries, Extractor = "generic:embeds", Id = page.Id ?? request.Url.AbsolutePath };
        }
        finally { ancestors.Remove(request.Url.AbsoluteUri); }
    }
    private static PageMetadata BindDelegation(PageMetadata parent,PageMetadata child,Uri target)
    {
        if(parent.DelegatedArchiveIdentity is null)return child;
        if(child.IsPlaylist||DownloadArchiveIdentity.FromMetadata(child,target).Record!=parent.DelegatedArchiveIdentity.Record)throw new InvalidDataException("The hosted response changed the consumer's declared video identity.");
        var fields=new Dictionary<string,object?>(child.MetadataFields,StringComparer.Ordinal);foreach(var pair in parent.MetadataFields)fields[pair.Key]=pair.Value;return child with {MetadataFields=fields};
    }

    private static async Task<DownloadedTrack> DownloadPairAsync(MediaHttp http, MediaDownloadPlan plan, string destination,
        NativeDownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken ct, IReadOnlyList<SubtitleTrack> existingSubtitles,ManifestPreparation manifests)
    {
        if (!string.IsNullOrWhiteSpace(request.MergeOutputFormat)) FfmpegTool.RequireProbe(request.FfmpegPath, "choose the merge container");
        var video = destination + ".native-video." + plan.Primary.Extension;
        var audio = destination + ".native-audio." + plan.Audio!.Extension;
        var merged = Path.Combine(Path.GetDirectoryName(destination)!, ".merge-" + Guid.NewGuid().ToString("N") + "." + plan.Extension);
        var files = new FileDownloader(http) { ExternalDownloader = request.ExternalDownloader, ExternalDownloaderArguments = request.ExternalDownloaderArguments, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NoMtime = request.NoMtime };
        var limiter = new ByteRateLimiter(ByteRateLimiter.Parse(request.RateLimit));
        try
        {
            var downloadedVideo = await DownloadTrackAsync(http, files, plan.Primary, video, request with { VideoOnly = true, AudioOnly = false }, limiter, progress, ct, existingSubtitles,manifests).ConfigureAwait(false);
            video = downloadedVideo.Path;
            await new NativeDownloadDelay(request).WaitAsync(request.DownloadDelayLog, ct).ConfigureAwait(false);
            audio = (await DownloadTrackAsync(http, files, plan.Audio, audio, request with { AudioOnly = true, VideoOnly = false, WriteSubs = false, WriteAutoSubs = false, EmbedSubs = false }, limiter, progress, ct, [],manifests).ConfigureAwait(false)).Path;
            if (!string.IsNullOrWhiteSpace(request.MergeOutputFormat))
            {
                var extension = await FfmpegTool.SelectMergeExtensionAsync(request.FfmpegPath!, video, audio, request.MergeOutputFormat, plan.Extension, ct).ConfigureAwait(false);
                destination = Path.ChangeExtension(destination, extension); merged = Path.ChangeExtension(merged, extension);
            }
            if (plan.Primary.LoopVideoToAudio)
                await FfmpegTool.MergeLoopedVideoAsync(request.FfmpegPath!, video, audio, merged, plan.Primary.Url, ct).ConfigureAwait(false);
            else await FfmpegTool.MergeAsync(request.FfmpegPath!, video, audio, merged, ct,
                preserveTimestamps: !string.IsNullOrEmpty(plan.Primary.TimelineId) && plan.Primary.TimelineId == plan.Audio!.TimelineId).ConfigureAwait(false);
            if (!File.Exists(merged) || new FileInfo(merged).Length == 0) throw new InvalidDataException("ffmpeg did not produce a merged media file.");
            ct.ThrowIfCancellationRequested();
            File.Move(merged, destination, overwrite: true);
            if (request.RetainedMedia is { } retained)
            {
                await retained.CaptureAsync(video, "video", Path.GetExtension(video).TrimStart('.'), plan.Primary, ct).ConfigureAwait(false);
                await retained.CaptureAsync(audio, "audio", Path.GetExtension(audio).TrimStart('.'), plan.Audio, ct).ConfigureAwait(false);
            }
            TryDelete(video);
            TryDelete(audio);
            return new(destination, downloadedVideo.Subtitles, Merged: true);
        }
        finally { TryDelete(merged); }
    }

    private static void ValidateRequest(NativeDownloadRequest request)
    {
        NativePlayerScriptCache.Validate(request);
        request.LiveDashSelection?.Validate();
        NativeExec.Validate(request.ExecCommands);
        NativeExternalDownloader.Validate(request.ExternalDownloader, request.ExternalDownloaderArguments);
        NativeSponsorBlock.Validate(request.SponsorBlockMark, request.SponsorBlockRemove);
        NativeProcessingOptions.Validate(request.Fixup, request.PostprocessorArguments);
        _ = VideoRecodeProfile.Parse(request.RecodeVideo);
        if(request.LiveDurationSeconds is { } liveDuration&&(!double.IsFinite(liveDuration)||liveDuration<=0||liveDuration>86400))
            throw new ArgumentException("Choose a finite live recording duration greater than zero and at most 24 hours.");
        NativeMetadataParser.Validate(request.MetadataParseRules);
        _ = new NativeFileSizeLimits(request);
        _ = new NativeDownloadDelay(request);
        Media.NativeSubtitleDelay.Validate(request.SubtitleSleepSeconds);
        NativeExtractionDelay.Validate(request.RequestSleepSeconds);
        _ = NativeReadBuffer.ParseSize(request.BufferSize);
        _ = NativeHttpChunkSize.Parse(request.HttpChunkSize);
        _ = MergeOutputFormats.Parse(request.MergeOutputFormat);
        if (!string.IsNullOrWhiteSpace(request.RecodeVideo) && (request.AudioOnly || !string.IsNullOrWhiteSpace(request.Container)))
            throw new ArgumentException("Choose video recoding, audio extraction or container remuxing for a job.");
        if (request.SelectionState is null) _ = new DownloadSelectionState(request);
        NetworkConnection.Validate(request.SourceAddress, request.IpVersion);
        NativeHttpHeaders.Validate(request.HttpHeaders);
        if (request.AudioOnly) AudioOutputPlan.Validate(request.AudioFormat, request.AudioQuality);
        if (request.MaximumPlaylistEntries is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        PlaylistSelection.Validate(request);
        NativeOutputTemplate.Validate(request.OutputTemplate);
        NativeFormatSelector.Validate(request.FormatSelector);
        if (request.MaximumFrameRate is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(request.MaximumFrameRate));
        FormatFacts.CodecMatches(null, request.VideoCodec);
        OrderedFragmentTransfer.Validate(request.ConcurrentFragments);
        if (request.Retries is < 0 or > 1000 || request.FragmentRetries is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(request.Retries));
        _ = ByteRateLimiter.Parse(request.RateLimit);
        if (string.IsNullOrWhiteSpace(request.CookieFile)) _ = BrowserCookieSelection.Parse(request.BrowserCookies);
        if (request.WriteSubs || request.WriteAutoSubs || request.EmbedSubs)
            SubtitleSelection.Validate(request.SubLanguages);
        _ = SubtitleFormats.Preferences(request.SubFormat);
        _ = SubtitleFormats.ConversionTarget(request.ConvertSubs);
    }

    private static async Task<DownloadedTrack> DownloadTrackAsync(MediaHttp http, FileDownloader files, MediaCandidate candidate, string path,
        NativeDownloadRequest request, ByteRateLimiter limiter, IProgress<DownloadProgress>? progress, CancellationToken ct, IReadOnlyList<SubtitleTrack> existingSubtitles,ManifestPreparation manifests)
    {
        if (candidate.Kind == MediaKind.HttpSequence)
        {
            await HttpSequenceDownloader.DownloadAsync(http, candidate, path, request, progress, ct).ConfigureAwait(false);
            return new(path, existingSubtitles);
        }
        if (candidate.Kind == MediaKind.Hls)
        {
            var track = await DownloadHlsWithAudioAsync(http, candidate.Url, path, request, progress, ct, existingSubtitles, candidate,manifests).ConfigureAwait(false);
            await VerifyHlsCandidateAsync(request, candidate, track, ct).ConfigureAwait(false);
            return track;
        }
        if (candidate.Kind == MediaKind.Sabr)
            return new(await SabrDownloader.DownloadAsync(http, candidate, null, path, request, progress, ct).ConfigureAwait(false), existingSubtitles);
        if (candidate.Kind == MediaKind.Dash)
        {
            var downloader = new DashDownloader(http) { RetainedMedia = request.RetainedMedia, RecoveryScope=request.AdaptiveRecoveryScope, NoContinue = request.NoContinue, NoPart = request.NoPart, BufferSize = request.BufferSize, NoResizeBuffer = request.NoResizeBuffer, HttpChunkSize = request.HttpChunkSize, TransferLog = request.DownloadDelayLog, NoMtime = request.NoMtime, MergeOutputFormat = request.MergeOutputFormat, PreferFreeFormats = request.PreferFreeFormats,
                BeforeAdditionalTrack = token => new NativeDownloadDelay(request).WaitAsync(request.DownloadDelayLog, token) };
            var manifest = await downloader.ReadAsync(candidate.Url, ct).ConfigureAwait(false);
            var subtitles = await DashSubtitleResolver.ResolveAsync(manifest, existingSubtitles, request, http, Path.GetDirectoryName(path)!, ct).ConfigureAwait(false);
            var output = await downloader.DownloadAsync(manifest, path,
                request.Quality, request.MaximumHeight, request.AudioOnly, request.FfmpegPath, limiter, progress, ct, request.Retries,
                request.VideoOnly, request.MaximumFrameRate, request.VideoCodec, request.ConcurrentFragments, request.FragmentRetries,request).ConfigureAwait(false);
            await VerifyDashCandidateAsync(request, candidate, output, ct).ConfigureAwait(false);
            return new(output, subtitles);
        }
        await files.DownloadAsync(candidate.Url, path, limiter, progress, ct, request.Retries).ConfigureAwait(false);
        if (candidate.VerificationDemuxer is not null)
            await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, path, candidate, ct).ConfigureAwait(false);
        return new(path, existingSubtitles);
    }

    private static async Task VerifyHlsCandidateAsync(NativeDownloadRequest request, MediaCandidate candidate, DownloadedTrack track, CancellationToken ct)
    {
        if (candidate.VerificationDemuxer is null) return;
        var path = track.Path;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var demuxer = extension is ".mp4" or ".m4a" ? "mov" : extension == ".ts" ? "mpegts" : throw new NotSupportedException("This HLS output container requires additional native verification support.");
        if (request.AudioOnly) await FfmpegTool.RequireAudioTrackAsync(request.FfmpegPath!, path, ct).ConfigureAwait(false);
        await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, path, new MediaCandidate { Url = candidate.Url, Kind = MediaKind.Progressive,
            HasVideo = request.AudioOnly ? false : candidate.HasVideo, HasAudio = request.VideoOnly ? false : request.AudioOnly ? true : candidate.HasAudio,
            VerificationDemuxer = demuxer, VerificationDurationSeconds = candidate.VerificationDurationSeconds ?? track.ManifestDurationSeconds,
            VerificationMinimumDurationSeconds = candidate.VerificationMinimumDurationSeconds }, ct).ConfigureAwait(false);
    }

    private static async Task VerifyDashCandidateAsync(NativeDownloadRequest request, MediaCandidate candidate, string path, CancellationToken ct)
    {
        if (candidate.VerificationDemuxer is null) return;
        var demuxer = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4a" or ".mov" => "mov",
            ".mkv" or ".webm" => "matroska",
            _ => throw new NotSupportedException("This DASH output container requires additional native verification support.")
        };
        if (request.AudioOnly) await FfmpegTool.RequireAudioTrackAsync(request.FfmpegPath!, path, ct).ConfigureAwait(false);
        await FfmpegTool.VerifyDownloadedMediaAsync(request.FfmpegPath!, path, new MediaCandidate { Url = candidate.Url, Kind = MediaKind.Progressive,
            HasVideo = request.AudioOnly ? false : candidate.HasVideo, HasAudio = request.VideoOnly ? false : request.AudioOnly ? true : candidate.HasAudio,
            VerificationDemuxer = demuxer, VerificationDurationSeconds = candidate.VerificationDurationSeconds }, ct).ConfigureAwait(false);
    }

    private static string? ExpectedExtension(NativeDownloadRequest request, MediaDownloadPlan plan) =>
        request.AudioOnly ? AudioOutputPlan.ExtensionFor(request.AudioFormat) :
        VideoRecodeProfile.Parse(request.RecodeVideo) is { } recode ? recode.Extension :
        !string.IsNullOrWhiteSpace(request.Container) ? request.Container :
        plan.NeedsMerge ? MergeOutputFormats.CanPredict(request.MergeOutputFormat, plan.Primary.VideoCodec, plan.Audio!.AudioCodec) ? plan.Extension : null :
        plan.Primary.Kind is MediaKind.Progressive or MediaKind.HttpSequence ? plan.Primary.Extension ?? "mp4" : null;

    private static HlsVariant SelectVariant(HlsPlaylist playlist, NativeDownloadRequest request)
    {
        var variants = playlist.Variants;
        if (variants.Count == 0)
            throw new InvalidDataException("The HLS master playlist has no variants.");
        if (request.MaximumHeight is > 0)
        {
            variants = variants.Where(item => item.Height <= request.MaximumHeight).ToList();
            if (variants.Count == 0) throw new InvalidOperationException("No HLS variant satisfies the maximum video height.");
        }
        if (!request.AudioOnly)
        {
            variants = variants.Where(item => (request.MaximumFrameRate <= 0 || item.FramesPerSecond <= request.MaximumFrameRate) &&
                FormatFacts.CodecMatches(item.VideoCodec, request.VideoCodec)).ToList();
            if (variants.Count == 0) throw new InvalidOperationException("No HLS variant satisfies the codec and frame-rate constraints.");
        }

        if (request.Quality == QualityPreference.Worst)
            return variants.OrderBy(static item => item.Height ?? int.MaxValue)
                .ThenBy(static item => item.Bandwidth ?? int.MaxValue).First();

        return variants.OrderByDescending(static item => item.Height ?? 0)
            .ThenByDescending(static item => item.FramesPerSecond ?? 0)
            .ThenByDescending(static item => item.Bandwidth ?? 0)
            .First();
    }
}








