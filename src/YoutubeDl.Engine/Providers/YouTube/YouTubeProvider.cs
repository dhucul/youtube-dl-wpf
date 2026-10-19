using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Native YouTube metadata and exposed-stream extraction; advanced transports remain explicit capabilities.</summary>
public sealed partial class YouTubeProvider : IMediaProvider
{
    private sealed class BrowserFallbackState : IDisposable
    {
        internal bool UseMobile;
        public void Dispose() { }
    }
    private readonly IYouTubePlayerRuntime? _playerRuntime;
    private readonly IYouTubePlaybackSource? _playbackSource;
    public YouTubeProvider(IYouTubePlayerRuntime? playerRuntime = null, IYouTubePlaybackSource? playbackSource = null)
    { _playerRuntime = playerRuntime; _playbackSource = playbackSource; }

    public MediaProviderInfo Info { get; } = new("youtube", "YouTube videos, public playlists and channel uploads", 200);
    public NativeAuthenticationCapabilities AuthenticationCapabilities=>NativeAuthenticationCapabilities.CookieAccount|NativeAuthenticationCapabilities.OAuth;
    public bool CanHandle(Uri url) => YouTubeAddress.IsYouTubeHost(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        if (YouTubeSearchAddress.TryParse(request.Url,out _)||YouTubeChannelAddress.TryParse(request.Url, out _)) return null;
        var address = YouTubeAddress.Parse(request.Url);
        return address.VideoId is { } id && (address.PlaylistId is null || !request.DownloadPlaylist && !request.ExpandPlaylist)
            ? new("youtube", id) : null;
    }

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if(YouTubeSearchAddress.TryParse(request.Url,out var search))return await ExtractSearchAsync(search!,request,http,ct).ConfigureAwait(false);
        if(request.Authentication.OAuth is { } credentials)
        {
            var oauth=http.SessionState("oauth:youtube",()=>new Authentication.NativeOAuthSession(http,credentials));await oauth.EnsureAsync(ct).ConfigureAwait(false);
            if(!YouTubeChannelAddress.TryParse(request.Url,out _)){var selected=YouTubeAddress.Parse(request.Url);if(selected.PlaylistId is not null&&(selected.VideoId is null||request.ExpandPlaylist))return await YouTubeOAuthInventory.ReadAsync(selected.PlaylistId,request,oauth,ct).ConfigureAwait(false);}
        }
        if (YouTubeChannelAddress.TryParse(request.Url, out var channel))
            return await ExtractChannelAsync(channel!, request, http, ct).ConfigureAwait(false);
        var address = YouTubeAddress.Parse(request.Url);
        if (address.PlaylistId is not null && (address.VideoId is null || request.ExpandPlaylist))
            return await ExtractPlaylistAsync(address.PlaylistId, request, http, ct).ConfigureAwait(false);
        if (address.VideoId is null)
            throw new NotSupportedException("This YouTube address does not identify a video or playlist.");
        var account=http.SessionState("youtube:account",()=>new YouTubeAccountSession(http,request.Authentication.YouTubeAccountIndex));
        async Task<MediaDocument> ReadManifest(Uri url,CancellationToken token){using var controlled=http.CreateDiscoverySession(false);return await controlled.ReadPlaybackManifestAsync(url,null,request.Url.AbsoluteUri,token).ConfigureAwait(false);}
        var session=http.SessionState("youtube:video:"+address.VideoId,()=>new YouTubeVideoSession(token=>ExtractVideoAsync(request,http,address,account,token),ReadManifest));
        return session.Bind(await ExtractVideoAsync(request,http,address,account,ct).ConfigureAwait(false),account.Authenticated,http.CanControlRedirects);
    }
    private async Task<PageMetadata> ExtractVideoAsync(NativeDownloadRequest request,MediaHttp http,YouTubeAddress address,YouTubeAccountSession account,CancellationToken ct)
    {
        var watch=account.Select(address.WatchUrl!);var document=await account.ReadPageAsync(watch,ct).ConfigureAwait(false);
        if(document.Url!=watch)throw new InvalidDataException("YouTube redirected outside the selected watch context.");var html=document.Text;account.Observe(html,watch,ct);
        ct.ThrowIfCancellationRequested();
        PageMetadata page;
        try{page=YouTubePlayerParser.ParsePage(html,address.VideoId!,address.PlaylistId);}
        catch(InvalidOperationException)when(account.Authenticated){page=await account.PlayerAsync(address.VideoId!,ct).ConfigureAwait(false);}
        catch(InvalidOperationException)when(request.Authentication.OAuth is not null){throw new NotSupportedException("Google API sign-in does not grant private player access; select fresh YouTube account cookies for playback.");}
        // A fresh watch response already establishes the title. Do not start browser
        // playback for a video that this download job will reject.
        if (request.MetadataParseRules.Count == 0 && request.SelectionState?.Rejection(page, request, ct, incomplete: true) is not null) return page;
        if (request.MetadataInventoryOnly) return page;
        if (page.Media.Count == 0 && page.ReportedFormats.Any(format => format.Availability == FormatAvailability.AdaptiveTransportRequired))
        {
            var fallback = http.SessionState("youtube:browser-fallback:" + address.VideoId, () => new BrowserFallbackState());
            if (_playbackSource is not null && !request.Simulate && !request.SkipDownload && !fallback.UseMobile)
            {
                try
                {
                var observed = await _playbackSource.CaptureAsync(address.VideoId!, request, http.YouTubeCookies(ct), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (observed.Id != address.VideoId || observed.IsPlaylist || observed.Media.Count == 0 ||
                    observed.Media.Any(media => media.Sabr?.Session.VideoId != address.VideoId))
                    throw new InvalidDataException("The playback source returned a mismatched or unsupported YouTube session.");
                return observed with { PlaylistId = page.PlaylistId, Chapters = page.Chapters, PlayerScriptUrl = page.PlayerScriptUrl,
                    UploadDate = observed.UploadDate ?? page.UploadDate, UploadTimestamp = observed.UploadTimestamp ?? page.UploadTimestamp,
                    ViewCount = observed.ViewCount ?? page.ViewCount, AgeLimit = observed.AgeLimit ?? page.AgeLimit,
                    MetadataFields = observed.MetadataFields.Count > 0 ? observed.MetadataFields : page.MetadataFields };
                }
                catch (Exception ex) when (!account.Authenticated && ex is TimeoutException or System.Runtime.InteropServices.COMException)
                {
                    ct.ThrowIfCancellationRequested();
                    fallback.UseMobile = true;
                    page = page with { Warnings = page.Warnings.Append("The browser session could not start; trying YouTube's public mobile playback.").ToArray() };
                }
            }
            try
            {
                var mobile = account.Authenticated?await account.PlayerAsync(address.VideoId!,ct).ConfigureAwait(false):await YouTubeMobilePlayback.ReadAsync(address.VideoId!, request, http, ct).ConfigureAwait(false);
                if (mobile.Media.Count > 0) return mobile with { PlaylistId = page.PlaylistId, Chapters = page.Chapters, PlayerScriptUrl = page.PlayerScriptUrl,
                    UploadDate = mobile.UploadDate ?? page.UploadDate, UploadTimestamp = mobile.UploadTimestamp ?? page.UploadTimestamp,
                    ViewCount = mobile.ViewCount ?? page.ViewCount, AgeLimit = mobile.AgeLimit ?? page.AgeLimit,
                    MetadataFields = mobile.MetadataFields.Count > 0 ? mobile.MetadataFields : page.MetadataFields,
                    Subtitles = page.Subtitles.Count > 0 ? page.Subtitles : mobile.Subtitles };
            }
            catch (Exception ex) when (!account.Authenticated&&ex is InvalidDataException or NotSupportedException or InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
            {
                ct.ThrowIfCancellationRequested();
                page = page with { Warnings = page.Warnings.Append("The native mobile playback context could not be established for this video.").ToArray() };
            }
        }
        if (_playerRuntime is null || page.PlayerScriptUrl is null ||
            !page.ReportedFormats.Any(format => format.Availability == FormatAvailability.PlayerChallengeRequired)) return page;
        try
        {
            using var session = await NativePlayerScriptCache.CreateAsync(request, http, page.PlayerScriptUrl, _playerRuntime, !account.Authenticated, ct).ConfigureAwait(false);
            return YouTubePlayerParser.ParsePage(html, address.VideoId!, address.PlaylistId, session, ct);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or HttpRequestException)
        {
            ct.ThrowIfCancellationRequested();
            return page with { Warnings = page.Warnings.Append("The native player runtime could not resolve this player version; affected formats remain unavailable.").ToArray() };
        }
    }

    private static async Task<PageMetadata> ExtractPlaylistAsync(string id, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        var account=http.SessionState("youtube:account",()=>new YouTubeAccountSession(http,request.Authentication.YouTubeAccountIndex));
        var url=account.Select(new("https://www.youtube.com/playlist?list="+Uri.EscapeDataString(id)));var document=await account.ReadPageAsync(url,ct).ConfigureAwait(false);
        if(document.Url!=url)throw new InvalidDataException("YouTube redirected outside the selected playlist context.");var html=document.Text;account.Observe(html,url,ct);
        var page = YouTubePlaylistParser.Parse(YouTubePageJson.ReadInitialData(html), maximumEntries: request.MaximumPlaylistEntries);
        var entries = page.Entries.ToList();
        var title = page.Title ?? id;
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var continuation = page.Continuation;
        var versionMatch = Regex.Match(html, @"""INNERTUBE_CLIENT_VERSION""\s*:\s*""(?<value>[^""]+)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var version = versionMatch.Groups["value"].Value;
        while (continuation is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count >= request.MaximumPlaylistEntries) throw new InvalidDataException("The playlist exceeds the configured item limit; it was not truncated silently.");
            if (!tokens.Add(continuation)) throw new InvalidDataException("YouTube repeated a playlist continuation token.");
            if (string.IsNullOrWhiteSpace(version) || version.Length > 128) throw new InvalidDataException("The playlist response did not include a supported client version for pagination.");
            var body = new { context = new { client = new { clientName = "WEB", clientVersion = version } }, continuation };
            var json = await account.PostAsync("browse",body,"1",version,8*1024*1024,ct).ConfigureAwait(false);
            page = YouTubePlaylistParser.Parse(json, entries.Count + 1, request.MaximumPlaylistEntries - entries.Count);
            entries.AddRange(page.Entries);
            continuation = page.Continuation;
        }
        return new PageMetadata { Id = id, PlaylistId = id, IsPlaylist = true, Extractor = "youtube", Title = title, Entries = entries };
    }
}
