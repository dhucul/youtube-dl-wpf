using YoutubeDlModern.Models;

namespace YoutubeDlModern.Services;

/// <summary>
/// Translates the GUI settings (which mirror classic youtube-dl's option groups)
/// into yt-dlp command-line arguments. yt-dlp accepts virtually every youtube-dl
/// flag, so full feature parity is preserved; a few dead options (annotations,
/// avconv, call-home) map to their modern equivalents or the custom-args box.
/// </summary>
public static class ArgsBuilder
{
    private const int MaximumPreviewPlaylistEntries = 500;
    public const string ProgressPrefix = "YTPROG|";
    public const string PostProcessMarker = "YTPOST";
    public const string FinalFilePrefix = "YTFILE|";

    private const string DownloadProgressTemplate =
        "download:YTPROG|%(progress.downloaded_bytes)s|%(progress.total_bytes,total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(progress.status)s";

    private const string PostProcessTemplate =
        "postprocess:YTPOST|%(progress.status)s";

    /// <summary>Args for metadata fetch. Quick and playlist modes stay flat; full mode includes formats.</summary>
    public static List<string> BuildInfoArgs(AppSettings s, string url, AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions
        {
            CookiesFile = s.Auth.CookiesFile,
            CookiesFromBrowser = s.Auth.CookiesFromBrowser
        };
        var a = new List<string>();
        AddConnectionArgs(a, s);
        AddAuthArgs(a, s, options.CookiesFile, options.CookiesFromBrowser, overrideCookies: true);
        a.AddRange(ProcessRunner.SplitArguments(s.General.ExtraArgs));
        // Never recursively expand every entry during the preview request. Full mode
        // still performs format checks for a video; quick mode skips those checks.
        // Safety flags are deliberately appended after custom arguments so they cannot
        // be overridden by a hand-edited configuration.
        a.Add("--flat-playlist");
        a.Add("--playlist-end");
        a.Add(MaximumPreviewPlaylistEntries.ToString());
        if (string.Equals(options.Mode, "quick", StringComparison.OrdinalIgnoreCase))
            a.Add("--no-check-formats");
        if (string.Equals(options.Mode, "playlist", StringComparison.OrdinalIgnoreCase))
            a.Add("--yes-playlist");
        a.Add("-J");
        a.Add("--no-warnings");
        a.Add(url);
        return a;
    }

    /// <summary>Args for retrieving structured formats for one video or the first playlist entry.</summary>
    public static List<string> BuildListFormatsArgs(
        AppSettings s,
        string url,
        AnalysisOptions? options = null,
        bool isPlaylist = false)
    {
        options ??= new AnalysisOptions
        {
            CookiesFile = s.Auth.CookiesFile,
            CookiesFromBrowser = s.Auth.CookiesFromBrowser
        };
        var a = new List<string>();
        AddConnectionArgs(a, s);
        AddAuthArgs(a, s, options.CookiesFile, options.CookiesFromBrowser, overrideCookies: true);
        a.AddRange(ProcessRunner.SplitArguments(s.General.ExtraArgs));
        if (isPlaylist)
        {
            // A format listing is meaningful per entry. Bound playlist work so the
            // button cannot spend minutes traversing every item.
            a.Add("--yes-playlist");
            a.Add("--playlist-items");
            a.Add("1");
        }
        else
        {
            a.Add("--no-playlist");
        }
        a.Add("-J");
        a.Add("--no-warnings");
        a.Add(url);
        return a;
    }

    /// <summary>Full argument set for an actual download.</summary>
    public static List<string> BuildDownloadArgs(AppSettings s, DownloadOptions o, string url, string? ffmpegLocation)
    {
        var a = new List<string>();

        // --- Machine-readable progress / final-path reporting ---
        a.Add("--newline");
        a.Add("--progress");
        a.Add("--progress-template"); a.Add(DownloadProgressTemplate);
        a.Add("--progress-template"); a.Add(PostProcessTemplate);
        a.Add("--print"); a.Add($"after_move:{FinalFilePrefix}%(filepath)s");

        AddConnectionArgs(a, s);
        AddAuthArgs(a, s, o.CookiesFile, o.CookiesFromBrowser, overrideCookies: true);

        var g = s.General;
        var f = s.Formats;
        var sub = s.Subtitles;
        var net = s.Network;
        var sel = s.Selection;
        var post = s.PostProcess;

        // --- Errors / playlists ---
        if (g.AbortOnError) a.Add("--abort-on-error");
        else if (g.IgnoreErrors) a.Add("--ignore-errors");
        if (!o.DownloadPlaylist) a.Add("--no-playlist");
        if (!string.IsNullOrWhiteSpace(g.DownloadArchive)) { a.Add("--download-archive"); a.Add(g.DownloadArchive); }

        // --- Download behavior ---
        if (!string.IsNullOrWhiteSpace(o.RateLimit)) { a.Add("-r"); a.Add(o.RateLimit); }
        if (o.Retries >= 0) { a.Add("-R"); a.Add(o.Retries.ToString()); }
        if (net.FragmentRetries > 0) { a.Add("--fragment-retries"); a.Add(net.FragmentRetries.ToString()); }
        if (!net.SkipUnavailableFragments) a.Add("--abort-on-unavailable-fragment");
        if (!string.IsNullOrWhiteSpace(net.BufferSize)) { a.Add("--buffer-size"); a.Add(net.BufferSize); }
        if (net.NoResizeBuffer) a.Add("--no-resize-buffer");
        if (!string.IsNullOrWhiteSpace(net.HttpChunkSize)) { a.Add("--http-chunk-size"); a.Add(net.HttpChunkSize); }
        if (net.PlaylistReverse) a.Add("--playlist-reverse");
        if (net.PlaylistRandom) a.Add("--playlist-random");
        if (!string.IsNullOrWhiteSpace(net.ExternalDownloader))
        {
            a.Add("--downloader"); a.Add(net.ExternalDownloader);
            if (!string.IsNullOrWhiteSpace(net.ExternalDownloaderArgs))
            {
                a.Add("--downloader-args");
                a.Add($"{net.ExternalDownloader}:{net.ExternalDownloaderArgs}");
            }
        }
        if (o.ConcurrentFragments > 1) { a.Add("-N"); a.Add(o.ConcurrentFragments.ToString()); }

        // --- Filesystem ---
        var outDir = !string.IsNullOrWhiteSpace(o.OutputDirectory) ? o.OutputDirectory : g.DownloadDirectory;
        a.Add("-P"); a.Add(outDir);
        var outputTemplate = !string.IsNullOrWhiteSpace(o.OutputTemplate) ? o.OutputTemplate : g.OutputTemplate;
        if (!string.IsNullOrWhiteSpace(outputTemplate)) { a.Add("-o"); a.Add(outputTemplate); }
        if (g.RestrictFilenames) a.Add("--restrict-filenames");
        if (g.NoOverwrites) a.Add("--no-overwrites");
        if (g.NoContinue) a.Add("--no-continue");
        if (g.NoPart) a.Add("--no-part");
        if (g.NoMtime) a.Add("--no-mtime");
        if (g.WriteDescription) a.Add("--write-description");
        if (g.WriteInfoJson) a.Add("--write-info-json");
        if (g.WriteAllThumbnails) a.Add("--write-all-thumbnails");
        else if (o.WriteThumbnail) a.Add("--write-thumbnail");
        if (!string.IsNullOrWhiteSpace(g.CacheDir)) { a.Add("--cache-dir"); a.Add(g.CacheDir); }
        if (g.NoCacheDir) a.Add("--no-cache-dir");

        // --- Format selection (classic -f / modern presets) ---
        var formatSelector = BuildFormatSelector(
            o.QualityPreset, o.CustomFormat, o.DownloadMode, o.VideoCodec, o.MaxFps);
        if (!string.IsNullOrWhiteSpace(formatSelector)) { a.Add("-f"); a.Add(formatSelector); }
        var hasExplicitContainer = !string.IsNullOrWhiteSpace(o.Container) && o.Container != "auto";
        var container = hasExplicitContainer ? o.Container : f.MergeOutputFormat;
        if (!string.IsNullOrWhiteSpace(container) && o.DownloadMode != "audio-only")
        {
            a.Add("--merge-output-format"); a.Add(container);
            if (hasExplicitContainer)
            {
                a.Add("--remux-video"); a.Add(container);
            }
        }
        if (f.PreferFreeFormats) a.Add("--prefer-free-formats");

        // --- Audio extraction (classic -x) ---
        if (o.DownloadMode == "audio-only" || o.QualityPreset == "audio")
        {
            a.Add("-x");
            a.Add("--audio-format"); a.Add(string.IsNullOrWhiteSpace(o.AudioFormat) ? "mp3" : o.AudioFormat);
            if (!string.IsNullOrWhiteSpace(o.AudioQuality)) { a.Add("--audio-quality"); a.Add(o.AudioQuality); }
        }

        if (o.DownloadMode != "audio-only" && o.QualityPreset != "audio" &&
            !hasExplicitContainer &&
            !string.IsNullOrWhiteSpace(f.RecodeVideo))
        {
            a.Add("--recode-video"); a.Add(f.RecodeVideo);
        }
        if (f.KeepVideo) a.Add("-k");
        if (f.NoPostOverwrites) a.Add("--no-post-overwrites");

        // --- Subtitles ---
        var wantSubs = o.WriteSubs || o.WriteAutoSubs || sub.WriteAllSubs || o.EmbedSubs;
        if (sub.WriteAllSubs) a.Add("--all-subs");
        if (o.WriteSubs || o.EmbedSubs) a.Add("--write-subs");
        if (o.WriteAutoSubs) a.Add("--write-auto-subs");
        if (wantSubs)
        {
            var langs = !string.IsNullOrWhiteSpace(o.SubLangs) ? o.SubLangs : sub.SubLangs;
            if (!string.IsNullOrWhiteSpace(langs) && !sub.WriteAllSubs) { a.Add("--sub-langs"); a.Add(langs); }
            if (!string.IsNullOrWhiteSpace(sub.SubFormat)) { a.Add("--sub-format"); a.Add(sub.SubFormat); }
        }
        if (!string.IsNullOrWhiteSpace(sub.ConvertSubs)) { a.Add("--convert-subs"); a.Add(sub.ConvertSubs); }
        if (o.EmbedSubs) a.Add("--embed-subs");

        // --- Video selection filters ---
        var playlistItems = o.PlaylistItems;
        if (!string.IsNullOrWhiteSpace(playlistItems)) { a.Add("--playlist-items"); a.Add(playlistItems); }
        if (o.PlaylistStart > 0) { a.Add("--playlist-start"); a.Add(o.PlaylistStart.ToString()); }
        if (o.PlaylistEnd > 0) { a.Add("--playlist-end"); a.Add(o.PlaylistEnd.ToString()); }
        if (!string.IsNullOrWhiteSpace(sel.MatchTitle)) { a.Add("--match-title"); a.Add(sel.MatchTitle); }
        if (!string.IsNullOrWhiteSpace(sel.RejectTitle)) { a.Add("--reject-title"); a.Add(sel.RejectTitle); }
        if (sel.MaxDownloads > 0) { a.Add("--max-downloads"); a.Add(sel.MaxDownloads.ToString()); }
        if (!string.IsNullOrWhiteSpace(sel.MinFilesize)) { a.Add("--min-filesize"); a.Add(sel.MinFilesize); }
        if (!string.IsNullOrWhiteSpace(sel.MaxFilesize)) { a.Add("--max-filesize"); a.Add(sel.MaxFilesize); }
        if (!string.IsNullOrWhiteSpace(sel.Date)) { a.Add("--date"); a.Add(sel.Date); }
        if (!string.IsNullOrWhiteSpace(sel.DateBefore)) { a.Add("--datebefore"); a.Add(sel.DateBefore); }
        if (!string.IsNullOrWhiteSpace(sel.DateAfter)) { a.Add("--dateafter"); a.Add(sel.DateAfter); }
        if (sel.MinViews > 0) { a.Add("--min-views"); a.Add(sel.MinViews.ToString()); }
        if (sel.MaxViews > 0) { a.Add("--max-views"); a.Add(sel.MaxViews.ToString()); }
        if (!string.IsNullOrWhiteSpace(sel.MatchFilter)) { a.Add("--match-filter"); a.Add(sel.MatchFilter); }
        if (sel.AgeLimit > 0) { a.Add("--age-limit"); a.Add(sel.AgeLimit.ToString()); }

        // --- Post-processing ---
        if (o.EmbedThumbnail) a.Add("--embed-thumbnail");
        if (o.EmbedChapters) a.Add("--embed-chapters");
        else a.Add("--no-embed-chapters");
        a.Add(o.AddMetadata ? "--add-metadata" : "--no-add-metadata");
        if (!string.IsNullOrWhiteSpace(post.ParseMetadata)) { a.Add("--parse-metadata"); a.Add(post.ParseMetadata); }
        if (!string.IsNullOrWhiteSpace(post.Fixup)) { a.Add("--fixup"); a.Add(post.Fixup); }
        // The resolver already validates the configured path and falls back to the
        // managed installation or PATH. Never reselect an invalid configured value.
        if (!string.IsNullOrWhiteSpace(ffmpegLocation))
        {
            a.Add("--ffmpeg-location");
            a.Add(ffmpegLocation);
        }
        if (!string.IsNullOrWhiteSpace(post.PostprocessorArgs)) { a.Add("--postprocessor-args"); a.Add(post.PostprocessorArgs); }
        if (!string.IsNullOrWhiteSpace(post.ExecCommand)) { a.Add("--exec"); a.Add(post.ExecCommand); }
        if (post.SponsorBlock == "mark") { a.Add("--sponsorblock-mark"); a.Add("all"); }
        else if (post.SponsorBlock == "remove") { a.Add("--sponsorblock-remove"); a.Add("all"); }

        // --- Free-form passthrough (guarantees nothing is unreachable) ---
        a.AddRange(ProcessRunner.SplitArguments(g.ExtraArgs));
        a.AddRange(ProcessRunner.SplitArguments(o.ExtraArgs));

        a.Add(url);
        return a;
    }

    /// <summary>Maps a quality preset to a yt-dlp format selector string.</summary>
    public static string BuildFormatSelector(string preset, string customFormat)
        => BuildFormatSelector(preset, customFormat, preset == "audio" ? "audio-only" : "video-audio", "any", 0);

    public static string BuildFormatSelector(
        string preset,
        string customFormat,
        string downloadMode,
        string videoCodec,
        int maxFps)
    {
        if (preset == "custom") return customFormat;
        if (downloadMode == "audio-only" || preset == "audio") return "ba/b";

        var filters = new List<string>();
        if (preset is "2160" or "1440" or "1080" or "720" or "480" or "360")
            filters.Add($"height<={preset}");
        if (maxFps > 0)
            filters.Add($"fps<={maxFps}");

        var codecFilter = videoCodec switch
        {
            "h264" => "vcodec^=avc",
            "h265" => "vcodec~='^(hev|hvc)'",
            "vp9" => "vcodec^=vp0",
            "av1" => "vcodec^=av01",
            _ => ""
        };
        if (codecFilter.Length > 0) filters.Add(codecFilter);

        var suffix = string.Concat(filters.Select(filter => $"[{filter}]"));
        if (downloadMode == "video-only")
            return preset == "worst" ? $"wv{suffix}" : $"bv{suffix}";
        return preset == "worst"
            ? $"wv*{suffix}+wa/w{suffix}"
            : $"bv*{suffix}+ba/b{suffix}";
    }

    // Proxy / geo / headers / workarounds — needed by metadata fetch and downloads alike.
    private static void AddConnectionArgs(List<string> a, AppSettings s)
    {
        var net = s.Network;

        if (!string.IsNullOrWhiteSpace(net.Proxy)) { a.Add("--proxy"); a.Add(net.Proxy); }
        if (net.SocketTimeout > 0) { a.Add("--socket-timeout"); a.Add(net.SocketTimeout.ToString()); }
        if (!string.IsNullOrWhiteSpace(net.SourceAddress)) { a.Add("--source-address"); a.Add(net.SourceAddress); }
        if (net.IpVersion == "4") a.Add("-4");
        else if (net.IpVersion == "6") a.Add("-6");

        if (!net.GeoBypass) a.Add("--no-geo-bypass");
        if (!string.IsNullOrWhiteSpace(net.GeoVerificationProxy)) { a.Add("--geo-verification-proxy"); a.Add(net.GeoVerificationProxy); }
        if (!string.IsNullOrWhiteSpace(net.GeoBypassCountry)) { a.Add("--geo-bypass-country"); a.Add(net.GeoBypassCountry); }
        if (!string.IsNullOrWhiteSpace(net.GeoBypassIpBlock)) { a.Add("--geo-bypass-ip-block"); a.Add(net.GeoBypassIpBlock); }

        if (net.SleepInterval > 0) { a.Add("--sleep-interval"); a.Add(net.SleepInterval.ToString()); }
        if (net.MaxSleepInterval > 0) { a.Add("--max-sleep-interval"); a.Add(net.MaxSleepInterval.ToString()); }
        if (net.NoCheckCertificate) a.Add("--no-check-certificate");

        // Default browser-like User-Agent prevents YouTube from returning 403 Forbidden
        // when no custom agent is configured. The user can still override it in settings.
        var userAgent = !string.IsNullOrWhiteSpace(net.UserAgent)
            ? net.UserAgent
            : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        a.Add("--user-agent"); a.Add(userAgent);

        if (!string.IsNullOrWhiteSpace(net.Referer)) { a.Add("--referer"); a.Add(net.Referer); }
        foreach (var line in net.CustomHeaders.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains(':'))
            {
                a.Add("--add-headers");
                a.Add(line);
            }
        }

        // YouTube frequently returns 403 Forbidden when the default "web" client is
        // blocked. The iOS client uses a different, less-restricted API endpoint and
        // is tried first. Android, web_embedded, and tv provide additional fallbacks.
        // player_skip configs avoid webpage requests that can also trigger 403s.
        // These extractor args are safe to pass for non-YouTube sites — yt-dlp
        // ignores unknown extractor arguments.
        a.Add("--extractor-args");
        a.Add("youtube:player_client=ios,android,web_embedded,tv;player_skip=webpage,configs");
        a.Add("--extractor-retries");
        a.Add("5");
    }

    private static void AddAuthArgs(
        List<string> a,
        AppSettings s,
        string? cookiesFile = null,
        string? cookiesFromBrowser = null,
        bool overrideCookies = false)
    {
        var auth = s.Auth;

        if (auth.UseNetrc) a.Add("-n");
        if (!string.IsNullOrWhiteSpace(auth.Username)) { a.Add("-u"); a.Add(auth.Username); }
        if (!string.IsNullOrWhiteSpace(auth.Password)) { a.Add("-p"); a.Add(auth.Password); }
        if (!string.IsNullOrWhiteSpace(auth.TwoFactor)) { a.Add("--twofactor"); a.Add(auth.TwoFactor); }
        if (!string.IsNullOrWhiteSpace(auth.VideoPassword)) { a.Add("--video-password"); a.Add(auth.VideoPassword); }
        if (!string.IsNullOrWhiteSpace(auth.ApMso)) { a.Add("--ap-mso"); a.Add(auth.ApMso); }
        if (!string.IsNullOrWhiteSpace(auth.ApUsername)) { a.Add("--ap-username"); a.Add(auth.ApUsername); }
        if (!string.IsNullOrWhiteSpace(auth.ApPassword)) { a.Add("--ap-password"); a.Add(auth.ApPassword); }
        var selectedCookiesFile = overrideCookies ? cookiesFile : auth.CookiesFile;
        var selectedBrowser = overrideCookies ? cookiesFromBrowser : auth.CookiesFromBrowser;
        if (!string.IsNullOrWhiteSpace(selectedCookiesFile))
        {
            a.Add("--cookies");
            a.Add(selectedCookiesFile);
        }
        else if (!string.IsNullOrWhiteSpace(selectedBrowser) && selectedBrowser != "none")
        {
            a.Add("--cookies-from-browser");
            a.Add(selectedBrowser);
        }
    }
}
