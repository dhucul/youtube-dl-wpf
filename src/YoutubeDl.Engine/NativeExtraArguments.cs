using System.Globalization;
using YoutubeDl.Engine.Download;

namespace YoutubeDl.Engine;

/// <summary>Maps supported extra arguments into typed native options without invoking a command processor.</summary>
public static class NativeExtraArguments
{
    private static Authentication.NativeHostingCredentials Hosting(NativeDownloadRequest request)=>request.Authentication.Hosting??new();
    private static Authentication.NativeOAuthCredentials OAuth(NativeDownloadRequest request)=>request.Authentication.OAuth??new(){Provider=Providers.YouTube.YouTubeAddress.IsYouTubeHost(request.Url)?Authentication.NativeOAuthProvider.YouTube:Providers.Vimeo.VimeoAddress.IsVimeoHost(request.Url)?Authentication.NativeOAuthProvider.Vimeo:throw new NotSupportedException("Native OAuth arguments require YouTube or Vimeo.")};
    public static NativeDownloadRequest Apply(NativeDownloadRequest request, IEnumerable<string> arguments)
    {
        request = NativeHttpHeaders.Normalize(request);
        var args = arguments.Take(1001).ToArray();
        if (args.Length > 1000 || args.Any(value => value is null || value.Length > 16384 || value.Any(char.IsControl)))
            throw new ArgumentException("The extra-argument list exceeds its limits or contains control characters.");
        for (var index = 0; index < args.Length; index++)
        {
            var equal = args[index].IndexOf('='); var flag = equal < 0 ? args[index] : args[index][..equal];
            if (!flag.StartsWith('-')) throw new NotSupportedException("Extra URLs and positional arguments require additional native support.");
            var inline = equal < 0 ? null : args[index][(equal + 1)..];
            if (equal < 0 && flag.StartsWith("-I", StringComparison.Ordinal) && flag.Length > 2) { inline = flag[2..]; flag = "-I"; }
            var takesValue = flag is "--sleep-requests" or "--sleep-subtitles" or "--downloader" or "--external-downloader" or "--downloader-args" or "--external-downloader-args" or "--exec" or "--sponsorblock-mark" or "--sponsorblock-remove" or "--fixup" or "--postprocessor-args" or "--ppa" or "--cache-dir" or "--sub-langs" or "--sub-format" or "--convert-subs" or "--convert-subtitles" or "--output" or "-o" or "--paths" or "-P" or "--download-archive" or
                "--xff" or "--geo-bypass-country" or "--geo-bypass-ip-block" or "--geo-verification-proxy" or "--cookies" or "--cookies-from-browser" or "--user-agent" or "--referer" or "--proxy" or "--source-address" or "--add-headers" or "--add-header" or "--socket-timeout" or "--limit-rate" or "-r" or
                "--retries" or "-R" or "--fragment-retries" or "--concurrent-fragments" or "-N" or "--playlist-items" or "-I" or "--playlist-start" or "--playlist-end" or "--match-title" or "--reject-title" or "--max-downloads" or "--recode-video" or "--remux-video" or "--merge-output-format" or "--format" or "-f" or
                "--date" or "--datebefore" or "--dateafter" or "--min-views" or "--max-views" or "--age-limit" or "--match-filter" or "--match-filters" or "--min-filesize" or "--max-filesize" or "--sleep-interval" or "--min-sleep-interval" or "--max-sleep-interval" or "--parse-metadata" or "--metadata-from-title" or "--buffer-size" or "--http-chunk-size" or "--audio-format" or "--audio-quality" or "--ffmpeg-location" or "--username" or "-u" or "--password" or "-p" or "--twofactor" or "--video-password" or "--netrc-location" or "--live-duration" or "--vimeo-access-token" or "--youtube-account-index" or "--oauth-client-id" or "--oauth-client-secret" or "--oauth-access-token" or "--oauth-refresh-token" or "--oauth-scopes" or "--oauth-redirect-uri" or "--hosting-provider" or "--hosting-account" or "--hosting-media" or "--hosting-access-token" or "--hosting-signing-key" or "--hosting-token-lifetime";
            if (!takesValue && inline is not null) throw new ArgumentException("The " + flag + " flag does not take a value.");
            string Value()
            {
                if (inline is not null) return inline;
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) && flag is not ("--playlist-items" or "-I")) throw new ArgumentException("The " + flag + " option needs a value.");
                return args[++index];
            }
            int Integer()
            {
                if (!int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("The " + flag + " option needs an integer.");
                return number;
            }
            long LongInteger()
            {
                if (!long.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("The " + flag + " option needs a whole-number count.");
                return number;
            }
            string DateValue()
            {
                var value = Value(); if (string.IsNullOrWhiteSpace(value)) throw new NativeSelectionException("The upload date option requires a value.");
                return value;
            }
            double Seconds()
            {
                if (!double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds < 0)
                    throw new ArgumentException("The " + flag + " option needs finite nonnegative seconds.");
                return seconds;
            }
            request = flag switch
            {
                "--geo-verification-proxy" => request with { GeoVerification = (request.GeoVerification ?? new()) with { Proxy = NativeGeoVerification.ProxyValue(Value()) } },
                "--xff" => request with { GeoVerification = NativeGeoVerification.WithMode(request.GeoVerification, Value()) },
                "--geo-bypass-country" => request with { GeoVerification = NativeGeoVerification.WithMode(request.GeoVerification, NativeGeoVerification.Country(Value())) },
                "--geo-bypass-ip-block" => request with { GeoVerification = NativeGeoVerification.WithMode(request.GeoVerification, NativeGeoVerification.IpBlock(Value())) },
                "--geo-bypass" => request with { GeoVerification = NativeGeoVerification.WithMode(request.GeoVerification, "default") },
                "--no-geo-bypass" => request with { GeoVerification = NativeGeoVerification.WithMode(request.GeoVerification, "never") },
                "--downloader" or "--external-downloader" => request with { ExternalDownloader = Value() },
                "--downloader-args" or "--external-downloader-args" => request with { ExternalDownloaderArguments = (request.ExternalDownloaderArguments ?? []).Append(Value()).ToArray() },
                "--hosting-provider" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {Provider=Value() switch {"wistia"=>Authentication.NativeHostingProvider.Wistia,"jwplatform"=>Authentication.NativeHostingProvider.JwPlatform,"brightcove"=>Authentication.NativeHostingProvider.Brightcove,"kaltura"=>Authentication.NativeHostingProvider.Kaltura,_=>throw new ArgumentException("Choose wistia, jwplatform, brightcove or kaltura for hosting credentials.")}}}},
                "--hosting-account" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {AccountId=Value()}}},
                "--hosting-media" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {MediaId=Value()}}},
                "--hosting-access-token" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {AccessToken=Value()}}},
                "--hosting-signing-key" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {SigningKey=Value()}}},
                "--hosting-token-lifetime" => request with {Authentication=request.Authentication with {Hosting=Hosting(request) with {TokenLifetimeSeconds=Integer()}}},
                "--oauth-client-id" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {ClientId=Value()}}},
                "--oauth-client-secret" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {ClientSecret=Value()}}},
                "--oauth-access-token" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {AccessToken=Value()}}},
                "--oauth-refresh-token" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {RefreshToken=Value()}}},
                "--oauth-scopes" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {Scopes=Value()}}},
                "--oauth-redirect-uri" => request with {Authentication=request.Authentication with {OAuth=OAuth(request) with {RedirectUri=new Uri(Value(),UriKind.Absolute)}}},
                "--oauth-login" => request with {Authentication=request.Authentication with {OAuthLogin=true}},
                "--no-oauth-login" => request with {Authentication=request.Authentication with {OAuthLogin=false}},
                "--vimeo-access-token" => request with { Authentication=request.Authentication with { AccessToken=Value() } },
                "--youtube-account-index" => request with { Authentication=request.Authentication with { YouTubeAccountIndex=Integer() } },
                "--username" or "-u" => request with { Authentication=request.Authentication with { Username=Value() } },
                "--password" or "-p" => request with { Authentication=request.Authentication with { Password=Value() } },
                "--twofactor" => request with { Authentication=request.Authentication with { TwoFactor=Value() } },
                "--video-password" => request with { Authentication=request.Authentication with { VideoPassword=Value() } },
                "--netrc-location" => request with { Authentication=request.Authentication with { NetrcLocation=Value(),UseNetrc=true } },
                "--live-duration" => request with { LiveDurationSeconds=Seconds() },
                "--live-from-start" => request with { LiveFromStart=true },
                "--no-live-from-start" => request with { LiveFromStart=false },
                "--netrc" or "-n" => request with { Authentication=request.Authentication with { UseNetrc=true } },
                "--no-netrc" => request with { Authentication=request.Authentication with { UseNetrc=false } },
                "--cache-dir" => request with { CacheDirectory = Value() is { Length: > 0 } cache ? cache : throw new ArgumentException("The cache directory option requires a path."), NoCache = false },
                "--no-cache-dir" => request with { NoCache = true },
                "--exec" => request with { ExecCommands = (request.ExecCommands ?? []).Concat([Value()]).ToArray() },
                "--no-exec" => request with { ExecCommands = null },
                "--sponsorblock-mark" => request with { SponsorBlockMark = Value() },
                "--sponsorblock-remove" => request with { SponsorBlockRemove = Value() },
                "--no-sponsorblock" => request with { SponsorBlockMark = null, SponsorBlockRemove = null },
                "--fixup" => request with { Fixup = Value() },
                "--postprocessor-args" or "--ppa" => request with { PostprocessorArguments = (request.PostprocessorArguments ?? []).Concat([Value()]).ToArray() },
                "--rm-cache-dir" => request with { RemoveCache = true },
                "--quiet" or "-q" => request with { Quiet = true },
                "--no-quiet" => request with { Quiet = false },
                "--no-progress" => request with { NoProgress = true },
                "--progress" => request with { NoProgress = false },
                "--no-warnings" => request with { NoWarnings = true },
                "--simulate" or "-s" => request with { Simulate = true },
                "--no-simulate" => request with { Simulate = false },
                "--skip-download" or "--no-download" => request with { SkipDownload = true },
                "--extract-audio" or "-x" => request with { AudioOnly = true, VideoOnly = false, RecodeVideo = "", Container = "" },
                "--audio-format" => request with { AudioFormat = Value() },
                "--audio-quality" => request with { AudioQuality = Value() },
                "--ffmpeg-location" => request with { FfmpegPath = FfmpegLocation(Value()) },
                "--ignore-no-formats-error" => request with { IgnoreNoFormatsError = true },
                "--no-ignore-no-formats-error" => request with { IgnoreNoFormatsError = false },
                "--force-write-archive" or "--force-download-archive" => request with { ForceWriteArchive = true },
                "--write-info-json" => request with { WriteInfoJson = true },
                "--no-write-info-json" => request with { WriteInfoJson = false },
                "--write-description" => request with { WriteDescription = true },
                "--no-write-description" => request with { WriteDescription = false },
                "--write-thumbnail" => request with { WriteThumbnail = true },
                "--write-all-thumbnails" => request with { WriteAllThumbnails = true },
                "--no-write-thumbnail" => request with { WriteThumbnail = false, WriteAllThumbnails = false },
                "--write-subs" => request with { WriteSubs = true },
                "--no-write-subs" => request with { WriteSubs = false },
                "--write-auto-subs" or "--write-automatic-subs" => request with { WriteAutoSubs = true },
                "--no-write-auto-subs" or "--no-write-automatic-subs" => request with { WriteAutoSubs = false },
                "--sub-langs" => request with { SubLanguages = Value() },
                "--all-subs" => request with { WriteSubs = true, SubLanguages = "all" },
                "--sub-format" => request with { SubFormat = Value() },
                "--convert-subs" or "--convert-subtitles" => request with { ConvertSubs = Value() },
                "--embed-subs" => request with { EmbedSubs = true },
                "--no-embed-subs" => request with { EmbedSubs = false },
                "--embed-thumbnail" => request with { EmbedThumbnail = true },
                "--no-embed-thumbnail" => request with { EmbedThumbnail = false },
                "--embed-metadata" or "--add-metadata" => request with { AddMetadata = true },
                "--parse-metadata" => request with { MetadataParseRules = request.MetadataParseRules.Concat([Value()]).ToArray() },
                "--metadata-from-title" => request with { MetadataParseRules = request.MetadataParseRules.Concat(["title:" + Value()]).ToArray() },
                "--no-embed-metadata" or "--no-add-metadata" => request with { AddMetadata = false },
                "--embed-chapters" or "--add-chapters" => request with { EmbedChapters = true },
                "--no-embed-chapters" or "--no-add-chapters" => request with { EmbedChapters = false },
                "--keep-video" or "-k" => request with { KeepOriginal = true },
                "--no-keep-video" => request with { KeepOriginal = false },
                "--no-overwrites" or "-w" => request with { NoOverwrites = true },
                "--force-overwrites" => request with { NoOverwrites = false, NoPostOverwrites = false, NoContinue = true },
                "--no-post-overwrites" => request with { NoPostOverwrites = true },
                "--post-overwrites" => request with { NoPostOverwrites = false },
                "--recode-video" => request with { RecodeVideo = Value(), Container = "" },
                "--remux-video" => request with { Container = Value(), RecodeVideo = "" },
                "--merge-output-format" => request with { MergeOutputFormat = Value() },
                "--allow-dynamic-mpd" or "--no-ignore-dynamic-mpd" => request with { AllowDynamicDash = null },
                "--ignore-dynamic-mpd" or "--no-allow-dynamic-mpd" => request with { AllowDynamicDash = false },
                "--prefer-free-formats" => request with { PreferFreeFormats = true },
                "--no-prefer-free-formats" => request with { PreferFreeFormats = false },
                "--continue" or "-c" => request with { NoContinue = false },
                "--no-continue" => request with { NoContinue = true },
                "--part" => request with { NoPart = false },
                "--no-part" => request with { NoPart = true },
                "--buffer-size" => request with { BufferSize = Value() },
                "--resize-buffer" => request with { NoResizeBuffer = false },
                "--no-resize-buffer" => request with { NoResizeBuffer = true },
                "--http-chunk-size" => request with { HttpChunkSize = Value() },
                "--mtime" => request with { NoMtime = false },
                "--no-mtime" => request with { NoMtime = true },
                "--restrict-filenames" => request with { RestrictFilenames = true },
                "--no-restrict-filenames" => request with { RestrictFilenames = false },
                "--no-playlist" => request with { DownloadPlaylist = false, ExpandPlaylist = false },
                "--yes-playlist" => request with { DownloadPlaylist = true, ExpandPlaylist = true },
                "--playlist-items" or "-I" => request with { PlaylistItems = Value() },
                "--playlist-start" => request with { PlaylistStart = Integer() },
                "--playlist-end" => request with { PlaylistEnd = Integer(), PlaylistEndExplicit = true },
                "--playlist-reverse" => request with { PlaylistReverse = true },
                "--no-playlist-reverse" => request with { PlaylistReverse = false },
                "--playlist-random" => request with { PlaylistRandom = true },
                "--no-playlist-random" => request with { PlaylistRandom = false },
                "--match-title" => request with { MatchTitle = Value() },
                "--reject-title" => request with { RejectTitle = Value() },
                "--max-downloads" => request with { MaxDownloads = Integer() },
                "--date" => request with { Date = DateValue() },
                "--datebefore" => request with { DateBefore = DateValue() },
                "--dateafter" => request with { DateAfter = DateValue() },
                "--min-views" => request with { MinimumViews = LongInteger() },
                "--max-views" => request with { MaximumViews = LongInteger() },
                "--age-limit" => request with { MaximumAge = Integer() },
                "--min-filesize" => request with { MinimumFileSize = Value() },
                "--max-filesize" => request with { MaximumFileSize = Value() },
                "--match-filter" or "--match-filters" => request with { MatchFilters = request.MatchFilters.Concat([Value()]).ToArray() },
                "--no-match-filter" or "--no-match-filters" => request with { MatchFilter = "", MatchFilters = [] },
                "--ignore-errors" or "-i" or "--no-abort-on-error" => request with { IgnoreErrors = true },
                "--abort-on-error" or "--no-ignore-errors" => request with { IgnoreErrors = false },
                "--output" or "-o" => request with { OutputTemplate = Value() },
                "--paths" or "-P" => request with { OutputDirectory = OutputPath(Value()) },
                "--download-archive" => request with { DownloadArchiveFile = Value() },
                "--no-download-archive" => request with { DownloadArchiveFile = "" },
                "--cookies" => request with { CookieFile = Value() },
                "--no-cookies" => request with { CookieFile = null },
                "--cookies-from-browser" => request with { BrowserCookies = Value() },
                "--no-cookies-from-browser" => request with { BrowserCookies = null },
                "--user-agent" => request with { UserAgent = Value() },
                "--referer" => request with { Referer = Value() },
                "--proxy" => request with { Proxy = Value() },
                "--source-address" => request with { SourceAddress = Value() },
                "--force-ipv4" or "-4" => request with { IpVersion = 4 },
                "--force-ipv6" or "-6" => request with { IpVersion = 6 },
                "--no-check-certificate" or "--no-check-certificates" => request with { NoCheckCertificate = true },
                "--check-certificate" or "--check-certificates" => request with { NoCheckCertificate = false },
                "--socket-timeout" => request with { TimeoutSeconds = Integer() },
                "--add-headers" or "--add-header" => NativeHttpHeaders.Normalize(request with { HttpHeaders = NativeHttpHeaders.Add(request.HttpHeaders, Value()) }),
                "--limit-rate" or "-r" => request with { RateLimit = Value() },
                "--retries" or "-R" => request with { Retries = Integer() },
                "--fragment-retries" => request with { FragmentRetries = Integer() },
                "--concurrent-fragments" or "-N" => request with { ConcurrentFragments = Integer() },
                "--sleep-interval" or "--min-sleep-interval" => request with { SleepIntervalSeconds = Seconds() },
                "--sleep-subtitles" => request with { SubtitleSleepSeconds = Seconds() },
                "--sleep-requests" => request with { RequestSleepSeconds = Seconds() },
                "--max-sleep-interval" => request with { MaximumSleepIntervalSeconds = Seconds() },
                "--format" or "-f" => request with { FormatSelector = Value(), FormatId = null },
                _ => throw new NotSupportedException("Native handling for extra argument " + flag + " is not implemented yet.")
            };
        }
        NativeExec.Validate(request.ExecCommands);
        NativeExternalDownloader.Validate(request.ExternalDownloader, request.ExternalDownloaderArguments);
        NativeGeoVerification.Validate(request.GeoVerification);
        NativeSponsorBlock.Validate(request.SponsorBlockMark, request.SponsorBlockRemove);
        return request;
    }
    private static string OutputPath(string path)
    {
        var colon = path.IndexOf(':');
        if (colon >= 0 && !(colon == 1 && char.IsAsciiLetter(path[0]) && path.Length > 2 && path[2] is '/' or '\\'))
            throw new NotSupportedException("Typed extra-argument output paths require additional native support. Use a plain output directory.");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("The output directory cannot be empty.");
        return path;
    }
    private static string FfmpegLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("The ffmpeg location requires a file or directory.");
        var full = Path.GetFullPath(path);
        return Directory.Exists(full) ? Path.Combine(full,OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg") : full;
    }
}
