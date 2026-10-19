using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Providers.YouTube;

if (args.Length == 3 && args[0] == "--stress-pipe-parent") return await StressRaceChecks.PipeParent(args[1]);
if (args.Length == 3 && args[0] == "--stress-pipe-child") return await StressRaceChecks.PipeChild(args[1]);
if (args.Length == 1 && args[0] == "--stress-utf8-helper") { await StressRaceChecks.Utf8Helper(); return 0; }

if (args.Length >= 4 && args[0] == "--exec-helper") return await ExecSponsorChecks.Helper(args[1], args[2], args.Skip(3).ToArray());
if (args.Length == 3 && args[0] == "--exec-crash-worker") return await ExecSponsorChecks.Worker(args[1], args[2]);
if (args.Length == 3 && args[0] == "--archive-worker") return await DownloadArchiveChecks.Worker(args[1], args[2]);
if (args.Length == 3 && args[0] == "--recovery-worker") return await FfmpegChecks.AdaptiveRecoveryWorker(args[1],args[2]);
if(args.Length==4&&args[0]=="--durable-live-worker")return await FfmpegChecks.DurableLiveWorker(args[1],args[2],args[3]);
if(args.Length==3&&args[0]=="--publication-worker")return await PublicationRecoveryChecks.Worker(args[1],args[2]);
if(args.Length==4&&args[0]=="--write-cleanup-worker")return await WriteCleanupChecks.Worker(args[1],args[2],args[3]);
if(args.Length==3&&args[0]=="--changed-job-worker")return await ChangedJobChecks.Worker(args[1],args[2]);
if(args.Length==3&&args[0]=="--inventory-retention-worker")return await InventoryRetentionChecks.Worker(args[1],args[2]);
if(args.Length==4&&args[0]=="--adaptation-worker")return await FfmpegChecks.AdaptationWorker(args[1],args[2],args[3]);
if(args.Length==4&&args[0]=="--wpf-ladder-worker")return await FfmpegChecks.WpfLadderWorker(args[1],args[2],args[3]);
if(args.Length==5&&args[0]=="--legacy-adaptation-worker")return await FfmpegChecks.LegacyAdaptationWorker(args[1],args[2],args[3],args[4]);
if(args.Length==3&&args[0]=="--inventory-worker")return await InventoryRecoveryChecks.Worker(args[1],args[2]);
if(args.Length==4&&args[0]=="--inventory-media-worker")return await FfmpegChecks.InventoryRecoveryMediaWorker(args[1],args[2],args[3]);
if(args.Length==3&&args[0]=="--changed-job-retry-worker")return await ChangedJobChecks.RetryWorker(args[1],args[2]);
if(args.Length==4&&args[0]=="--changed-media-worker")return await FfmpegChecks.ChangedMediaWorker(args[1],args[2],args[3]);
if(args.Length==4&&args[0]=="--changed-media-retry-worker")return await FfmpegChecks.ChangedMediaWorker(args[1],args[2],args[3],true);
if(args.Length==4&&args[0]=="--publication-media-worker")return await FfmpegChecks.PublicationMediaWorker(args[1],args[2],args[3]);
if(args.Length==2&&args[0]=="--lifetime-parent")return await FfmpegChecks.LifetimeParentWorker(args[1]);
if(args.Length==2&&args[0]=="--lifetime-helper")return await FfmpegChecks.LifetimeHelper(args[1]);
if (args.LastOrDefault() == "pipe:1" && Environment.GetEnvironmentVariable(SubtitleFormatChecks.WorkerVariable) is { } captionWorker)
    return await SubtitleFormatChecks.Worker(captionWorker);

var workerFile = Environment.GetEnvironmentVariable("YOUTUBEDL_NATIVE_TEST_WORKER_FILE");
if ((args.Contains("-version") || args.Contains("-abort_on")) && workerFile is not null)
{
    await File.WriteAllTextAsync(workerFile + ".tmp", Environment.ProcessId.ToString());
    File.Move(workerFile + ".tmp", workerFile);
    await Task.Delay(TimeSpan.FromMinutes(1));
    return 0;
}

if (args.Length > 0 && args[0] is "--inspect" or "--inspect-file")
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        NativeMediaInfo info;
        if (args[0] == "--inspect" && args.Length == 2)
            info = await new NativeEngine().InspectAsync(new NativeDownloadRequest { Url = new Uri(args[1]), OutputDirectory = Path.GetTempPath() }, timeout.Token);
        else if (args[0] == "--inspect-file" && args.Length == 3)
        {
            var address = YouTubeAddress.Parse(new Uri(args[2]));
            var page = YouTubePlayerParser.ParsePage(await File.ReadAllTextAsync(args[1], timeout.Token), address.VideoId!);
            info = new NativeMediaInfo { Id = page.Id!, Title = page.Title, Extractor = page.Extractor, DurationSeconds = page.DurationSeconds,
                Formats = page.ReportedFormats, Subtitles = page.Subtitles, Chapters = page.Chapters, Warnings = page.Warnings, PlayerScriptUrl = page.PlayerScriptUrl };
        }
        else throw new ArgumentException("Use --inspect URL or --inspect-file HTML_PATH URL.");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            info.Id, info.Title, info.Extractor, info.DurationSeconds, FormatCount = info.Formats.Count,
            ReadyFormats = info.Formats.Count(format => format.Availability == FormatAvailability.Ready),
            ChallengedFormats = info.Formats.Count(format => format.Availability == FormatAvailability.PlayerChallengeRequired),
            AdaptiveTransportFormats = info.Formats.Count(format => format.Availability == FormatAvailability.AdaptiveTransportRequired),
            CaptionTracks = info.Subtitles.Count, ChapterCount = info.Chapters.Count, info.IsPlaylist, PlaylistEntries = info.Entries.Count,
            PlayerScriptFound = info.PlayerScriptUrl is not null, info.Warnings
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

var failures = 0;
var checks = 0;
Check("Page extractor finds video, Open Graph, and HLS links", PageExtractor);
Check("Quality selector honors height and worst", Quality);
Check("HLS parser separates master variants from segments", Hls);
Check("Complex sites require dedicated native providers", Routing);
Check("Rate limit accepts the classic suffixes", Rate);
Check("DASH parser reads progressive representations", Dash);
Check("Page capture reads script URLs and refuses DRM", Capture);
var nativeTests = EngineFoundationChecks.All.Concat(YouTubeChecks.All).Concat(PlayerRuntimeChecks.All).Concat(AttestationChecks.All).Concat(WaaChecks.All).Concat(FfmpegChecks.ProcessChecks).Concat(ProtocolChecks.All).Concat(SabrChecks.All).Concat(SabrStateChecks.All).Concat(HlsTransferChecks.All).Concat(HlsCaptionChecks.All).Concat(SabrHttpChecks.All).Concat(DashTransferChecks.All).Concat(DashIndexChecks.All).Concat(DashCaptionChecks.All).Concat(VimeoChecks.All).Concat(ChapterChecks.All).Concat(ArchiveChecks.All).Concat(PeerTubeChecks.All).Concat(PlaylistChecks.All).Concat(CaptionChecks.All).Concat(PublicationChecks.All).Concat(OutputTemplateChecks.All).Concat(FormatSelectorChecks.All).Concat(DailymotionChecks.All).Concat(HttpSessionChecks.All).Concat(HostingChecks.All).Concat(BrightcoveChecks.All).Concat(BandcampChecks.All).Concat(KalturaChecks.All).Concat(DownloadArchiveChecks.All);
var ffmpegArgument = Array.IndexOf(args, "--ffmpeg");
var legacyNativeArgument=Array.IndexOf(args,"--legacy-native-tests");
if(legacyNativeArgument>=0){if(legacyNativeArgument+1>=args.Length||!File.Exists(args[legacyNativeArgument+1]))throw new ArgumentException("Provide the preserved legacy native test assembly.");FfmpegChecks.LegacyNativeAssembly=Path.GetFullPath(args[legacyNativeArgument+1]);}
nativeTests = nativeTests.Concat(SabrNativeChecks.All).Concat(PornhubRenewalChecks.All);
nativeTests = nativeTests.Concat(FfmpegChecks.CoverChecks);
nativeTests = nativeTests.Concat(QualityBootstrapChecks.All.Concat(QualitySiteChecks.All).Concat(QualitySessionChecks.All).Concat(QualityAudioChecks.All.Concat(EncodedPlayerChecks.All))).Concat(PracticalYouTubeChecks.All).Concat(PracticalPlayerChecks.All).Concat(PracticalFacebookChecks.All).Concat(AudioCatalogChecks.All).Concat(ExtractionDelayChecks.All).Concat(PublicBroadcastChecks.All).Concat(GlobalBroadcastChecks.All).Concat(CatalogSessionChecks.All).Concat(RadioCompletionChecks.All).Concat(OrfBroadcastChecks.All).Concat(ProviderFamiliesChecks.All);
nativeTests = nativeTests.Concat(RtveChecks.All);
nativeTests = nativeTests.Concat(ExternalDownloaderChecks.All);
nativeTests = nativeTests.Concat(ExecSponsorChecks.All);
nativeTests = nativeTests.Concat(ProcessingOptionsChecks.All).Concat(WhypChecks.All);
nativeTests = nativeTests.Concat(MusicFamilyChecks.All).Concat(ProviderCacheChecks.All).Concat(SoundCloudChecks.All).Concat(SoundCloudInventoryChecks.All);
nativeTests = nativeTests.Concat(FeedChecks.All);
nativeTests = nativeTests.Concat(ApplePodcastsChecks.All);
nativeTests = nativeTests.Concat(TwitchChecks.All);
nativeTests = nativeTests.Concat(FfmpegChecks.AudioChecks);
nativeTests = nativeTests.Concat(TikTokChecks.All);
nativeTests = nativeTests.Concat(PlaybackHeaderChecks.All);
nativeTests = nativeTests.Concat(TedChecks.All).Concat(FfmpegChecks.HlsPeriodChecks);
nativeTests = nativeTests.Concat(StreamableChecks.All);
nativeTests = nativeTests.Concat(VidyardChecks.All);
nativeTests = nativeTests.Concat(CookieImportChecks.All);
nativeTests = nativeTests.Concat(ParallelFragmentChecks.All);
nativeTests = nativeTests.Concat(MetadataOnlyChecks.All);
nativeTests = nativeTests.Concat(ThumbnailChecks.All);
nativeTests = nativeTests.Concat(CloudflareStreamChecks.All);
nativeTests = nativeTests.Concat(RestartChecks.All);
nativeTests = nativeTests.Concat(SubtitleFormatChecks.All);
nativeTests = nativeTests.Concat(YouTubeChannelChecks.All).Concat(CoreInventoryChecks.All);
nativeTests = nativeTests.Concat(RedditChecks.All);
nativeTests = nativeTests.Concat(TtmlChecks.All);
nativeTests = nativeTests.Concat(Mp4TtmlChecks.All);
nativeTests = nativeTests.Concat(OdyseeChecks.All);
nativeTests = nativeTests.Concat(PlaylistSyntaxChecks.All);
nativeTests = nativeTests.Concat(BilibiliChecks.All);
nativeTests = nativeTests.Concat(BilibiliSequenceChecks.All);
nativeTests = nativeTests.Concat(RumbleChecks.All);
nativeTests = nativeTests.Concat(RumbleCollectionChecks.All);
nativeTests = nativeTests.Concat(PlaylistSessionChecks.All);
nativeTests = nativeTests.Concat(StructuredMediaChecks.All).Concat(SimpleDownloadChecks.All);
nativeTests = nativeTests.Concat(NfbChecks.All);
nativeTests = nativeTests.Concat(ImgurChecks.All);
nativeTests = nativeTests.Concat(BlueskyChecks.All);
nativeTests = nativeTests.Concat(NetworkSettingsChecks.All);
nativeTests = nativeTests.Concat(DownloadSelectionChecks.All);
nativeTests = nativeTests.Concat(ObservedYouTubeChecks.All);
nativeTests = nativeTests.Concat(YouTubePlaybackSourceChecks.All);
nativeTests = nativeTests.Concat(RecodeChecks.All);
nativeTests = nativeTests.Concat(MergeFormatChecks.All);
nativeTests = nativeTests.Concat(PanoptoChecks.All);
nativeTests = nativeTests.Concat(PanoptoCollectionChecks.All);
nativeTests = nativeTests.Concat(LoomChecks.All).Concat(ScopedMediaQueryChecks.All);
nativeTests = nativeTests.Concat(TwitterChecks.All).Concat(FlickrChecks.All).Concat(PinterestChecks.All).Concat(PinterestBoardChecks.All).Concat(PlaylistSnapshotChecks.All).Concat(PinterestStoryChecks.All).Concat(NestedPlaylistChecks.All).Concat(TumblrChecks.All);
nativeTests = nativeTests.Concat(TransportContinuityChecks.All);
nativeTests = nativeTests.Concat(MastodonChecks.All);
nativeTests = nativeTests.Concat(MicrosoftLearnChecks.All);
nativeTests = nativeTests.Concat(MicrosoftEmbedChecks.All);
nativeTests = nativeTests.Concat(PbsChecks.All);
nativeTests = nativeTests.Concat(PbsKidsChecks.All);
nativeTests = nativeTests.Concat(CoubChecks.All);
nativeTests = nativeTests.Concat(MedalChecks.All).Concat(FlightChecks.All);
nativeTests = nativeTests.Concat(MixcloudChecks.All);
nativeTests = nativeTests.Concat(IHeartChecks.All);
nativeTests = nativeTests.Concat(MetadataSelectionChecks.All);
nativeTests = nativeTests.Concat(OmnyChecks.All);
nativeTests = nativeTests.Concat(FileSizeChecks.All).Concat(DownloadDelayChecks.All).Concat(JamendoChecks.All).Concat(NewgroundsChecks.All).Concat(FileTimeChecks.All).Concat(AudiomackChecks.All).Concat(NoPartChecks.All).Concat(DownloadBufferChecks.All).Concat(HttpChunkChecks.All).Concat(RangeChunkChecks.All).Concat(BandlabChecks.All).Concat(HearThisChecks.All).Concat(ReverbNationChecks.All).Concat(MetadataParsingChecks.All).Concat(VocarooChecks.All).Concat(ClypChecks.All).Concat(BatchedArgumentChecks.All).Concat(AuthenticationBatchChecks.All).Concat(LiveBatchChecks.All).Concat(AdaptiveLiveChecks.All).Concat(DashContinuityChecks.All).Concat(LiveCaptionClippingChecks.All).Concat(AccountRenewalChecks.All);
nativeTests = nativeTests.Concat(AdaptiveAccountChecks.All);
nativeTests = nativeTests.Concat(SharedHostingChecks.All);
nativeTests = nativeTests.Concat(ConsumerDashChecks.All);
nativeTests = nativeTests.Concat(HostingCollectionChecks.All);
nativeTests = nativeTests.Concat(PlaylistCaptionChecks.All);
nativeTests = nativeTests.Concat(MixedCollectionChecks.All);
nativeTests = nativeTests.Concat(AdaptiveRecoveryChecks.All);
nativeTests = nativeTests.Concat(LiveSabrRecoveryChecks.All);
nativeTests = nativeTests.Concat(WindowPrefixChecks.All);
nativeTests = nativeTests.Concat(DurableLiveChecks.All);
nativeTests = nativeTests.Concat(PublicationRecoveryChecks.All).Concat(BroaderSessionChecks.All);
nativeTests = nativeTests.Concat(WriteCleanupChecks.All).Concat(ProviderLifetimeChecks.All);
nativeTests = nativeTests.Concat(ChangedJobChecks.All).Concat(ChangedSessionChecks.All);
nativeTests = nativeTests.Concat(NoOverwriteBatchChecks.All);
nativeTests = nativeTests.Concat(AdaptiveNoOverwriteChecks.All);
nativeTests = nativeTests.Concat(LiveInventoryChecks.All);
nativeTests = nativeTests.Concat(InventoryRecoveryChecks.All);
nativeTests = nativeTests.Concat(InventoryRetentionChecks.All);
nativeTests = nativeTests.Concat(LivePeriodSessionChecks.All);
nativeTests = nativeTests.Concat(RepresentationContractChecks.All);
nativeTests = nativeTests.Concat(AdaptationRecoveryChecks.All);
nativeTests = nativeTests.Concat(SelectionMigrationChecks.All);
nativeTests = nativeTests.Concat(WpfLadderChecks.All);
nativeTests = nativeTests.Concat(ColorAudioChecks.All).Concat(HdrGamutChecks.All).Concat(HdrMetadataChecks.All).Concat(HdrSpatialChecks.All).Concat(HdrTransformChecks.All).Concat(HdrReconstructChecks.All).Concat(HdrDomainChecks.All).Concat(HdrSelectorChecks.All).Concat(RecordingReceiptChecks.All);
nativeTests = nativeTests.Concat(AudiusChecks.All);
nativeTests = nativeTests.Concat(PodomaticFeedChecks.All);
nativeTests = nativeTests.Concat(PodomaticChecks.All);
nativeTests = nativeTests.Concat(MegaphoneChecks.All);
nativeTests = nativeTests.Concat(LibsynChecks.All);
nativeTests = nativeTests.Concat(SimplecastChecks.All);
nativeTests = nativeTests.Concat(AudioBoomChecks.All);
nativeTests = nativeTests.Concat(SpreakerChecks.All);
nativeTests = nativeTests.Concat(AcastChecks.All);
nativeTests = nativeTests.Concat(MovieAncillaryChecks.All);
nativeTests = nativeTests.Concat(MovieChapterChecks.All);
nativeTests = nativeTests.Concat(WikimediaChecks.All);
nativeTests = nativeTests.Concat(YouTubeCookieChecks.All);
nativeTests = nativeTests.Concat(SubtitleOverlapChecks.All);
nativeTests = nativeTests.Concat(NfbSeriesChecks.All);
nativeTests = nativeTests.Concat(StressRaceChecks.All);
if (ffmpegArgument >= 0)
{
    if (ffmpegArgument + 1 >= args.Length) throw new ArgumentException("Provide a path after --ffmpeg.");
    nativeTests = nativeTests.Concat(FfmpegChecks.MediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.MixedCollectionMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.AdaptiveRecoveryMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.LiveSabrRecoveryMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.WindowPrefixMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.DurableLiveMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.PublicationSessionMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.WriteCleanupMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.ChangedJobMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.NoOverwriteMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.AdaptiveNoOverwriteMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.LiveInventoryMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.InventoryRecoveryMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.InventoryRetentionMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.LivePeriodSessionMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.RepresentationContractMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.AdaptationRecoveryMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.SelectionMigrationMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.WpfLadderMediaChecks(args[ffmpegArgument + 1]));
    nativeTests = nativeTests.Concat(FfmpegChecks.ColorAudioMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrGamutMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrMetadataMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrSpatialMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrTransformMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrReconstructMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.HdrSelectorMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.CoreInventoryMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.AudioInventoryMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.ProviderCacheMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.MusicFamilyMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.ProcessingOptionsMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.WhypMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.PracticalPlayerMediaChecks(args[ffmpegArgument + 1]).Concat(FfmpegChecks.EncodedPlayerMediaChecks(args[ffmpegArgument + 1]).Concat(FfmpegChecks.PornhubRenewalMediaChecks(args[ffmpegArgument + 1]).Concat(FfmpegChecks.SimpleDownloadMediaChecks(args[ffmpegArgument + 1]))))).Concat(FfmpegChecks.PracticalFacebookMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.AudioCatalogMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.PublicBroadcastMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.GlobalBroadcastMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.CatalogSessionMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.RadioCompletionMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.OrfBroadcastMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.ProviderFamiliesMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.RtveMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.ExternalDownloaderMediaChecks(args[ffmpegArgument + 1])).Concat(FfmpegChecks.ExecSponsorMediaChecks(args[ffmpegArgument + 1]));
}
var filterArgument = Array.IndexOf(args, "--filter");
if (filterArgument >= 0)
{
    if (filterArgument + 1 >= args.Length || string.IsNullOrWhiteSpace(args[filterArgument + 1])) throw new ArgumentException("Provide a check-name substring after --filter.");
    nativeTests = nativeTests.Where(test => test.Name.Contains(args[filterArgument + 1], StringComparison.OrdinalIgnoreCase)).ToArray();
    if (!nativeTests.Any()) throw new ArgumentException("No native checks match the requested filter.");
}
var repeatArgument = Array.IndexOf(args, "--repeat");
var repetitions = 1;
if (repeatArgument >= 0 && (repeatArgument + 1 >= args.Length || !int.TryParse(args[repeatArgument + 1], out repetitions) || repetitions is < 1 or > 1000))
    throw new ArgumentException("Provide a repeat count between 1 and 1000.");
var selectedTests = nativeTests.ToArray();
Console.WriteLine($"Running {selectedTests.Length * repetitions} selected native checks.");
foreach (var (name, test) in selectedTests.SelectMany(test => Enumerable.Repeat(test, repetitions)))
{
    checks++;
    try { await test().WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine(failures == 0 ? $"All {checks} native engine checks passed." : failures + " check(s) failed.");
return failures == 0 ? 0 : 1;

void Check(string name, Action test)
{
    checks++;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}

void PageExtractor()
{
    const string html = """
        <html><head>
          <title>Sample show</title>
          <meta property="og:video" content="https://cdn.example/show.mp4" />
          <meta property="og:title" content="Sample show" />
        </head><body>
          <video><source src="/relative/trailer.webm"></video>
          <a href="https://cdn.example/live/index.m3u8">stream</a>
        </body></html>
        """;
    var page = new Uri("https://videos.example/watch/1");
    var media = PageMediaExtractor.Extract(html, page);
    Assert(media.Any(item => item.Url.AbsoluteUri == "https://cdn.example/show.mp4"), "mp4 missing");
    Assert(media.Any(item => item.Url.AbsoluteUri.EndsWith("/relative/trailer.webm")), "relative source missing");
    Assert(media.Any(item => item.Kind == MediaKind.Hls), "HLS missing");
    Assert(PageMetadataReader.Read(html, page).Title == "Sample show", "title missing");
}

void Quality()
{
    var candidates = new[]
    {
        Candidate(720, "https://cdn.example/720.mp4"),
        Candidate(1080, "https://cdn.example/1080.mp4"),
        Candidate(2160, "https://cdn.example/2160.mp4")
    };
    Assert(QualitySelector.Select(candidates, QualityPreference.Best, null, false).Height == 2160, "best");
    Assert(QualitySelector.Select(candidates, QualityPreference.Worst, null, false).Height == 720, "worst");
    Assert(QualitySelector.Select(candidates, QualityPreference.MaximumHeight, 1080, false).Height == 1080, "cap");
}

void Hls()
{
    const string master = """
        #EXTM3U
        #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
        low.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720
        high.m3u8
        """;
    var parsed = HlsPlaylistParser.Parse(master, new Uri("https://cdn.example/master.m3u8"));
    Assert(parsed.IsMaster && parsed.Variants.Count == 2, "variants");
    Assert(parsed.Variants[1].Height == 720, "height");
    var media = HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:6,\nhttps://cdn.example/a.ts\n", new Uri("https://cdn.example/media.m3u8"));
    Assert(!media.IsMaster && media.Segments.Count == 1, "segments");
}

void Routing()
{
    Assert(NativeEngine.CanHandle(new Uri("https://videos.example/watch/1")), "generic page");
    Assert(NativeEngine.CanHandle(new Uri("https://cdn.example/file.mp4")), "direct file");
    Assert(NativeEngine.CanHandle(new Uri("https://www.youtube.com/watch?v=abcdefghijk")), "youtube provider");
    Assert(NativeEngine.CanHandle(new Uri("https://vimeo.com/123")), "vimeo provider");
}

void Rate()
{
    Assert(ByteRateLimiter.Parse("4.2M") == (long)(4.2 * 1024 * 1024), "megabytes");
    Assert(ByteRateLimiter.Parse("") is null, "empty");
    Assert(ByteRateLimiter.Parse("1T") == 1L << 40 && ByteRateLimiter.Parse("1p") == 1L << 50, "large classic suffixes");
    foreach (var text in new[] { "0.1", "8192P", "9223372036854775808" })
    {
        try { _ = ByteRateLimiter.Parse(text); throw new InvalidOperationException("Invalid byte-rate boundary accepted: " + text); }
        catch (FormatException) { }
    }
}

void Dash()
{
    const string xml = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011">
          <Period>
            <AdaptationSet mimeType="video/mp4">
              <Representation id="720" bandwidth="1000" height="720">
                <BaseURL>https://cdn.example/720.mp4</BaseURL>
              </Representation>
            </AdaptationSet>
            <AdaptationSet mimeType="audio/mp4">
              <Representation id="audio" bandwidth="128">
                <BaseURL>https://cdn.example/audio.m4a</BaseURL>
              </Representation>
            </AdaptationSet>
          </Period>
        </MPD>
        """;
    var manifest = DashManifestParser.Parse(xml, new Uri("https://cdn.example/show.mpd"));
    Assert(manifest.Representations.Count == 2, "representations");
    Assert(manifest.Representations[0].Height == 720, "height");
}

void Capture()
{
    var page = new Uri("https://videos.example/watch");
    var found = PageCapture.Read("player.src = \"https://cdn.example/show.m3u8\";", page);
    Assert(found.Media.Count == 1 && !found.Refused, "captured media");
    var refused = PageCapture.Read("\"widevine\" license", page);
    Assert(refused.Refused, "drm");
}

static MediaCandidate Candidate(int height, string url) => new()
{
    Url = new Uri(url),
    Height = height,
    Extension = "mp4",
    Kind = MediaKind.Progressive
};

static void Assert(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}















