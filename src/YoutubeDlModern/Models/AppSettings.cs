using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YoutubeDlModern.Models;

/// <summary>
/// Root persisted settings object. Every category maps to an option group
/// of classic youtube-dl (see README of the original project), persisted as
/// JSON in %APPDATA%\YoutubeDlModern\settings.json.
/// Empty string / 0 generally means "let the engine decide" (option not passed).
/// </summary>
public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public FormatSettings Formats { get; set; } = new();
    public int StreamPreservingDefaultsVersion { get; set; } = 1;
    public SubtitleSettings Subtitles { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public SelectionSettings Selection { get; set; } = new();
    public AuthSettings Auth { get; set; } = new();
    public PostProcessSettings PostProcess { get; set; } = new();
}

public partial class GeneralSettings : ObservableObject
{
    [ObservableProperty] private string _downloadDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YoutubeDL Modern");
    [ObservableProperty] private string _outputTemplate = "%(title)s [%(id)s].%(ext)s";
    [ObservableProperty] private int _maxConcurrentDownloads = 2;
    [ObservableProperty] private bool _ignoreErrors = true;
    [ObservableProperty] private bool _abortOnError;
    [ObservableProperty] private string _downloadArchive = "";
    [ObservableProperty] private bool _noPlaylist;
    [ObservableProperty] private bool _restrictFilenames;
    [ObservableProperty] private bool _noOverwrites;
    [ObservableProperty] private bool _noContinue;
    [ObservableProperty] private bool _noPart;
    [ObservableProperty] private bool _noMtime;
    [ObservableProperty] private bool _writeDescription;
    [ObservableProperty] private bool _writeInfoJson;
    [ObservableProperty] private bool _writeThumbnail;
    [ObservableProperty] private bool _writeAllThumbnails;
    [ObservableProperty] private string _cacheDir = "";
    [ObservableProperty] private bool _noCacheDir;
    [ObservableProperty] private string _extraArgs = "";
    [ObservableProperty] private bool _checkEngineUpdatesOnStart = true;

    partial void OnIgnoreErrorsChanged(bool value)
    {
        if (value) AbortOnError = false;
    }

    partial void OnAbortOnErrorChanged(bool value)
    {
        if (value) IgnoreErrors = false;
    }
}

public partial class FormatSettings : ObservableObject
{
    /// <summary>Default quality preset: best, 2160, 1440, 1080, 720, 480, worst, audio.</summary>
    [ObservableProperty] private string _defaultQuality = "best";
    [ObservableProperty] private string _mergeOutputFormat = "";   // mkv, mp4, webm... (classic --merge-output-format)
    [ObservableProperty] private bool _preferFreeFormats;
    [ObservableProperty] private string _audioFormat = "best";      // classic --audio-format
    [ObservableProperty] private string _audioQuality = "0";    // classic --audio-quality
    [ObservableProperty] private string _recodeVideo = "";         // classic --recode-video
    [ObservableProperty] private bool _keepVideo;                  // classic -k
    [ObservableProperty] private bool _noPostOverwrites;
    [ObservableProperty] private bool _nativeLiveDash;
    [ObservableProperty] private string _liveAudioLanguages="";
    [ObservableProperty] private string _liveAudioRoles="";
    [ObservableProperty] private string _liveCaptionRoles="";
    [ObservableProperty] private bool _liveFollowQuality=true;
    [ObservableProperty] private bool _liveConvertCaptions=true;
    [ObservableProperty] private bool _liveNormalizeTransitions;
    [ObservableProperty] private bool _liveAdaptiveLadder;
    [ObservableProperty] private int _liveTransitionHeight=720;
    [ObservableProperty] private string _liveTransitionVideoCodec="auto";
    [ObservableProperty] private string _liveTransitionAudioCodec="auto";
    [ObservableProperty] private string _liveTransitionAudioLayout="preserve";
    [ObservableProperty] private int _liveTransitionAudioSampleRate=48000;
    [ObservableProperty] private bool _liveNormalizeGeometry;
    [ObservableProperty] private string _liveTransitionColorPolicy="preserve";
    [ObservableProperty] private int _liveHdrPeakNits=1000;
    [ObservableProperty] private string _liveTransitionCrop="";
    [ObservableProperty] private string _liveHdrSelectorPolicy="strict";
    partial void OnLiveNormalizeTransitionsChanged(bool value){if(!value)LiveAdaptiveLadder=false;}
}

public partial class SubtitleSettings : ObservableObject
{
    [ObservableProperty] private bool _writeSubs;
    [ObservableProperty] private bool _writeAutoSubs;
    [ObservableProperty] private bool _writeAllSubs;
    [ObservableProperty] private string _subLangs = "en";
    [ObservableProperty] private string _subFormat = "";           // e.g. "srt" or "ass/srt/best"
    [ObservableProperty] private string _convertSubs = "";         // srt | vtt | ass | lrc
    [ObservableProperty] private bool _embedSubs;
}

public partial class NetworkSettings : ObservableObject
{
    [ObservableProperty] private string _proxy = "";
    [ObservableProperty] private int _socketTimeout;
    [ObservableProperty] private string _sourceAddress = "";
    [ObservableProperty] private string _ipVersion = "auto";       // auto | 4 | 6
    [ObservableProperty] private bool _geoBypass = true;
    [ObservableProperty] private string _geoVerificationProxy = "";
    [ObservableProperty] private string _geoBypassCountry = "";
    [ObservableProperty] private string _geoBypassIpBlock = "";
    [ObservableProperty] private string _rateLimit = "";           // classic -r, e.g. "4.2M"
    [ObservableProperty] private int _retries = 10;
    [ObservableProperty] private int _fragmentRetries = 10;
    [ObservableProperty] private bool _skipUnavailableFragments = true;
    [ObservableProperty] private string _httpChunkSize = "";
    [ObservableProperty] private string _bufferSize = "";
    [ObservableProperty] private bool _noResizeBuffer;
    [ObservableProperty] private bool _playlistReverse;
    [ObservableProperty] private bool _playlistRandom;
    [ObservableProperty] private string _externalDownloader = "";  // aria2c, curl, wget...
    [ObservableProperty] private string _externalDownloaderArgs = "";
    [ObservableProperty] private int _sleepInterval;
    [ObservableProperty] private int _maxSleepInterval;
    [ObservableProperty] private bool _noCheckCertificate;
    [ObservableProperty] private string _userAgent = "";
    [ObservableProperty] private string _referer = "";
    [ObservableProperty] private string _customHeaders = "";       // one "Field:Value" per line
    [ObservableProperty] private int _concurrentFragments = 1;     // yt-dlp modernization
}

public partial class SelectionSettings : ObservableObject
{
    [ObservableProperty] private int _playlistStart;
    [ObservableProperty] private int _playlistEnd;
    [ObservableProperty] private string _playlistItems = "";
    [ObservableProperty] private string _matchTitle = "";
    [ObservableProperty] private string _rejectTitle = "";
    [ObservableProperty] private int _maxDownloads;
    [ObservableProperty] private string _minFilesize = "";
    [ObservableProperty] private string _maxFilesize = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _dateBefore = "";
    [ObservableProperty] private string _dateAfter = "";
    [ObservableProperty] private long _minViews;
    [ObservableProperty] private long _maxViews;
    [ObservableProperty] private string _matchFilter = "";
    [ObservableProperty] private int _ageLimit;
}

public partial class AuthSettings : ObservableObject
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _twoFactor = "";
    [ObservableProperty] private bool _useNetrc;
    [ObservableProperty] private string _videoPassword = "";
    [ObservableProperty] private string _apMso = "";
    [ObservableProperty] private string _apUsername = "";
    [ObservableProperty] private string _apPassword = "";
    [ObservableProperty] private string _cookiesFile = "";
    [ObservableProperty] private string _cookiesFromBrowser = "";  // modernization: chrome, edge, firefox...
}

public partial class PostProcessSettings : ObservableObject
{
    [ObservableProperty] private bool _embedThumbnail;
    [ObservableProperty] private bool _embedChapters = true;
    [ObservableProperty] private bool _addMetadata = true;
    [ObservableProperty] private string _parseMetadata = "";       // modernization of --metadata-from-title
    [ObservableProperty] private string _fixup = "";               // never | warn | detect_or_warn
    [ObservableProperty] private string _ffmpegLocation = "";
    [ObservableProperty] private string _postprocessorArgs = "";
    [ObservableProperty] private string _execCommand = "";         // classic --exec
    [ObservableProperty] private string _sponsorBlock = "none";    // modernization: none | mark | remove
}
