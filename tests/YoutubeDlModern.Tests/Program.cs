using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Threading;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    private static int _failures;
    private static int _checks;
    private static readonly string TestRoot = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main(string[] args)
    {
        if (TryRunRuntimeRegressionHelper(args, out var helperExitCode)) return helperExitCode;
        if (args.Length >= 3 && args[0] == "--native-exec-helper") { NativeExecHelper(args[1], args.Skip(2).ToArray()); return 0; }
        var preview=Array.IndexOf(args,"--wpf-policy-preview");if(preview>=0)WpfLadderPreview=Path.GetFullPath(args[preview+1]);
        Directory.CreateDirectory(TestRoot);
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (args.Contains("--stream-quality-only")) { foreach (var (name, test) in StreamQualityDefaultChecks) await Check(name, test); return; }
                if (args.Contains("--runtime-regressions-only")) { foreach (var (name, test) in RuntimeRegressionChecks) await Check(name, test); return; }
                if(args.Contains("--wpf-ladders-only")){foreach(var (name,test) in WpfLadderAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-selectors-only")){foreach(var (name,test) in HdrSelectorAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-reconstruct-only")){foreach(var (name,test) in HdrReconstructAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-transform-only")){foreach(var (name,test) in HdrTransformAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-spatial-only")){foreach(var (name,test) in HdrSpatialAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-metadata-only")){foreach(var (name,test) in HdrMetadataAppChecks)await Check(name,test);return;}
                if(args.Contains("--hdr-gamut-only")){foreach(var (name,test) in HdrGamutAppChecks)await Check(name,test);return;}
                if(args.Contains("--color-audio-only")){foreach(var (name,test) in ColorAudioAppChecks)await Check(name,test);return;}
                if (args.Contains("--audit-regressions-only")) { foreach (var (name, test) in AuditRegressionChecks) await Check(name, test); return; }
                foreach (var (name, test) in RuntimeRegressionChecks) await Check(name, test);
                foreach (var (name, test) in AuditRegressionChecks) await Check(name, test);
                foreach (var (name, test) in StreamQualityDefaultChecks) await Check(name, test);
                await Check("Format selectors honor offered heights and VP9 spellings", Selectors);
                await Check("Analysis excludes download side effects", AnalysisArguments);
                await Check("Fragment retries can be disabled and estimated sizes have a namespace", DownloadArguments);
                await Check("Playlist progress returns to downloading", PlaylistProgress);
                await Check("Rapid queued cancellation and resume preserve workers", QueueCancellation);
                await Check("Partial playlist files survive failure", PartialFailure);
                await Check("Partial playlist files survive cancellation", PartialCancellation);
                await Check("Shutdown drains successful completion and history", ShutdownCompletion);
                await Check("Preview options are ready before the thumbnail", PreviewReadiness);
                await Check("Discarded and canceled previews reject late results", StalePreview);
                await Check("Changing type or filters clears custom selection", CustomSelection);
                await Check("Saved authentication refreshes without overwriting overrides", Authentication);
                await Check("Backup failure preserves the committed settings", BackupFailure);
                await Check("ffmpeg recovery survives replacement until verification", FfmpegRollback);
                await Check("Canceled and oversized batches enqueue nothing", BatchAdmission);
                await Check("Engine installation publishes its status change", EngineInstallation);
                await Check("Native download settings honor the configured archive", NativeArchive);
                await Check("Native job settings preserve authentication and fragment choices", NativeAuthentication);
                await Check("Native previews expose ready formats and preserve manual and automatic captions", NativeAvailability);
                await Check("Native extra arguments enable standalone files with per-job precedence", NativeMetadataArguments);
                await Check("Native subtitle preferences and all-language settings support job overrides", NativeSubtitleSettings);
                await Check("Native playlist job overrides preserve ordered selections and sequential filenames", NativePlaylistSelection);
                await Check("Native network settings and per-job overrides reach typed requests", NativeNetworkSettings);
                await Check("Native title filters download limits and abort settings honor job overrides", NativeSelectionSettings);
                await Check("Native recode and post-overwrite settings honor explicit containers and audio jobs", NativeRecodeSettings);
                await Check("Native saved merge and free-format settings honor explicit containers and job overrides", NativeMergeSettings);
                await Check("Native queued playlist selection keeps the inspected video after listing changes", NativeSnapshotQueue);
                await Check("Native queued selections remain independent of shared options and later previews", NativeSnapshotIsolation);
                await Check("Native analysis modes control playlist expansion", NativeAnalysisModes);
                await Check("Native analysis preserves saved connections and explicit authentication", NativeAnalysisConnections);
                await Check("Native format listing resolves the first nested playlist entry", NativePlaylistFormats);
                await Check("Native format listing preserves resolved provider formats without reopening parent pages", NativeResolvedFormats);
                await Check("Native format listing refuses unavailable and cyclic collections", NativePlaylistFormatBoundaries);
                await Check("Native format listing drains cancellation during child inspection", NativePlaylistFormatCancellation);
                await Check("Native unsupported saved settings fail before discovery without exposing values", NativeUnsupportedSettings);
                await Check("Built-in downloader startup avoids external engine installation", PracticalNativeStartup);
                await Check("Built-in downloader status and maintenance controls", PracticalNativeStatus);
                foreach (var (name, test) in QualityPreviewChecks.All) await Check(name, test);
                await Check("Native extraction request pacing and preflight", NativeExtractionTimingArguments);
                await Check("Native ARD Sounds and legacy host routing", NativeArdSoundsRoutes);
                await Check("Native subtitle timing arguments and preflight", NativeSubtitleTimingArguments);
                await Check("Native NRK opaque address mapping", NativeNrkOpaqueMapping);
                await Check("Native dynamic DASH argument mapping and preflight", NativeDynamicDashArguments);
                await Check("Native geographic setting headers and precedence", NativeGeoSettingHeaders);
                await Check("Native Radio France provider routing", NativeRadioFranceRoute);
                foreach (var family in new[] { "on", "radio" }) await Check("Native ORF broadcast " + family, () => NativeOrfBroadcast(family));
                foreach (var provider in new[] { "tvp", "arte", "orf" }) await Check("Native public family " + provider, () => NativeProviderFamily(provider));
                foreach (var mode in new[] { "saved", "overrides", "analysis", "immutable", "invalid", "unsupported" }) await Check("Native RTVE verification " + mode, () => NativeRtveVerificationMapping(mode));
                foreach (var mode in new[] { "saved", "overrides", "analysis", "immutable", "invalid" }) await Check("Native external downloader " + mode, () => NativeExternalDownloaderMapping(mode));
                foreach (var mode in new[] { "saved", "overrides", "analysis", "immutable", "invalid", "execution", "case" }) await Check("Native exec/SponsorBlock " + mode, () => NativeExecSponsorMapping(mode));
                foreach (var mode in new[] { "saved", "overrides", "analysis", "immutable", "invalid" }) await Check("Native processing options " + mode, () => NativeProcessingOptionsMapping(mode));
                foreach (var mode in new[] { "flags", "overrides", "analysis", "cleanup", "immutable", "invalid" }) await Check("Native music/output options " + mode, () => NativeMusicOptions(mode));
                foreach (var mode in new[] { "saved", "disabled", "override", "analysis", "invalid", "immutable" }) await Check("Native public cache option " + mode, () => NativePublicCache(mode));
                await Check("Native retry limits and saved subtitle languages retain requested values", NativeRequestValues);
                await Check("Native invalid download choices fail before discovery", NativeInvalidChoices);
                await Check("Native metadata date view age and match filters preserve saved and job settings", NativeMetadataSelectionSettings);
                await Check("Native metadata parsing preserves saved general and job rule order", NativeMetadataParsingSettings);
                await Check("Native metadata parsing invalid rules fail before discovery or output", NativeMetadataParsingPreflight);
                await Check("Native metadata parsing saved rules reach filenames and descriptive JSON", NativeMetadataParsingPublication);
                await Check("Native Vocaroo analysis preserves recording identity and native audio format without media transfer", NativeVocarooAnalysis);
                await Check("Native Clyp analysis preserves public playback identity date and audio format without media transfer", NativeClypAnalysis);
                await Check("Authentication batch saved analysis maps scoped account settings",AuthBatchSavedAnalysis);
                await Check("Authentication batch job credentials preserve override order",AuthBatchJobOrder);
                await Check("Authentication batch saved netrc lookup preserves files and settings",AuthBatchNetrc);
                await Check("Authentication batch television fields remain explicit boundaries",AuthBatchTelevisionBoundary);
                await Check("Live batch job recording policies preserve saved overrides",LiveBatchJobPolicy);
                await Check("Native batched analysis honors boolean controls and opaque download values", NativeBatchedAnalysisControls);
                await Check("Native batched analysis rejects unsupported authentication before discovery", NativeBatchedAuthenticationPreflight);
                await Check("Native batched job arguments preserve saved and job precedence", NativeBatchedJobOrder);
                await Check("Native metadata filtering preserves archives history callbacks and inspection facts", NativeMetadataSelectionEffects);
                await Check("Native file-size settings and job overrides preserve byte bounds and output state", NativeFileSizeSettings);
                await Check("Native saved delay settings and job overrides preserve cancellation and publication", NativeDelaySettings);
                await Check("Native saved file-time settings and job overrides preserve timestamp policy", NativeFileTimeSettings);
                await Check("Native saved no-part settings and job overrides preserve direct transfer policy", NativeNoPartSettings);
                await Check("Native saved buffer settings and job overrides preserve resizing policy", NativeBufferSettings);
                await Check("Native saved HTTP chunk settings and job overrides preserve range policy", NativeHttpChunkSettings);
                await Check("Newgrounds browser contract and pre-cancellation preserve session boundaries", NewgroundsBrowserContract);
                await Check("Newgrounds browser unsupported connections fail before acquisition", NewgroundsBrowserValidation);
                await Check("Newgrounds browser deadlines remain distinct from caller cancellation", NewgroundsBrowserTimeout);
                await Check("Newgrounds browser gate cancellation drains and permits later sessions", NewgroundsBrowserGate);
                foreach(var (name,test) in WpfLadderAppChecks)await Check(name,test);
                foreach(var (name,test) in ColorAudioAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrGamutAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrMetadataAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrSpatialAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrTransformAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrReconstructAppChecks)await Check(name,test);
                foreach(var (name,test) in HdrSelectorAppChecks)await Check(name,test);
                foreach (var (name, test) in BrowserRuntimeChecks.All) await Check(name, test);
                if (args.Contains("--browser-tools")) foreach (var (name, test) in BrowserTransportChecks.All) await Check(name, test, TimeSpan.FromSeconds(45));
            }
            catch (Exception ex) { _failures++; Console.Error.WriteLine(ex); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        Directory.Delete(TestRoot, recursive: true);
        Console.WriteLine(_failures == 0 ? $"All {_checks} regression checks passed." : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Task> test, TimeSpan? timeout = null)
    {
        _checks++;
        try { await test().WaitAsync(timeout ?? TimeSpan.FromSeconds(20)); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static async Task NativeArchive()
    {
        var archive = Path.Combine(TestRoot, "native-history.txt");
        await File.WriteAllTextAsync(archive, "youtube abcdefghijk\n");
        var settings = new AppSettings(); settings.General.DownloadArchive = archive;
        var engine = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: _ => throw new InvalidOperationException("Archived native jobs must not contact the website.")));
        var result = await engine.DownloadAsync("https://youtu.be/abcdefghijk", new DownloadOptions { OutputDirectory = TestRoot }, settings, null,
            new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(result.ExitCode == 0 && result.FinalFilePaths.Count == 0 && File.ReadAllText(archive) == "youtube abcdefghijk\n", "The app's archive setting was not carried into the native request.");
    }
    private static async Task NativeAuthentication()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        {
            requests.Add(request);
            return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler());
        }));
        var browser = "firefox:" + Path.Combine(TestRoot, "selected-profile") + "::Work";
        var cookieFile = Path.Combine(TestRoot, "selected-cookies.txt");
        await adapter.GetVideoInfoAsync("https://media.example/video.mp4", default, new AnalysisOptions { CookiesFromBrowser = browser });
        Assert(requests[^1].BrowserCookies == browser && string.IsNullOrEmpty(requests[^1].CookieFile), "Analysis lost the selected browser/profile/container.");
        var settings = new AppSettings(); settings.Auth.CookiesFromBrowser = "chrome"; settings.Auth.CookiesFile = "saved-default.txt"; settings.Network.FragmentRetries = 4; settings.General.WriteAllThumbnails = true;
        settings.General.NoContinue = true; settings.Network.PlaylistReverse = true; settings.Network.PlaylistRandom = true;
        await adapter.DownloadAsync("https://media.example/video.mp4", new DownloadOptions { OutputDirectory = TestRoot, CookiesFile = cookieFile, CookiesFromBrowser = browser, AddMetadata = false, EmbedChapters = false, ConcurrentFragments = 3 }, settings, null,
            new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].CookieFile == cookieFile && requests[^1].BrowserCookies == browser, "Saved authentication overrode the job's explicit cookie choices.");
        Assert(requests[^1].ConcurrentFragments == 3 && requests[^1].FragmentRetries == 4, "The native request lost the chosen fragment concurrency or retry setting.");
        Assert(requests[^1].WriteAllThumbnails, "The native request lost the app's all-thumbnail setting.");
        Assert(requests[^1].NoContinue && requests[^1].PlaylistReverse && requests[^1].PlaylistRandom, "The native request lost restart or playlist ordering settings.");
        await adapter.DownloadAsync("https://media.example/video.mp4", new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false,
            ExtraArgs = "--continue --no-playlist-reverse --no-playlist-random" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(!requests[^1].NoContinue && !requests[^1].PlaylistReverse && !requests[^1].PlaylistRandom, "Job overrides did not take precedence over saved restart/ordering settings.");
        await adapter.GetVideoInfoAsync("https://media.example/video.mp4", default, new AnalysisOptions { CookiesFromBrowser = "none" });
        Assert(requests[^1].BrowserCookies is null, "Disabling browser import was not carried into the native request.");
    }
    private sealed class NativeCookieMappingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent("fixture media"u8.ToArray()) });
    }
    private static async Task NativeAvailability()
    {
        var protectedFormat = new YoutubeDl.Engine.NativeFormat { Id = "protected", Availability = YoutubeDl.Engine.FormatAvailability.DrmProtected, UnavailableReason = "Protected fixture media is unavailable." };
        var page = new YoutubeDl.Engine.Discovery.PageMetadata { Id = "fixture", Title = "Fixture", Extractor = "fixture",
            Thumbnails = [new() { Id = "small", Url = new("https://media.example/small.jpg"), Width = 120, Height = 90 }, new() { Id = "large", Url = new("https://media.example/large.jpg"), Width = 480, Height = 360 }],
            ReportedFormats = [new YoutubeDl.Engine.NativeFormat { Id = "ready", Url = new("https://media.example/file.mp4"), Height = 360 }, protectedFormat],
            Subtitles = [new() { Url = new("https://media.example/en.vtt"), Language = "en", Extension = "vtt" }, new() { Url = new("https://media.example/fr.vtt"), Language = "fr", Extension = "vtt", Automatic = true }] };
        NativeEngineAdapter Adapter(YoutubeDl.Engine.Discovery.PageMetadata metadata) => new(new YoutubeDl.Engine.NativeEngine([new NativePreviewProvider(metadata)], _ => new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler())));
        var info = await Adapter(page).GetVideoInfoAsync("https://media.example/watch", default);
        Assert(info.Formats!.Single().FormatId == "ready" && info.SubtitleLanguages.SequenceEqual(new[] { "en" }) && info.AutoCaptionLanguages.SequenceEqual(new[] { "fr" }),
            "Unavailable formats or missing caption classifications reached native preview options.");
        Assert(info.Thumbnails?.Count == 2 && info.BestThumbnailUrl == "https://media.example/large.jpg", "Native preview discarded its thumbnail inventory or preferred image.");
        try { await Adapter(page with { ReportedFormats = [protectedFormat] }).GetVideoInfoAsync("https://media.example/watch", default); throw new InvalidOperationException("A protected-only item appeared downloadable."); }
        catch (NativeEngineUnsupportedException ex) { Assert(ex.Message.Contains("Protected fixture"), "Native preview discarded the actual unavailability reason."); }
    }
    private sealed class NativePreviewProvider(YoutubeDl.Engine.Discovery.PageMetadata page) : YoutubeDl.Engine.Providers.IMediaProvider
    {
        public YoutubeDl.Engine.Providers.MediaProviderInfo Info { get; } = new("fixture", "Fixture metadata", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<YoutubeDl.Engine.Discovery.PageMetadata> ExtractAsync(YoutubeDl.Engine.NativeDownloadRequest request, YoutubeDl.Engine.Http.MediaHttp http, CancellationToken ct) => Task.FromResult(page);
    }
    private static async Task NativeMetadataArguments()
    {
        var output = Path.Combine(TestRoot, "metadata-args"); var sessions = 0;
        var page = new YoutubeDl.Engine.Discovery.PageMetadata { Id = "fixture", Title = "Fixture", Description = "Description", Extractor = "fixture",
            Media = [new() { Url = new("https://media.example/file.mp4"), Kind = YoutubeDl.Engine.Discovery.MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }] };
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine([new NativePreviewProvider(page)], _ => { sessions++; return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.General.ExtraArgs = "--simulate --write-info-json --write-description";
        var callbacks = new List<string>();
        var result = await adapter.DownloadAsync("https://media.example/watch", new DownloadOptions { OutputDirectory = output,
            ExtraArgs = "--no-simulate --skip-download --no-write-description -o \"exports/%(id)s.%(ext)s\"" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default, callbacks.Add);
        Assert(result.ExitCode == 0 && result.FinalFilePaths.Count == 0 && callbacks.Count == 0 && File.Exists(Path.Combine(output, "exports", "fixture.info.json")) &&
            !File.Exists(Path.Combine(output, "exports", "fixture.description")) && !Directory.EnumerateFiles(output, "*.mp4", SearchOption.AllDirectories).Any(),
            "Extra-argument precedence or standalone output behavior changed.");
        try { await adapter.DownloadAsync("https://media.example/watch", new DownloadOptions { OutputDirectory = output, ExtraArgs = "--exec before_dl:unwanted-command" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("An unsupported extra argument was accepted."); }
        catch (NativeEngineUnsupportedException) { Assert(sessions == 1, "Unsupported extras reached native extraction."); }
    }

    private static async Task NativeSubtitleSettings()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        { requests.Add(request); return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings();
        settings.Subtitles.WriteAllSubs = true; settings.Subtitles.SubFormat = "srt/vtt/best"; settings.Subtitles.ConvertSubs = "srt";
        await adapter.DownloadAsync("https://media.example/subtitle-settings.mp4", new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false,
            EmbedChapters = false, SubLangs = "en", ExtraArgs = "--simulate" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].WriteSubs && requests[^1].SubLanguages == "all" && requests[^1].SubFormat == "srt/vtt/best" && requests[^1].ConvertSubs == "srt",
            "Saved subtitle settings were not mapped into the native request.");
        await adapter.DownloadAsync("https://media.example/subtitle-settings.mp4", new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false,
            EmbedChapters = false, ExtraArgs = "--simulate --sub-langs fr --sub-format ass/best --convert-subs ass --no-write-subs --write-auto-subs" },
            settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(!requests[^1].WriteSubs && requests[^1].WriteAutoSubs && requests[^1].SubLanguages == "fr" && requests[^1].SubFormat == "ass/best" && requests[^1].ConvertSubs == "ass",
            "Per-job subtitle settings did not override saved all-language preferences.");
    }

    private static async Task NativePlaylistSelection()
    {
        var output = Path.Combine(TestRoot, "native-playlist-selection");
        var page = new YoutubeDl.Engine.Discovery.PageMetadata { Id = "list", Title = "Playlist", IsPlaylist = true,
            Entries = Enumerable.Range(1, 4).Select(index => new YoutubeDl.Engine.NativePlaylistEntry(index, index.ToString(), "Item " + index, new Uri("https://media.example/" + index + ".mp4"), null, null, true,
                new YoutubeDl.Engine.Discovery.PageMetadata { Id = index.ToString(), Title = "Item " + index, Media = [new() { Url = new("https://media.example/" + index + ".mp4"), Extension = "mp4", HasVideo = true, HasAudio = true }] })).ToArray() };
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine([new NativePreviewProvider(page)], _ => new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler())));
        var settings = new AppSettings(); settings.General.ExtraArgs = "-I 1,2"; settings.General.WriteInfoJson = true;
        var completed = new List<string>();
        var result = await adapter.DownloadAsync("https://media.example/list", new DownloadOptions { OutputDirectory = output, AddMetadata = false, EmbedChapters = false,
            PlaylistItems = "2:3", PlaylistStart = 2, PlaylistEnd = 3, OutputTemplate = "%(playlist_autonumber)02d-%(playlist_index)02d.%(ext)s",
            ExtraArgs = "-I=-1,1 --playlist-start 2 --playlist-end 2" }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default, completed.Add);
        Assert(result.ExitCode == 0 && result.FinalFilePaths.SequenceEqual(completed) && completed.Select(Path.GetFileName).SequenceEqual(new[] { "01-04.mp4", "02-01.mp4" }),
            "Typed/global/job precedence or selected-order filenames changed in the app adapter.");
        var empty = await adapter.DownloadAsync("https://media.example/list", new DownloadOptions { OutputDirectory = output, AddMetadata = false, EmbedChapters = false, ExtraArgs = "-I 0" },
            settings, null, new Progress<ProgressUpdate>(), _ => { }, default, _ => throw new InvalidOperationException("An empty selection emitted a file callback."));
        Assert(empty.ExitCode == 0 && empty.FinalFilePaths.Count == 0, "An empty native selection did not complete as a no-file job.");
    }

    private static async Task NativeNetworkSettings()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        { requests.Add(request); return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Network.SourceAddress = "127.0.0.2"; settings.Network.IpVersion = "4";
        settings.Network.NoCheckCertificate = true; settings.Network.UserAgent = "Saved/1";
        settings.Network.CustomHeaders = "X-Native: saved\r\nUser-Agent: Header/1\r\nReferer: https://saved.example/";
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/network.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].SourceAddress == "127.0.0.2" && requests[^1].IpVersion == 4 && requests[^1].NoCheckCertificate && requests[^1].UserAgent == "Header/1" && requests[^1].Referer == "https://saved.example/" && requests[^1].HttpHeaders["X-Native"] == "saved", "Saved native network settings were ignored.");
        options.ExtraArgs = "--simulate --source-address ::1 -6 --check-certificate --add-headers \"x-native: job:value\" --user-agent Job/2 --socket-timeout 12";
        await adapter.DownloadAsync("https://media.example/network.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].SourceAddress == "::1" && requests[^1].IpVersion == 6 && !requests[^1].NoCheckCertificate && requests[^1].UserAgent == "Job/2" && requests[^1].HttpHeaders["X-Native"] == "job:value" && requests[^1].TimeoutSeconds == 12, "Per-job network overrides did not take precedence.");
        Assert(settings.Network.SourceAddress == "127.0.0.2" && settings.Network.CustomHeaders.Contains("X-Native: saved"), "Network overrides mutated saved settings.");
    }

    private static async Task NativeSelectionSettings()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        { requests.Add(request); return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Selection.MatchTitle = "saved title"; settings.Selection.RejectTitle = "reject"; settings.Selection.MaxDownloads = 2;
        settings.General.IgnoreErrors = true; settings.General.AbortOnError = true;
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/selection.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MatchTitle == "saved title" && requests[^1].RejectTitle == "reject" && requests[^1].MaxDownloads == 2 && !requests[^1].IgnoreErrors, "Saved selection or abort settings were ignored.");
        options.ExtraArgs = "--simulate --match-title job --reject-title excluded --max-downloads 1 --ignore-errors";
        await adapter.DownloadAsync("https://media.example/selection.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MatchTitle == "job" && requests[^1].RejectTitle == "excluded" && requests[^1].MaxDownloads == 1 && requests[^1].IgnoreErrors, "Per-job selection overrides did not take precedence.");
        Assert(settings.Selection.MatchTitle == "saved title" && settings.Selection.MaxDownloads == 2 && settings.General.AbortOnError, "Selection overrides changed saved settings.");
    }

    private static async Task NativeRecodeSettings()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        { requests.Add(request); return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Formats.RecodeVideo = "webm"; settings.Formats.NoPostOverwrites = true;
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, Container = "auto", ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].RecodeVideo == "webm" && requests[^1].NoPostOverwrites, "Saved conversion preferences were ignored.");
        options.Container = "mkv";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].RecodeVideo == "" && requests[^1].Container == "mkv", "Saved recoding overrode the explicit container.");
        options.Container = "auto"; options.QualityPreset = "audio";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].AudioOnly && requests[^1].RecodeVideo == "", "Audio preset did not suppress video conversion.");
        options.QualityPreset = "best"; options.ExtraArgs = "--simulate --recode-video mp4 --post-overwrites";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].RecodeVideo == "mp4" && !requests[^1].NoPostOverwrites && settings.Formats.RecodeVideo == "webm", "Job overrides changed or ignored saved conversion settings.");
    }

    private static async Task NativeMergeSettings()
    {
        var requests = new List<YoutubeDl.Engine.NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new YoutubeDl.Engine.NativeEngine(httpFactory: request =>
        { requests.Add(request); return new YoutubeDl.Engine.Http.MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Formats.MergeOutputFormat = "webm/mkv"; settings.Formats.PreferFreeFormats = true; settings.Formats.RecodeVideo = "mp4";
        var options = new DownloadOptions { OutputDirectory = TestRoot, AddMetadata = false, EmbedChapters = false, Container = "auto", ExtraArgs = "--simulate" };
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MergeOutputFormat == "webm/mkv" && requests[^1].PreferFreeFormats && requests[^1].Container == "" && requests[^1].RecodeVideo == "mp4", "Saved merge preferences were ignored or became final remuxing.");
        options.Container = "mkv";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MergeOutputFormat == "mkv" && requests[^1].Container == "mkv" && requests[^1].RecodeVideo == "", "Explicit container did not override saved merge and recode settings.");
        options.Container = "auto"; options.QualityPreset = "audio";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MergeOutputFormat == "" && requests[^1].PreferFreeFormats, "Audio preset did not suppress the saved video merge preference.");
        options.QualityPreset = "best"; options.ExtraArgs = "--simulate --merge-output-format mp4/mkv --no-prefer-free-formats";
        await adapter.DownloadAsync("https://media.example/convert.mp4", options, settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
        Assert(requests[^1].MergeOutputFormat == "mp4/mkv" && !requests[^1].PreferFreeFormats && settings.Formats.MergeOutputFormat == "webm/mkv" && settings.Formats.PreferFreeFormats, "Job overrides ignored or changed saved preferences.");
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("State transition timed out.");
            await Task.Delay(5);
        }
    }

    private static Task Selectors()
    {
        foreach (var height in new[] { "144", "240", "1080", "4320", "1920" })
            Assert(ArgsBuilder.BuildFormatSelector(height, "", "video-audio", "any", 0).Contains("height<=" + height), height);
        var selector = ArgsBuilder.BuildFormatSelector("1080", "", "video-audio", "vp9", 30);
        Assert(selector.Contains("height<=1080") && selector.Contains("fps<=30"), selector);
        var regex = selector.Split("vcodec~='")[1].Split('\'')[0];
        foreach (var codec in new[] { "vp9", "vp9.2", "vp09.00.10.08" })
            Assert(System.Text.RegularExpressions.Regex.IsMatch(codec, regex), codec);
        Assert(!System.Text.RegularExpressions.Regex.IsMatch("vp08.00", regex), "VP8 incorrectly accepted");
        return Task.CompletedTask;
    }

    private static Task AnalysisArguments()
    {
        var settings = new AppSettings();
        settings.General.ExtraArgs = "--no-simulate --exec calc --print-to-file title out.txt --config-locations other.conf --extractor-args youtube:player_client=web --proxy=http://localhost:8080";
        foreach (var args in new[]
        {
            ArgsBuilder.BuildInfoArgs(settings, "https://example.com/video"),
            ArgsBuilder.BuildListFormatsArgs(settings, "https://example.com/video")
        })
        {
            Assert(args.Contains("--simulate") && args.Contains("--ignore-config") && args.Contains("--no-exec"), "Missing analysis contract");
            Assert(!args.Any(x => x is "--no-simulate" or "--exec" or "--print-to-file" or "--config-locations"), "Side effect survived filtering");
            Assert(args.Contains("youtube:player_client=web") && args.Contains("--proxy=http://localhost:8080"), "Connection/extraction override lost");
        }
        return Task.CompletedTask;
    }

    private static Task DownloadArguments()
    {
        var settings = new AppSettings();
        settings.Network.FragmentRetries = 0;
        var args = ArgsBuilder.BuildDownloadArgs(settings, new DownloadOptions(), "https://example.com/video", null);
        Assert(args[args.IndexOf("--fragment-retries") + 1] == "0", "Zero retry option omitted");
        Assert(args.Any(x => x.Contains("progress.total_bytes,progress.total_bytes_estimate")), "Estimated-size alternative is wrong");
        return Task.CompletedTask;
    }

    private static Task PlaylistProgress()
    {
        var item = new DownloadItemViewModel(null!, "https://example.com/video", new(), new(), null, null);
        item.ApplyProgress(new() { IsPostProcessing = true });
        Assert(item.Status == DownloadStatus.Processing, "Missing processing transition");
        item.ApplyProgress(new() { Status = "downloading", DownloadedBytes = 10, TotalBytes = 100 });
        Assert(item.Status == DownloadStatus.Downloading && item.ShowPause && item.Progress == 10, "Next entry did not restore download state");
        item.ApplyProgress(new() { FinalFilePath = "file.mp4" });
        Assert(item.Status == DownloadStatus.Downloading, "Final path changed the phase");
        item.MarkCompleted([]);
        item.ApplyProgress(new() { IsPostProcessing = true });
        Assert(item.Status == DownloadStatus.Completed, "Late progress overwrote terminal state");
        return Task.CompletedTask;
    }

    private static async Task QueueCancellation()
    {
        await using var f = new Fixture();
        var errors = new List<string>();
        f.Queue.QueueError += errors.Add;
        f.Engine.Download = async (ct, _) => { await Task.Delay(20, ct); return new(); };
        for (var index = 0; index < 128; index++)
        {
            var item = f.Enqueue();
            f.Queue.Pause(item);
            f.Queue.StartOrResume(item);
            f.Queue.Cancel(item);
            f.Queue.Remove(item);
        }
        var final = f.Enqueue();
        await Until(() => final.Status == DownloadStatus.Completed);
        await f.Queue.ShutdownAsync();
        Assert(errors.Count == 0, string.Join("; ", errors));
        Assert(final.Cts is null, "Finished run still attached");
    }

    private static async Task PartialFailure()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        f.Engine.Download = (_, file) => { file!(path); throw new InvalidOperationException("second entry failed"); };
        var item = f.Enqueue();
        await Until(() => item.Status == DownloadStatus.Failed);
        await f.Queue.ShutdownAsync();
        await f.History.FlushAsync();
        using var reloaded = new HistoryService(f.Directory);
        reloaded.Load();
        Assert(reloaded.Entries.Single().FilePath == path, "Finalized file was not persisted");
        Assert(item.OutputPaths.Contains(path), "Partial output missing from item");
    }

    private static async Task PartialCancellation()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Download = async (ct, file) =>
        {
            file!(path);
            reported.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new();
        };
        var item = f.Enqueue();
        await reported.Task;
        f.Queue.Cancel(item);
        await f.Queue.ShutdownAsync();
        Assert(item.Status == DownloadStatus.Canceled && f.History.Entries.Single().FilePath == path, "Cancellation erased a completed file");
    }

    private static async Task ShutdownCompletion()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<DownloadRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Download = (_, file) => { file!(path); reported.SetResult(); return exited.Task; };
        var item = f.Enqueue();
        await reported.Task;
        var shutdown = f.Queue.ShutdownAsync();
        exited.SetResult(new() { FinalFilePaths = [path] });
        await shutdown;
        await f.History.FlushAsync();
        Assert(item.Status == DownloadStatus.Completed, "Late cancellation replaced successful exit");
        Assert(f.History.Entries.Single().FilePath == path, "Shutdown did not drain file commits");
    }

    private static async Task PreviewReadiness()
    {
        await using var f = new Fixture();
        f.Settings.Data.Formats.DefaultQuality = "1080";
        var thumbnail = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = f.ViewModel((_, _) => thumbnail.Task);
        vm.SelectedQualityKey = "worst";
        vm.UrlInput = "https://example.com/video";
        var fetch = vm.FetchCommand.ExecuteAsync(null);
        await Until(() => vm.HasPreview);
        Assert(vm.SelectedQualityKey == "1080", "Published preview still had previous options");
        vm.SelectedQualityKey = "720";
        thumbnail.SetResult(null);
        await fetch;
        Assert(vm.SelectedQualityKey == "720", "Thumbnail continuation overwrote selection");
    }

    private static async Task StalePreview()
    {
        await using var f = new Fixture();
        var metadata = new TaskCompletionSource<VideoInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Info = (_, _, _) => metadata.Task;
        using var vm = f.ViewModel();
        vm.UrlInput = "https://example.com/video";
        var fetch = vm.FetchCommand.ExecuteAsync(null);
        vm.ClearInputCommand.Execute(null);
        metadata.SetResult(new() { Title = "Late result" });
        await fetch;
        Assert(!vm.HasPreview && vm.UrlInput == "", "Canceled metadata resurrected the preview");
    }

    private static async Task CustomSelection()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        vm.CustomFormat = "137+ba/b";
        vm.SelectedQualityKey = "custom";
        vm.AddDownloadMode = "video-only";
        Assert(vm.SelectedQualityKey == "best" && vm.CustomFormat == "", "Type left old stream selection active");
        vm.CustomFormat = "137";
        vm.SelectedQualityKey = "custom";
        vm.AddMaxFps = "30";
        Assert(vm.SelectedQualityKey != "custom", "FPS constraint silently ignored");
    }

    private static async Task Authentication()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        var draft = f.Settings.CreateDraft();
        draft.Auth.CookiesFromBrowser = "firefox";
        f.Settings.Save(draft);
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "firefox" && vm.AnalysisCookiesFromBrowser == "firefox", "Saved cookies were stale");
        vm.UseSavedAnalysisAuthentication = false;
        vm.AnalysisCookiesFromBrowser = "edge";
        draft.Auth.CookiesFromBrowser = "chrome";
        f.Settings.Save(draft);
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "edge", "Save overwrote explicit authentication override");
        vm.UseSavedAnalysisAuthentication = true;
        Assert(vm.BuildAnalysisOptions().CookiesFromBrowser == "chrome", "Could not return to saved defaults");
    }

    private static Task BackupFailure()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "settings.json.bak"));
        var settings = new SettingsService(dir);
        var draft = settings.CreateDraft();
        draft.General.MaxConcurrentDownloads = 7;
        settings.Save(draft);
        Assert(settings.Data.General.MaxConcurrentDownloads == 7 && settings.BackupWarning is not null, "Primary commit was reported as an overall failure");
        var reloaded = new SettingsService(dir);
        reloaded.Load();
        Assert(reloaded.Data.General.MaxConcurrentDownloads == settings.Data.General.MaxConcurrentDownloads, "Memory and disk disagree");
        return Task.CompletedTask;
    }

    private static Task FfmpegRollback()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        var managed = Path.Combine(dir, "managed");
        var staging = Path.Combine(dir, "staging");
        var backup = Path.Combine(dir, "backup");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(managed, "old.txt"), "old");
        File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
        var manager = new FfmpegManager(new SettingsService(dir), managed);
        manager.CommitManagedInstall(staging, backup);
        Assert(File.Exists(Path.Combine(backup, "old.txt")), "Backup deleted before verification");
        manager.RestoreManagedInstallAfterVerificationFailure(backup);
        Assert(File.Exists(Path.Combine(managed, "old.txt")) && !File.Exists(Path.Combine(managed, "new.txt")), "Rollback failed");
        return Task.CompletedTask;
    }

    private static async Task BatchAdmission()
    {
        await using var f = new Fixture();
        using var vm = f.ViewModel();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { vm.EnqueueBatch(["https://example.com/video"], new(), new(), cts.Token); throw new Exception("Canceled batch accepted"); }
        catch (OperationCanceledException) { }
        try { vm.EnqueueBatch(Enumerable.Repeat("https://example.com/video", 10_001).ToList(), new(), new(), default); throw new Exception("Oversized batch accepted"); }
        catch (InvalidOperationException) { }
        Assert(f.Queue.Items.Count == 0, "Batch admission was partial");
    }

    private static async Task EngineInstallation()
    {
        await using var f = new Fixture();
        f.Engine.IsInstalled = false;
        using var vm = f.ViewModel();
        var count = 0;
        vm.EngineStateChanged += () => count++;
        await vm.InstallEngineCommand.ExecuteAsync(null);
        Assert(vm.IsEngineInstalled && count == 1, "Install did not publish readiness");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        public SettingsService Settings { get; }
        public HistoryService History { get; }
        public FakeEngine Engine { get; } = new();
        public DownloadQueueService Queue { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Settings = new(Directory);
            History = new(Directory);
            Queue = new(Engine, new FakeFfmpeg(), Settings, History);
        }
        public DownloadItemViewModel Enqueue() => Queue.Enqueue("https://example.com/video", new(), null);
        public QueueViewModel ViewModel(Func<string?, CancellationToken, Task<ImageSource?>>? thumbnail = null) =>
            new(Queue, Settings, Engine, thumbnail ?? ((_, _) => Task.FromResult<ImageSource?>(null)));
        public string MediaFile()
        {
            var path = Path.Combine(Directory, "complete.mp4");
            File.WriteAllText(path, "completed media");
            return path;
        }
        public async ValueTask DisposeAsync()
        {
            await Queue.ShutdownAsync();
            await History.FlushAsync();
            History.Dispose();
        }
    }

    private sealed class FakeFfmpeg : IFfmpegLocator
    {
        public Task<string?> LocateAsync(AppSettings settingsSnapshot, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeEngine : IYtDlpEngine
    {
        public bool IsInstalled { get; set; } = true;
        public Func<CancellationToken, Action<string>?, Task<DownloadRunResult>> Download { get; set; } = (_, _) => Task.FromResult(new DownloadRunResult());
        public Func<string, CancellationToken, AnalysisOptions?, Task<VideoInfo>> Info { get; set; } = (_, _, _) => Task.FromResult(new VideoInfo { Title = "Video" });
        public Func<string, CancellationToken, AnalysisOptions?, bool, Task<IReadOnlyList<FormatInfo>>> Formats { get; set; } = (_, _, _, _) => Task.FromResult<IReadOnlyList<FormatInfo>>([]);
        public Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct, AnalysisOptions? options = null) => Info(url, ct, options);
        public Task<IReadOnlyList<FormatInfo>> ListFormatsAsync(string url, CancellationToken ct, AnalysisOptions? options = null, bool isPlaylist = false) => Formats(url, ct, options, isPlaylist);
        public Task EnsureInstalledAsync(IProgress<(double Percent, string Message)>? progress, CancellationToken ct) { IsInstalled = true; return Task.CompletedTask; }
        public Task<DownloadRunResult> DownloadAsync(string url, DownloadOptions options, AppSettings settingsSnapshot, string? ffmpegLocation,
            IProgress<ProgressUpdate> progress, Action<string> log, CancellationToken ct, Action<string>? onFileCompleted = null) => Download(ct, onFileCompleted);
    }
}





