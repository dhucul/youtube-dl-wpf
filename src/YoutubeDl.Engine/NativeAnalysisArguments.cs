namespace YoutubeDl.Engine;

/// <summary>Scans argument arity before projecting extraction/connection options into read-only analysis.</summary>
public static class NativeAnalysisArguments
{
    private static readonly HashSet<string> ConnectionValues=new(StringComparer.Ordinal)
    {"--cookies","--cookies-from-browser","--user-agent","--referer","--proxy","--source-address","--add-headers","--add-header","--socket-timeout","--retries","-R"};
    private static readonly HashSet<string> ConnectionFlags=new(StringComparer.Ordinal)
    {"--no-cookies","--no-cookies-from-browser","--force-ipv4","-4","--force-ipv6","-6","--no-check-certificate","--no-check-certificates","--check-certificate","--check-certificates"};
    private static readonly HashSet<string> DiscoveryValues=new(StringComparer.Ordinal)
    {"--sleep-requests","--cache-dir","--extractor-args","--extractor-retries","--js-runtimes","--remote-components","--impersonate","--xff","--geo-verification-proxy","--geo-bypass-country","--geo-bypass-ip-block",
        "--hosting-provider","--hosting-account","--hosting-media","--hosting-access-token","--hosting-signing-key","--hosting-token-lifetime","--oauth-client-id","--oauth-client-secret","--oauth-access-token","--oauth-refresh-token","--oauth-scopes","--oauth-redirect-uri","--vimeo-access-token","--youtube-account-index","--username","-u","--password","-p","--twofactor","--video-password","--ap-mso","--ap-username","--ap-password","--netrc-location","--netrc-cmd","--client-certificate","--client-certificate-key","--client-certificate-password"};
    private static readonly HashSet<string> DiscoveryFlags=new(StringComparer.Ordinal)
    {"--allow-dynamic-mpd","--no-ignore-dynamic-mpd","--ignore-dynamic-mpd","--no-allow-dynamic-mpd","--no-cache-dir","--oauth-login","--no-oauth-login","--netrc","-n","--no-netrc","--geo-bypass","--no-geo-bypass","--flat-playlist","--no-flat-playlist","--lazy-playlist","--no-lazy-playlist","--mark-watched","--no-mark-watched","--live-from-start","--no-live-from-start"};
    private static readonly HashSet<string> DownloadValues=new(StringComparer.Ordinal)
    {"--live-duration","--sub-langs","--sub-format","--convert-subs","--convert-subtitles","--output","-o","--paths","-P","--download-archive","--limit-rate","-r","--fragment-retries","--concurrent-fragments","-N",
        "--playlist-items","-I","--playlist-start","--playlist-end","--match-title","--reject-title","--max-downloads","--recode-video","--remux-video","--merge-output-format","--format","-f",
        "--date","--datebefore","--dateafter","--min-views","--max-views","--age-limit","--match-filter","--match-filters","--min-filesize","--max-filesize","--sleep-subtitles","--sleep-interval","--min-sleep-interval","--max-sleep-interval",
        "--parse-metadata","--metadata-from-title","--buffer-size","--http-chunk-size","--audio-format","--audio-quality","--ffmpeg-location","--exec","--postprocessor-args","--ppa","--fixup",
        "--sponsorblock-mark","--sponsorblock-remove","--sponsorblock-api","--sponsorblock-chapter-title","--downloader","--downloader-args","--external-downloader","--external-downloader-args","--batch-file","-a","--download-sections","--print","-O","--print-to-file","--output-na-placeholder","--progress-template","--format-sort","-S"};
    private static readonly HashSet<string> DownloadFlags=new(StringComparer.Ordinal)
    {"--simulate","-s","--no-simulate","--skip-download","--no-download","--extract-audio","-x","--ignore-no-formats-error","--no-ignore-no-formats-error","--force-write-archive","--force-download-archive",
        "--write-info-json","--no-write-info-json","--write-description","--no-write-description","--write-thumbnail","--write-all-thumbnails","--no-write-thumbnail","--write-subs","--no-write-subs","--write-auto-subs","--write-automatic-subs","--no-write-auto-subs","--no-write-automatic-subs","--all-subs",
        "--embed-subs","--no-embed-subs","--embed-thumbnail","--no-embed-thumbnail","--embed-metadata","--add-metadata","--no-embed-metadata","--no-add-metadata","--embed-chapters","--add-chapters","--no-embed-chapters","--no-add-chapters",
        "--keep-video","-k","--no-keep-video","--no-overwrites","-w","--force-overwrites","--no-force-overwrites","--no-post-overwrites","--post-overwrites","--prefer-free-formats","--no-prefer-free-formats","--continue","-c","--no-continue","--part","--no-part","--resize-buffer","--no-resize-buffer","--mtime","--no-mtime","--restrict-filenames","--no-restrict-filenames",
        "--no-playlist","--yes-playlist","--playlist-reverse","--no-playlist-reverse","--playlist-random","--no-playlist-random","--no-match-filter","--no-match-filters","--ignore-errors","-i","--no-abort-on-error","--abort-on-error","--no-ignore-errors","--no-download-archive","--verbose","-v","--quiet","-q","--no-quiet","--no-warnings","--no-progress","--progress","--newline","--rm-cache-dir","--no-exec","--no-sponsorblock","--skip-unavailable-fragments","--abort-on-unavailable-fragments","--no-skip-unavailable-fragments","--no-abort-on-unavailable-fragments"};
    public static IReadOnlyList<string> Select(IEnumerable<string> arguments)
    {
        var args=arguments.Take(1001).ToArray();
        if(args.Length>1000||args.Any(value=>value is null||value.Length>16384||value.Any(char.IsControl)))throw new ArgumentException("Analysis arguments exceed their limits or contain control characters.");
        var result=new List<string>();
        for(var index=0;index<args.Length;index++)
        {
            var text=args[index];var equal=text.IndexOf('=');var flag=equal<0?text:text[..equal];var inline=equal<0?null:text[(equal+1)..];
            if(equal<0&&flag.StartsWith("-I",StringComparison.Ordinal)&&flag.Length>2){inline=flag[2..];flag="-I";}
            var keep=ConnectionValues.Contains(flag)||ConnectionFlags.Contains(flag)||DiscoveryValues.Contains(flag)||DiscoveryFlags.Contains(flag);
            var takesValue=ConnectionValues.Contains(flag)||DiscoveryValues.Contains(flag)||DownloadValues.Contains(flag);
            var known=takesValue||ConnectionFlags.Contains(flag)||DiscoveryFlags.Contains(flag)||DownloadFlags.Contains(flag);
            if(!known)throw new NotSupportedException("Native analysis handling for this extra argument is not implemented.");
            string? value=null;
            if(takesValue)
            {
                if(inline is not null)value=inline;
                else if(index+1<args.Length)value=args[++index];
                else throw new ArgumentException("An analysis extra argument is missing its value.");
                // Download-only values are opaque. They cannot inject a later connection flag.
                if(keep&&value.StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("An analysis connection option is missing its value.");
            }
            else if(inline is not null)throw new ArgumentException("An analysis flag does not take a value.");
            if(keep){result.Add(flag);if(value is not null)result.Add(value);}
            // --print-to-file takes a template and a path; both remain download-only.
            if(flag=="--print-to-file"){if(index+1>=args.Length)throw new ArgumentException("The print-to-file option requires a path.");index++;}
        }
        return result.AsReadOnly();
    }
}
