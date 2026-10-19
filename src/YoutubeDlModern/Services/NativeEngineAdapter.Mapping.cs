using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

public sealed partial class NativeEngineAdapter
{
    private static NativeDownloadRequest CreateRequest(
        string url, DownloadOptions options, AppSettings? settings, string? ffmpeg)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new NativeEngineUnsupportedException("The address is not a valid URL.");
        var (quality, height) = MapQuality(options.QualityPreset);
        var output = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? settings?.General.DownloadDirectory
            : options.OutputDirectory;
        if (string.IsNullOrWhiteSpace(output))
            output = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var ffmpegPath = string.IsNullOrWhiteSpace(ffmpeg) ? null : Path.Combine(ffmpeg, "ffmpeg.exe");
        if (ffmpegPath is not null && !File.Exists(ffmpegPath) && File.Exists(ffmpeg))
            ffmpegPath = ffmpeg;

        return new NativeDownloadRequest
        {
            Url = uri,
            OutputDirectory = output,
            OutputTemplate = string.IsNullOrWhiteSpace(options.OutputTemplate) ? settings?.General.OutputTemplate ?? "" : options.OutputTemplate,
            RestrictFilenames = settings?.General.RestrictFilenames ?? false,
            NoOverwrites = settings?.General.NoOverwrites ?? false,
            NoPostOverwrites = settings?.Formats.NoPostOverwrites ?? false,
            RecodeVideo = options.DownloadMode.Equals("audio-only", StringComparison.OrdinalIgnoreCase) || options.QualityPreset.Equals("audio", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(options.Container) && !options.Container.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : settings?.Formats.RecodeVideo ?? "",
            NoContinue = settings?.General.NoContinue ?? false,
            NoPart = settings?.General.NoPart ?? false,
            NoMtime = settings?.General.NoMtime ?? false,
            CacheDirectory = settings?.General.CacheDir,
            DownloadArchiveFile = settings?.General.DownloadArchive ?? "",
            AudioOnly = options.DownloadMode.Equals("audio-only", StringComparison.OrdinalIgnoreCase) || options.QualityPreset.Equals("audio", StringComparison.OrdinalIgnoreCase),
            KeepOriginal = settings?.Formats.KeepVideo ?? false,
            DownloadPlaylist = options.DownloadPlaylist,
            ExpandPlaylist = options.DownloadPlaylist,
            MaximumPlaylistEntries = 10000,
            PlaylistItems = options.PlaylistItems,
            PlaylistStart = options.PlaylistStart,
            PlaylistEnd = options.PlaylistEnd,
            PlaylistReverse = settings?.Network.PlaylistReverse ?? false,
            PlaylistRandom = settings?.Network.PlaylistRandom ?? false,
            IgnoreErrors = settings?.General.AbortOnError == true ? false : settings?.General.IgnoreErrors ?? true,
            MatchTitle = settings?.Selection.MatchTitle ?? "",
            RejectTitle = settings?.Selection.RejectTitle ?? "",
            MaxDownloads = settings?.Selection.MaxDownloads ?? 0,
            Date = settings?.Selection.Date ?? "",
            DateBefore = settings?.Selection.DateBefore ?? "",
            DateAfter = settings?.Selection.DateAfter ?? "",
            MinimumViews = settings?.Selection.MinViews > 0 ? settings.Selection.MinViews : null,
            MaximumViews = settings?.Selection.MaxViews > 0 ? settings.Selection.MaxViews : null,
            MaximumAge = settings?.Selection.AgeLimit > 0 ? settings.Selection.AgeLimit : null,
            MatchFilter = settings?.Selection.MatchFilter ?? "",
            MinimumFileSize=settings?.Selection.MinFilesize??"",
            MaximumFileSize=settings?.Selection.MaxFilesize??"",
            VideoOnly = options.DownloadMode.Equals("video-only", StringComparison.OrdinalIgnoreCase) && !options.QualityPreset.Equals("audio", StringComparison.OrdinalIgnoreCase),
            FormatSelector = options.QualityPreset.Equals("custom", StringComparison.OrdinalIgnoreCase) ? options.CustomFormat : "",
            VideoCodec = options.VideoCodec,
            MaximumFrameRate = options.MaxFps,
            AudioFormat = options.AudioFormat,
            AudioQuality = options.AudioQuality,
            Container = options.Container.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : options.Container,
            MergeOutputFormat = options.DownloadMode.Equals("audio-only", StringComparison.OrdinalIgnoreCase) || options.QualityPreset.Equals("audio", StringComparison.OrdinalIgnoreCase) ? "" :
                !string.IsNullOrWhiteSpace(options.Container) && !options.Container.Equals("auto", StringComparison.OrdinalIgnoreCase) ? options.Container : settings?.Formats.MergeOutputFormat ?? "",
            PreferFreeFormats = settings?.Formats.PreferFreeFormats ?? false,
            Quality = quality,
            MaximumHeight = height,
            LiveDashSelection=settings is null?null:NativeLivePolicy.Create(settings.Formats),
            Authentication = MapAuthentication(settings?.Auth),
            CookieFile = options.CookiesFile,
            Proxy = settings?.Network.Proxy,
            GeoVerification = NativeGeoVerification.FromLegacy(settings?.Network.GeoVerificationProxy, settings?.Network.GeoBypass ?? true, settings?.Network.GeoBypassCountry, settings?.Network.GeoBypassIpBlock),
            BufferSize = settings?.Network.BufferSize ?? "",
            NoResizeBuffer = settings?.Network.NoResizeBuffer ?? false,
            HttpChunkSize = settings?.Network.HttpChunkSize ?? "",
            ExternalDownloader = string.IsNullOrWhiteSpace(settings?.Network.ExternalDownloader) ? null : settings.Network.ExternalDownloader,
            ExternalDownloaderArguments = string.IsNullOrWhiteSpace(settings?.Network.ExternalDownloaderArgs) ? null : [settings.Network.ExternalDownloaderArgs],
            SourceAddress = settings?.Network.SourceAddress,
            IpVersion = settings?.Network.IpVersion switch { null or "" or "auto" => 0, "4" => 4, "6" => 6, _ => throw new ArgumentException("Choose automatic, IPv4 or IPv6 for the network connection.") },
            NoCheckCertificate = settings?.Network.NoCheckCertificate ?? false,
            HttpHeaders = NativeHttpHeaders.ParseLines(settings?.Network.CustomHeaders),
            UserAgent = settings?.Network.UserAgent,
            Referer = settings?.Network.Referer,
            RateLimit = string.IsNullOrWhiteSpace(options.RateLimit) ? settings?.Network.RateLimit : options.RateLimit,
            Retries = options.Retries,
            FragmentRetries = settings?.Network.FragmentRetries,
            ConcurrentFragments = options.ConcurrentFragments,
            SleepIntervalSeconds = settings?.Network.SleepInterval is { } minimumDelay && minimumDelay != 0 ? minimumDelay : null,
            MaximumSleepIntervalSeconds = settings?.Network.MaxSleepInterval is { } maximumDelay && maximumDelay != 0 ? maximumDelay : null,
            TimeoutSeconds = settings?.Network.SocketTimeout > 0 ? settings.Network.SocketTimeout : 30,
            FfmpegPath = ffmpegPath,
            WriteSubs = options.WriteSubs || settings?.Subtitles.WriteAllSubs == true,
            WriteAutoSubs = options.WriteAutoSubs,
            EmbedSubs = options.EmbedSubs,
            SubLanguages = settings?.Subtitles.WriteAllSubs == true ? "all" :
                string.IsNullOrWhiteSpace(options.SubLangs) ? settings?.Subtitles.SubLangs ?? "" : options.SubLangs,
            SubFormat = settings?.Subtitles.SubFormat ?? "",
            ConvertSubs = settings?.Subtitles.ConvertSubs ?? "",
            WriteThumbnail = options.WriteThumbnail,
            WriteAllThumbnails = settings?.General.WriteAllThumbnails ?? false,
            EmbedThumbnail = options.EmbedThumbnail,
            WriteDescription = settings?.General.WriteDescription == true,
            WriteInfoJson = settings?.General.WriteInfoJson == true,
            EmbedChapters = options.EmbedChapters,
            AddMetadata = options.AddMetadata,
            ExecCommands = string.IsNullOrWhiteSpace(settings?.PostProcess.ExecCommand) ? null : [settings.PostProcess.ExecCommand],
            SponsorBlockMark = string.Equals(settings?.PostProcess.SponsorBlock, "mark", StringComparison.OrdinalIgnoreCase) ? "all" : null,
            SponsorBlockRemove = string.Equals(settings?.PostProcess.SponsorBlock, "remove", StringComparison.OrdinalIgnoreCase) ? "all" : null,
            Fixup = string.IsNullOrWhiteSpace(settings?.PostProcess.Fixup) ? null : settings.PostProcess.Fixup,
            PostprocessorArguments = string.IsNullOrWhiteSpace(settings?.PostProcess.PostprocessorArgs) ? null : [settings.PostProcess.PostprocessorArgs],
            MetadataParseRules = string.IsNullOrWhiteSpace(settings?.PostProcess.ParseMetadata) ? [] : [settings.PostProcess.ParseMetadata],
            BrowserCookies = options.CookiesFromBrowser.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null
                : options.CookiesFromBrowser
        };
    }

    private NativeDownloadRequest CreateAnalysisRequest(string url, AnalysisOptions? options)
    {
        var settings = _settings?.CreateSnapshot();
        if (settings is not null) ValidateAnalysisSettings(settings,url);
        options ??= new AnalysisOptions
        {
            CookiesFile = settings?.Auth.CookiesFile ?? "",
            CookiesFromBrowser = settings?.Auth.CookiesFromBrowser ?? "none"
        };
        if (options.Mode is null || !new[] { "quick", "full", "playlist" }.Contains(options.Mode, StringComparer.OrdinalIgnoreCase))
            throw new NativeEngineUnsupportedException("Choose quick, full or playlist analysis.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new NativeEngineUnsupportedException("The address is not a valid URL.");
        var request = new NativeDownloadRequest
        {
            Url = uri,
            SkipDownload = YoutubeDl.Engine.Providers.YouTube.YouTubeAddress.IsYouTubeHost(uri),
            MetadataInventoryOnly = YoutubeDl.Engine.Providers.YouTube.YouTubeAddress.IsYouTubeHost(uri),
            OutputDirectory = Path.GetTempPath(),
            CacheDirectory = settings?.General.CacheDir,
            DownloadPlaylist = options.Mode.Equals("playlist", StringComparison.OrdinalIgnoreCase),
            ExpandPlaylist = options.Mode.Equals("playlist", StringComparison.OrdinalIgnoreCase),
            MaximumPlaylistEntries = 10000,
            Authentication = MapAuthentication(settings?.Auth),
            CookieFile = options.CookiesFile,
            BrowserCookies = options.CookiesFromBrowser.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : options.CookiesFromBrowser,
            Proxy = settings?.Network.Proxy,
            GeoVerification = NativeGeoVerification.FromLegacy(settings?.Network.GeoVerificationProxy, settings?.Network.GeoBypass ?? true, settings?.Network.GeoBypassCountry, settings?.Network.GeoBypassIpBlock),
            SourceAddress = settings?.Network.SourceAddress,
            IpVersion = settings?.Network.IpVersion switch { null or "" or "auto" => 0, "4" => 4, "6" => 6, _ => throw new ArgumentException("Choose automatic, IPv4 or IPv6 for the network connection.") },
            NoCheckCertificate = settings?.Network.NoCheckCertificate ?? false,
            HttpHeaders = NativeHttpHeaders.ParseLines(settings?.Network.CustomHeaders),
            UserAgent = settings?.Network.UserAgent,
            Referer = settings?.Network.Referer,
            Retries = settings?.Network.Retries ?? 10,
            TimeoutSeconds = settings?.Network.SocketTimeout > 0 ? settings.Network.SocketTimeout : 30
        };
        try { request=NativeExtraArguments.Apply(request, NativeAnalysisArguments.Select(ProcessRunner.SplitArguments(settings?.General.ExtraArgs ?? ""))); _engine.ValidateAuthenticationOptions(request); return request; }
        catch (NotSupportedException ex) { throw new NativeEngineUnsupportedException(ex.Message); }
    }

    private static NativeAuthenticationOptions MapAuthentication(AuthSettings? auth) => new() { Username=auth?.Username??"",Password=auth?.Password??"",TwoFactor=auth?.TwoFactor??"",UseNetrc=auth?.UseNetrc??false,VideoPassword=auth?.VideoPassword??"" };

    private static (QualityPreference Quality, int? Height) MapQuality(string preset)
    {
        if (preset.Equals("worst", StringComparison.OrdinalIgnoreCase))
            return (QualityPreference.Worst, null);
        return int.TryParse(preset, out var height) && height > 0
            ? (QualityPreference.MaximumHeight, height)
            : (QualityPreference.Best, null);
    }

    private static VideoInfo ToVideoInfo(string url, NativeMediaInfo info) => new()
    {
        NativePlaylistSnapshot = info.PlaylistSnapshot,
        Id = info.Id,
        Title = info.Title,
        Description = info.Description,
        Thumbnail = info.ThumbnailUrl,
        Thumbnails = info.Thumbnails.Select(item => new ThumbnailInfo { Id = item.Id, Url = item.Url.AbsoluteUri, Width = item.Width, Height = item.Height }).ToList(),
        Uploader = info.Uploader,
        WebpageUrl = url,
        ExtractorKey = info.Extractor,
        Duration = info.DurationSeconds,
        UploadDate = info.UploadDate?.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
        ViewCount = info.ViewCount,
        IsLive = info.IsLive,
        Type = info.IsPlaylist ? "playlist" : "video",
        Entries = info.Entries.Select(entry => new PlaylistEntry { Id = entry.Id, Title = entry.Title, WebpageUrl = entry.Url?.AbsoluteUri,
            Url = entry.Url?.AbsoluteUri, Duration = entry.DurationSeconds, Thumbnail = entry.ThumbnailUrl }).ToList(),
        Subtitles = MapSubtitles(info, automatic: false),
        AutomaticCaptions = MapSubtitles(info, automatic: true),
        Formats = info.Formats.Where(format => PreviewFormat(info, format)).Select(format => new FormatInfo
        {
            FormatId = format.Id,
            Ext = format.Extension,
            Height = format.Height,
            Width = format.Width,
            Fps = format.FramesPerSecond,
            Filesize = format.FileSize,
            FormatNote = format.Availability == FormatAvailability.Ready ? format.Note : (format.Note is { Length: > 0 } ? format.Note + " · " : "") + "Checked on download",
            Vcodec = format.AudioOnly ? "none" : format.VideoCodec ?? "unknown",
            Acodec = format.HasAudio == false ? "none" : format.AudioCodec ?? "unknown",
            Protocol = format.Protocol
        }).ToList()
    };
    private static Dictionary<string, List<SubtitleFormat>> MapSubtitles(NativeMediaInfo info, bool automatic) => info.Subtitles.Where(track => track.Automatic == automatic)
        .GroupBy(track => string.IsNullOrWhiteSpace(track.Language) ? "und" : track.Language, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Select(track => new SubtitleFormat { Ext = track.Extension, Url = track.Url.AbsoluteUri }).ToList(), StringComparer.OrdinalIgnoreCase);
}
