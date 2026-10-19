using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.Views;

namespace YoutubeDlModern.ViewModels;

public sealed class QualityChoice
{
    public string Key { get; }
    public string Label { get; }
    public QualityChoice(string key, string label) { Key = key; Label = label; }
    public override string ToString() => Label;
}

public partial class QueueViewModel : ObservableObject, IDisposable
{
    private const int MaxBatchUrls = 1000;
    private const int MaximumUrlLength = 8192;
    private const int MaximumPastedInputCharacters = 10 * 1024 * 1024;
    private const long MaximumBatchFileBytes = 10L * 1024 * 1024;

    private readonly DownloadQueueService _queue;
    private readonly SettingsService _settings;
    private readonly IYtDlpEngine _engine;
    private readonly Func<string?, CancellationToken, Task<ImageSource?>> _loadThumbnail;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private CancellationTokenSource? _fetchCts;
    private CancellationTokenSource? _formatsCts;
    private string _fetchedUrl = "";

    public event Action<string>? NotifyRequested;
    public event Action? EngineStateChanged;

    public ObservableCollection<DownloadItemViewModel> Items => _queue.Items;
    public ObservableCollection<QualityChoice> QualityChoices { get; } = new();
    public IReadOnlyList<QualityChoice> AnalysisModes { get; } =
    [
        new("quick", "Quick"),
        new("full", "Full"),
        new("playlist", "Playlist")
    ];
    public IReadOnlyList<QualityChoice> DownloadModes { get; } =
    [
        new("video-audio", "Video + audio"),
        new("video-only", "Video only"),
        new("audio-only", "Audio only")
    ];
    public IReadOnlyList<QualityChoice> VideoCodecs { get; } =
    [
        new("any", "Any"), new("h264", "H.264"), new("h265", "H.265"),
        new("vp9", "VP9"), new("av1", "AV1")
    ];
    public IReadOnlyList<QualityChoice> FrameRateChoices { get; } =
    [
        new("0", "Any"), new("30", "30 fps"), new("60", "60 fps")
    ];
    public string[] Containers { get; } = { "auto", "avi", "flv", "mkv", "mov", "mp4", "webm" };
    public string[] BrowserChoices { get; } =
        { "none", "brave", "chrome", "chromium", "edge", "firefox", "opera", "safari", "vivaldi", "whale" };
    public string[] AudioFormats { get; } = { "mp3", "m4a", "opus", "flac", "wav", "vorbis", "aac", "best" };
    public string[] AudioQualities { get; } = { "0", "2", "5", "128K", "192K", "256K", "320K" };

    [ObservableProperty] private string _urlInput = "";
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private bool _isInspectingFormats;
    [ObservableProperty] private string _fetchError = "";
    [ObservableProperty] private VideoInfo? _preview;
    [ObservableProperty] private ImageSource? _previewThumbnail;

    [ObservableProperty] private string _selectedAnalysisMode = "quick";
    [ObservableProperty] private bool _useSavedAnalysisAuthentication = true;
    [ObservableProperty] private string _analysisCookiesFile = "";
    [ObservableProperty] private string _analysisCookiesFromBrowser = "none";

    [ObservableProperty] private string _addDownloadMode = "video-audio";
    [ObservableProperty] private string _selectedQualityKey = "best";
    [ObservableProperty] private string _customFormat = "";
    [ObservableProperty] private string _addVideoCodec = "any";
    [ObservableProperty] private string _addMaxFps = "0";
    [ObservableProperty] private string _addContainer = "auto";
    [ObservableProperty] private string _addAudioFormat = "mp3";
    [ObservableProperty] private string _addAudioQuality = "192K";
    [ObservableProperty] private bool _addWriteSubs;
    [ObservableProperty] private bool _addWriteAutoSubs;
    [ObservableProperty] private bool _addEmbedSubs;
    [ObservableProperty] private string _addSubLangs = "en";
    [ObservableProperty] private bool _addWriteThumbnail;
    [ObservableProperty] private bool _addEmbedThumbnail;
    [ObservableProperty] private bool _addMetadata = true;
    [ObservableProperty] private bool _addEmbedChapters = true;
    [ObservableProperty] private string _addOutputDir = "";
    [ObservableProperty] private string _addOutputTemplate = "";
    [ObservableProperty] private bool _addDownloadPlaylist = true;
    [ObservableProperty] private string _addPlaylistItems = "";
    [ObservableProperty] private int _addPlaylistStart;
    [ObservableProperty] private int _addPlaylistEnd;
    [ObservableProperty] private string _addCookiesFile = "";
    [ObservableProperty] private string _addCookiesFromBrowser = "none";
    [ObservableProperty] private string _addRateLimit = "";
    [ObservableProperty] private int _addRetries = 10;
    [ObservableProperty] private int _addConcurrentFragments = 1;
    [ObservableProperty] private string _addExtraArgs = "";

    [ObservableProperty] private bool _isEngineInstalled;
    [ObservableProperty] private bool _isInstallingEngine;
    [ObservableProperty] private double _engineInstallProgress;
    [ObservableProperty] private string _engineInstallText = "";

    [ObservableProperty] private bool _hasItems;

    public QueueViewModel(DownloadQueueService queue, SettingsService settings, IYtDlpEngine engine,
        Func<string?, CancellationToken, Task<ImageSource?>>? loadThumbnail = null)
    {
        _queue = queue;
        _settings = settings;
        _engine = engine;
        _loadThumbnail = loadThumbnail ?? ThumbnailService.LoadAsync;
        _settings.Changed += OnSettingsChanged;

        _isEngineInstalled = engine.IsInstalled;
        Items.CollectionChanged += (_, _) => HasItems = Items.Count > 0;
        AnalysisCookiesFile = settings.Data.Auth.CookiesFile;
        AnalysisCookiesFromBrowser = NormalizeBrowser(settings.Data.Auth.CookiesFromBrowser);
        RebuildQualityChoices();
        ApplyDownloadDefaults();
    }

    // ---- Derived preview state ----
    public bool HasPreview => Preview is not null;
    public bool HasFetchError => !string.IsNullOrWhiteSpace(FetchError);
    public bool IsPlaylistPreview => Preview?.IsPlaylist == true;
    public bool ShowAudioOptions => AddDownloadMode == "audio-only";
    public bool ShowVideoOptions => AddDownloadMode != "audio-only";
    public bool ShowCustomFormat => SelectedQualityKey == "custom";
    public bool HasSubtitleInfo => (Preview?.SubtitleLanguages.Count ?? 0) > 0 || (Preview?.AutoCaptionLanguages.Count ?? 0) > 0;

    public string PreviewMetaText
    {
        get
        {
            if (Preview is null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Preview.UploaderText)) parts.Add(Preview.UploaderText);
            if (!string.IsNullOrWhiteSpace(Preview.DurationText)) parts.Add(Preview.DurationText);
            if (Preview.ViewCount is > 0) parts.Add($"{Formatting.FormatCount(Preview.ViewCount)} views");
            if (!string.IsNullOrWhiteSpace(Preview.ExtractorKey)) parts.Add(Preview.ExtractorKey);
            return string.Join(" | ", parts);
        }
    }

    public string SubtitleSummaryText
    {
        get
        {
            if (Preview is null) return "";
            var subs = Preview.SubtitleLanguages;
            var autos = Preview.AutoCaptionLanguages;
            var manual = subs.Count > 0 ? $"Subtitles: {string.Join(", ", subs.Take(12))}{(subs.Count > 12 ? "..." : "")}" : "Subtitles: none";
            var auto = autos.Count > 0 ? $"  |  Auto-captions: {autos.Count} languages" : "";
            return manual + auto;
        }
    }

    partial void OnPreviewChanged(VideoInfo? value)
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsPlaylistPreview));
        OnPropertyChanged(nameof(PreviewMetaText));
        OnPropertyChanged(nameof(SubtitleSummaryText));
        OnPropertyChanged(nameof(HasSubtitleInfo));
    }

    partial void OnFetchErrorChanged(string value) => OnPropertyChanged(nameof(HasFetchError));

    partial void OnSelectedQualityKeyChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAudioOptions));
        OnPropertyChanged(nameof(ShowCustomFormat));
    }

    public void RefreshEngineState() => IsEngineInstalled = _engine.IsInstalled;

    // ---- Commands ----

    [RelayCommand]
    private async Task FetchAsync()
    {
        FetchError = "";
        List<string> urls;
        try
        {
            urls = SplitUrls(UrlInput);
        }
        catch (ArgumentException ex)
        {
            FetchError = ex.Message;
            return;
        }
        if (urls.Count == 0)
        {
            FetchError = "Paste at least one video or playlist URL.";
            return;
        }

        var invalidUrl = urls.FirstOrDefault(url => !IsSupportedUrl(url));
        if (invalidUrl is not null)
        {
            FetchError = $"Only absolute HTTP or HTTPS URLs are supported: {invalidUrl}";
            return;
        }

        if (urls.Count > MaxBatchUrls)
        {
            FetchError = $"A maximum of {MaxBatchUrls} URLs can be added at once.";
            return;
        }
        if (urls.Count > 1 && !_queue.CanEnqueue(urls.Count))
        {
            FetchError = "The queue does not have enough capacity for this batch. Remove finished items first.";
            return;
        }

        if (!ValidateCookiesFile(BuildAnalysisOptions().CookiesFile, out var cookieError))
        {
            FetchError = cookieError;
            return;
        }

        CancelFormatInspection();

        if (!EnsureEngineInstalledForAction())
            return;

        // Multiple URLs (or lines) behave like a batch file: enqueue everything directly.
        if (urls.Count > 1)
        {
            AppSettings settingsSnapshot;
            DownloadOptions options;
            try
            {
                settingsSnapshot = _settings.CreateSnapshot();
                SettingsValidation.ThrowIfInvalid(settingsSnapshot);
                options = BuildDefaultOptions(useAnalysisAuthentication: true, settingsSnapshot);
            }
            catch (Exception ex)
            {
                FetchError = "Cannot enqueue this batch: " + ex.Message;
                return;
            }

            EnqueueBatch(urls, options, settingsSnapshot, _lifetimeCts.Token);
            NotifyRequested?.Invoke($"Added {urls.Count} URLs to the queue.");
            UrlInput = "";
            return;
        }

        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _fetchCts = fetchCts;
        IsFetching = true;
        Preview = null;
        PreviewThumbnail = null;
        try
        {
            var url = urls[0];
            var authentication = BuildAnalysisOptions();
            var defaults = _settings.CreateSnapshot();
            var info = await _engine.GetVideoInfoAsync(url, fetchCts.Token, authentication);
            fetchCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_fetchCts, fetchCts)) return;

            // Publish the preview only after all of its editable options are ready.
            _fetchedUrl = url;
            RebuildQualityChoices(info);
            ApplyDownloadDefaults(useAnalysisAuthentication: true, defaults, authentication);
            Preview = info;
            var thumbnail = await _loadThumbnail(info.BestThumbnailUrl, fetchCts.Token);
            if (!fetchCts.IsCancellationRequested && ReferenceEquals(_fetchCts, fetchCts) &&
                ReferenceEquals(Preview, info))
                PreviewThumbnail = thumbnail;
        }
        catch (OperationCanceledException) { /* user canceled the fetch */ }
        catch (Exception ex)
        {
            if (!fetchCts.IsCancellationRequested && ReferenceEquals(_fetchCts, fetchCts))
                FetchError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_fetchCts, fetchCts))
            {
                _fetchCts = null;
                IsFetching = false;
            }
            fetchCts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelFetch()
    {
        _fetchCts?.Cancel();
    }

    [RelayCommand]
    private void AddToQueue()
    {
        if (Preview is null) return;

        CancelFormatInspection();

        if (!EnsureEngineInstalledForAction())
            return;
        if (!_queue.CanEnqueue(1))
        {
            FetchError = "The queue is full. Remove finished items before adding another download.";
            return;
        }

        if (!TryBuildCurrentOptions(out var options, out var error))
        {
            FetchError = error;
            return;
        }

        _fetchCts?.Cancel(); // The optional thumbnail must not mutate an accepted preview.
        _queue.Enqueue(_fetchedUrl, options!, Preview, PreviewThumbnail);
        NotifyRequested?.Invoke($"Added to queue: {Preview.DisplayTitle}");

        Preview = null;
        PreviewThumbnail = null;
        UrlInput = "";
        FetchError = "";
    }

    [RelayCommand]
    private void DiscardPreview()
    {
        _fetchCts?.Cancel();
        CancelFormatInspection();
        Preview = null;
        PreviewThumbnail = null;
        FetchError = "";
    }

    [RelayCommand]
    private async Task InspectFormatsAsync(CancellationToken commandToken)
    {
        if (Preview is null) return;
        FetchError = "";
        _formatsCts?.Cancel();
        _formatsCts?.Dispose();
        var formatsCts = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken, _lifetimeCts.Token);
        _formatsCts = formatsCts;
        IsInspectingFormats = true;
        var inspectedPreview = Preview;
        try
        {
            IReadOnlyList<FormatInfo> formats = (Preview.Formats ?? [])
                .Where(format => !string.IsNullOrWhiteSpace(format.FormatId))
                .ToList();

            if (formats.Count == 0)
            {
                var lookupUrl = _fetchedUrl;
                var lookupIsPlaylist = Preview.IsPlaylist;
                if (lookupIsPlaylist && FindFirstPlaylistEntryUrl(Preview) is { } entryUrl)
                {
                    // Resolve one entry directly instead of asking yt-dlp to reopen and
                    // traverse the playlist merely to obtain one format list.
                    lookupUrl = entryUrl;
                    lookupIsPlaylist = false;
                }

                formats = await _engine.ListFormatsAsync(
                    lookupUrl,
                    formatsCts.Token,
                    BuildAnalysisOptions(),
                    lookupIsPlaylist);
            }

            formatsCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Preview, inspectedPreview)) return;
            var title = inspectedPreview.IsPlaylist
                ? "Available formats (first playlist item)"
                : "Available formats";
            var formatSnapshot = formats.ToList();
            var choices = await BackgroundWork.RunAsync(
                token => FormatChoice.CreateChoices(formatSnapshot, token),
                formatsCts.Token);
            formatsCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Preview, inspectedPreview)) return;
            var dialog = new FormatPickerDialog(title, choices);
            var owner = Application.Current?.MainWindow;
            if (owner?.IsVisible == true)
                dialog.Owner = owner;
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedSelector))
            {
                AddDownloadMode = dialog.SelectedDownloadMode;
                CustomFormat = dialog.SelectedSelector;
                SelectedQualityKey = "custom";
                NotifyRequested?.Invoke($"Selected format: {dialog.SelectedSelector}");
            }
        }
        catch (OperationCanceledException) when (formatsCts.IsCancellationRequested)
        {
            // Canceled by the user or because the application is shutting down.
        }
        catch (Exception ex)
        {
            if (!formatsCts.IsCancellationRequested && ReferenceEquals(_formatsCts, formatsCts) &&
                ReferenceEquals(Preview, inspectedPreview))
                FetchError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_formatsCts, formatsCts))
            {
                _formatsCts = null;
                IsInspectingFormats = false;
            }
            formatsCts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelFormatInspection() => _formatsCts?.Cancel();

    [RelayCommand]
    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
                UrlInput = Clipboard.GetText().Trim();
        }
        catch { /* clipboard busy */ }
    }

    private void ClearCustomSelection()
    {
        if (SelectedQualityKey != "custom") return;
        SelectedQualityKey = "best";
        CustomFormat = "";
    }

    partial void OnAddVideoCodecChanged(string value) => ClearCustomSelection();
    partial void OnAddMaxFpsChanged(string value) => ClearCustomSelection();

    partial void OnAddDownloadModeChanged(string value)
    {
        ClearCustomSelection();
        OnPropertyChanged(nameof(ShowAudioOptions));
        OnPropertyChanged(nameof(ShowVideoOptions));
        OnPropertyChanged(nameof(ShowCustomFormat));
    }

    [RelayCommand]
    private void ClearInput()
    {
        _fetchCts?.Cancel();
        CancelFormatInspection();
        UrlInput = "";
        Preview = null;
        PreviewThumbnail = null;
        FetchError = "";
        _fetchedUrl = "";
    }

    [RelayCommand]
    private void BrowseAnalysisCookies()
    {
        var selected = SelectCookiesFile();
        if (selected is not null) AnalysisCookiesFile = selected;
    }

    [RelayCommand]
    private void BrowseDownloadCookies()
    {
        var selected = SelectCookiesFile();
        if (selected is not null) AddCookiesFile = selected;
    }

    /// <summary>Classic --batch-file: reads URLs from a text file (one per line, #/;/] comments).</summary>
    [RelayCommand]
    private async Task ImportBatchFileAsync(CancellationToken ct)
    {
        if (!ValidateCookiesFile(BuildAnalysisOptions().CookiesFile, out var cookieError))
        {
            FetchError = cookieError;
            return;
        }

        if (!EnsureEngineInstalledForAction())
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Select batch file with URLs (one per line)"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
            var operationToken = linkedCts.Token;
            var settingsSnapshot = _settings.CreateSnapshot();
            SettingsValidation.ThrowIfInvalid(settingsSnapshot);
            var options = BuildDefaultOptions(useAnalysisAuthentication: true, settingsSnapshot);
            if (new FileInfo(dlg.FileName).Length > MaximumBatchFileBytes)
                throw new InvalidOperationException(
                    $"The batch file exceeds the {Formatting.FormatBytes(MaximumBatchFileBytes)} safety limit.");
            var urls = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var invalidCount = 0;
            await using var stream = new FileStream(
                dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(operationToken) is { } line)
            {
                var value = line.Trim();
                if (value.Length == 0 || value.StartsWith('#') || value.StartsWith(';') || value.StartsWith(']'))
                    continue;
                if (value.Length > MaximumUrlLength)
                {
                    invalidCount++;
                    continue;
                }
                if (!IsSupportedUrl(value))
                {
                    invalidCount++;
                    continue;
                }
                if (seen.Add(value))
                {
                    if (urls.Count == MaxBatchUrls)
                        throw new InvalidOperationException($"The batch file contains more than {MaxBatchUrls} unique URLs.");
                    urls.Add(value);
                }
            }

            operationToken.ThrowIfCancellationRequested();
            // Commit the bounded batch in one UI turn: no other command can consume
            // capacity between this check and insertion. Cancellation is checked first.
            EnqueueBatch(urls, options, settingsSnapshot, operationToken);

            var skipped = invalidCount > 0 ? $" Skipped {invalidCount} invalid line(s)." : "";
            NotifyRequested?.Invoke($"Imported {urls.Count} URLs from batch file.{skipped}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FetchError = $"Could not import batch file: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseOutputDir()
    {
        var dlg = new OpenFolderDialog { Title = "Choose download folder" };
        if (dlg.ShowDialog() == true)
            AddOutputDir = dlg.FolderName;
    }

    [RelayCommand]
    private async Task InstallEngineAsync()
    {
        if (IsInstallingEngine) return;
        IsInstallingEngine = true;
        EngineInstallText = "Connecting...";
        try
        {
            await _engine.EnsureInstalledAsync(
                new Progress<(double Percent, string Message)>(p =>
                {
                    EngineInstallProgress = p.Percent;
                    EngineInstallText = p.Message;
                }),
                _lifetimeCts.Token);
            RefreshEngineState();
            EngineStateChanged?.Invoke();
            NotifyRequested?.Invoke("yt-dlp engine installed successfully.");
        }
        catch (Exception ex)
        {
            EngineInstallText = "Install failed: " + ex.Message;
        }
        finally
        {
            IsInstallingEngine = false;
        }
    }

    [RelayCommand] private void StartAll() => _queue.StartAll();
    [RelayCommand] private void PauseAll() => _queue.PauseAll();
    [RelayCommand] private void ClearCompleted() => _queue.ClearCompleted();

    // ---- Helpers ----

    private bool EnsureEngineInstalledForAction()
    {
        IsEngineInstalled = _engine.IsInstalled;
        if (IsEngineInstalled)
            return true;

        FetchError = "The yt-dlp engine is not installed yet. Install it before analyzing or adding downloads.";
        return false;
    }

    internal void EnqueueBatch(IReadOnlyList<string> urls, DownloadOptions options,
        AppSettings settingsSnapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_queue.CanEnqueue(urls.Count))
            throw new InvalidOperationException("The queue does not have enough capacity for this batch. Remove finished items first.");
        foreach (var url in urls)
            _queue.Enqueue(url, options, null, settingsSnapshot: settingsSnapshot);
    }

    private void RebuildQualityChoices(VideoInfo? info = null)
    {
        var previous = SelectedQualityKey;
        QualityChoices.Clear();
        QualityChoices.Add(new QualityChoice("best", "Best"));
        var fixedHeights = new[] { 2160, 1440, 1080, 720, 480, 360 };
        var availableHeights = (info ?? Preview)?.AvailableHeights ?? (IReadOnlyList<int>)Array.Empty<int>();
        foreach (var h in fixedHeights.Concat(availableHeights).Distinct().OrderByDescending(height => height))
            QualityChoices.Add(new QualityChoice(h.ToString(), $"{h}p"));
        QualityChoices.Add(new QualityChoice("worst", "Smallest"));
        QualityChoices.Add(new QualityChoice("custom", "Custom"));

        SelectedQualityKey = QualityChoices.Any(choice => choice.Key == previous) ? previous : "best";
    }

    private DownloadOptions BuildDefaultOptions(
        bool useAnalysisAuthentication = false,
        AppSettings? settingsSnapshot = null,
        AnalysisOptions? authentication = null)
    {
        var settings = settingsSnapshot ?? _settings.Data;
        authentication ??= BuildAnalysisOptions();
        var defaultQuality = settings.Formats.DefaultQuality;
        return new DownloadOptions
        {
            DownloadMode = defaultQuality == "audio" ? "audio-only" : "video-audio",
            QualityPreset = defaultQuality == "audio" ? "best" : defaultQuality,
            VideoCodec = "any",
            MaxFps = 0,
            // "auto" lets ArgsBuilder distinguish the global merge preference from an
            // explicit per-download remux request.
            Container = "auto",
            AudioFormat = settings.Formats.AudioFormat,
            AudioQuality = settings.Formats.AudioQuality,
            WriteSubs = settings.Subtitles.WriteSubs,
            WriteAutoSubs = settings.Subtitles.WriteAutoSubs,
            EmbedSubs = settings.Subtitles.EmbedSubs,
            SubLangs = settings.Subtitles.SubLangs,
            WriteThumbnail = settings.General.WriteThumbnail,
            EmbedThumbnail = settings.PostProcess.EmbedThumbnail,
            AddMetadata = settings.PostProcess.AddMetadata,
            EmbedChapters = settings.PostProcess.EmbedChapters,
            OutputTemplate = settings.General.OutputTemplate,
            DownloadPlaylist = !settings.General.NoPlaylist,
            PlaylistItems = settings.Selection.PlaylistItems,
            PlaylistStart = settings.Selection.PlaylistStart,
            PlaylistEnd = settings.Selection.PlaylistEnd,
            CookiesFile = useAnalysisAuthentication ? authentication.CookiesFile : settings.Auth.CookiesFile,
            CookiesFromBrowser = useAnalysisAuthentication
                ? NormalizeBrowser(authentication.CookiesFromBrowser)
                : NormalizeBrowser(settings.Auth.CookiesFromBrowser),
            RateLimit = settings.Network.RateLimit,
            Retries = Math.Max(0, settings.Network.Retries),
            ConcurrentFragments = Math.Clamp(settings.Network.ConcurrentFragments, 1, 32)
        };
    }

    private void ApplyDownloadDefaults(bool useAnalysisAuthentication = false,
        AppSettings? settingsSnapshot = null, AnalysisOptions? authentication = null)
    {
        var defaults = BuildDefaultOptions(useAnalysisAuthentication, settingsSnapshot, authentication);
        AddDownloadMode = defaults.DownloadMode;
        SelectedQualityKey = QualityChoices.Any(choice => choice.Key == defaults.QualityPreset)
            ? defaults.QualityPreset
            : "best";
        CustomFormat = defaults.CustomFormat;
        AddVideoCodec = defaults.VideoCodec;
        AddMaxFps = defaults.MaxFps.ToString();
        AddContainer = Containers.Contains(defaults.Container) ? defaults.Container : "auto";
        AddAudioFormat = defaults.AudioFormat;
        AddAudioQuality = defaults.AudioQuality;
        AddWriteSubs = defaults.WriteSubs;
        AddWriteAutoSubs = defaults.WriteAutoSubs;
        AddEmbedSubs = defaults.EmbedSubs;
        AddSubLangs = defaults.SubLangs;
        AddWriteThumbnail = defaults.WriteThumbnail;
        AddEmbedThumbnail = defaults.EmbedThumbnail;
        AddMetadata = defaults.AddMetadata;
        AddEmbedChapters = defaults.EmbedChapters;
        AddOutputDir = defaults.OutputDirectory;
        AddOutputTemplate = defaults.OutputTemplate;
        AddDownloadPlaylist = defaults.DownloadPlaylist;
        AddPlaylistItems = defaults.PlaylistItems;
        AddPlaylistStart = defaults.PlaylistStart;
        AddPlaylistEnd = defaults.PlaylistEnd;
        AddCookiesFile = defaults.CookiesFile;
        AddCookiesFromBrowser = defaults.CookiesFromBrowser;
        AddRateLimit = defaults.RateLimit;
        AddRetries = defaults.Retries;
        AddConcurrentFragments = defaults.ConcurrentFragments;
        AddExtraArgs = defaults.ExtraArgs;
    }

    partial void OnUseSavedAnalysisAuthenticationChanged(bool value)
    {
        if (value) OnSettingsChanged();
    }

    private void OnSettingsChanged()
    {
        if (!UseSavedAnalysisAuthentication) return;
        AnalysisCookiesFile = _settings.Data.Auth.CookiesFile;
        AnalysisCookiesFromBrowser = NormalizeBrowser(_settings.Data.Auth.CookiesFromBrowser);
    }

    internal AnalysisOptions BuildAnalysisOptions() => new()
    {
        Mode = SelectedAnalysisMode,
        CookiesFile = (UseSavedAnalysisAuthentication ? _settings.Data.Auth.CookiesFile : AnalysisCookiesFile).Trim(),
        CookiesFromBrowser = NormalizeBrowser(UseSavedAnalysisAuthentication
            ? _settings.Data.Auth.CookiesFromBrowser : AnalysisCookiesFromBrowser)
    };

    private bool TryBuildCurrentOptions(out DownloadOptions? options, out string error)
    {
        options = null;
        error = "";

        try
        {
            SettingsValidation.ThrowIfInvalid(_settings.Data);
            SettingsValidation.ValidateExtraArguments(AddExtraArgs, "per-download extra arguments");
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        if (SelectedQualityKey == "custom" && string.IsNullOrWhiteSpace(CustomFormat))
        {
            error = "Enter a custom yt-dlp format code, or choose a quality preset.";
            return false;
        }
        if (AddPlaylistStart < 0 || AddPlaylistEnd < 0 ||
            (AddPlaylistStart > 0 && AddPlaylistEnd > 0 && AddPlaylistEnd < AddPlaylistStart))
        {
            error = "Playlist start and end must be positive, and the end cannot precede the start.";
            return false;
        }
        if (AddRetries is < 0 or > 1000)
        {
            error = "Retries must be between 0 and 1000.";
            return false;
        }
        if (AddConcurrentFragments is < 1 or > 32)
        {
            error = "Concurrent fragments must be between 1 and 32.";
            return false;
        }
        if (!SettingsValidation.IsValidRateLimit(AddRateLimit))
        {
            error = "The speed limit must be a positive number, optionally followed by K, M, G, T, or P (for example 500K or 4.2M).";
            return false;
        }
        if (!ValidateCookiesFile(AddCookiesFile, out error))
            return false;

        options = new DownloadOptions
        {
            DownloadMode = AddDownloadMode,
            QualityPreset = SelectedQualityKey,
            CustomFormat = CustomFormat.Trim(),
            VideoCodec = AddVideoCodec,
            MaxFps = int.TryParse(AddMaxFps, out var fps) ? fps : 0,
            Container = AddContainer,
            AudioFormat = AddAudioFormat,
            AudioQuality = AddAudioQuality.Split(' ')[0],
            WriteSubs = AddWriteSubs,
            WriteAutoSubs = AddWriteAutoSubs,
            EmbedSubs = AddEmbedSubs,
            SubLangs = AddSubLangs.Trim(),
            WriteThumbnail = AddWriteThumbnail,
            EmbedThumbnail = AddEmbedThumbnail,
            AddMetadata = AddMetadata,
            EmbedChapters = AddEmbedChapters,
            OutputDirectory = AddOutputDir.Trim(),
            OutputTemplate = AddOutputTemplate.Trim(),
            DownloadPlaylist = AddDownloadPlaylist,
            PlaylistItems = AddPlaylistItems.Trim(),
            PlaylistStart = AddPlaylistStart,
            PlaylistEnd = AddPlaylistEnd,
            CookiesFile = AddCookiesFile.Trim(),
            CookiesFromBrowser = NormalizeBrowser(AddCookiesFromBrowser),
            RateLimit = AddRateLimit.Trim(),
            Retries = AddRetries,
            ConcurrentFragments = AddConcurrentFragments,
            ExtraArgs = AddExtraArgs
        };
        return true;
    }

    private static bool ValidateCookiesFile(string value, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(value) || File.Exists(value)) return true;
        error = "The selected cookies file does not exist.";
        return false;
    }

    private static string NormalizeBrowser(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value;

    private static string? SelectCookiesFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Netscape-format cookies file",
            Filter = "Cookie files (*.txt;*.cookies)|*.txt;*.cookies|All files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static List<string> SplitUrls(string input)
    {
        if (input.Length > MaximumPastedInputCharacters)
            throw new ArgumentException(
                $"Pasted input exceeds the {Formatting.FormatBytes(MaximumPastedInputCharacters)} safety limit.");

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tokenStart = -1;
        for (var index = 0; index <= input.Length; index++)
        {
            var isDelimiter = index == input.Length || char.IsWhiteSpace(input[index]);
            if (!isDelimiter && tokenStart < 0)
                tokenStart = index;
            if (!isDelimiter || tokenStart < 0)
                continue;

            var length = index - tokenStart;
            if (length > MaximumUrlLength)
                throw new ArgumentException($"A URL cannot exceed {MaximumUrlLength:N0} characters.");
            var value = input.Substring(tokenStart, length);
            tokenStart = -1;
            if (seen.Add(value))
            {
                result.Add(value);
                if (result.Count > MaxBatchUrls)
                    return result;
            }
        }
        return result;
    }

    private static bool IsSupportedUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? FindFirstPlaylistEntryUrl(VideoInfo preview) =>
        preview.Entries?
            .SelectMany(entry => new[] { entry.WebpageUrl, entry.Url })
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && IsSupportedUrl(value));

    internal Task WaitForPendingWorkAsync() => Task.WhenAll(new[]
    {
        FetchCommand.ExecutionTask, InspectFormatsCommand.ExecutionTask,
        ImportBatchFileCommand.ExecutionTask, InstallEngineCommand.ExecutionTask
    }.OfType<Task>());

    private bool _isDisposed;
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _settings.Changed -= OnSettingsChanged;
        _lifetimeCts.Cancel();
        _fetchCts?.Cancel();
        _formatsCts?.Cancel();
        _lifetimeCts.Dispose();
    }
}
