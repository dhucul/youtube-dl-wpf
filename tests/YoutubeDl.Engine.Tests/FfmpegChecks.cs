using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

internal static partial class FfmpegChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> ProcessChecks =>
        [("Canceled ffmpeg operations kill and drain their child process", Cancellation)];
    public static IEnumerable<(string Name, Func<Task> Test)> MediaChecks(string ffmpeg) =>
    [
        ("Playlist captions media Nowness membership order loads only selected fresh story",()=>PlaylistCaptionMedia(ffmpeg,"nowness")),
        ("Playlist captions media Nowness inspected playlist retains selected identity",()=>PlaylistCaptionMedia(ffmpeg,"snapshot")),
        ("Playlist captions media private Nowness playlists preserve prior publication",()=>PlaylistCaptionMedia(ffmpeg,"private")),
        ("Playlist captions media legacy Wistia resolves selected fresh assets",()=>PlaylistCaptionMedia(ffmpeg,"legacy")),
        ("Playlist captions media legacy page declaration delegates a complete playlist",()=>PlaylistCaptionMedia(ffmpeg,"embed")),
        ("Playlist captions media protected legacy members preserve prior publication",()=>PlaylistCaptionMedia(ffmpeg,"protected")),
        ("Playlist captions media Wistia VTT saves and embeds every cue",()=>PlaylistCaptionMedia(ffmpeg,"caption")),
        ("Playlist captions media Wistia captions convert to SRT with original timing",()=>PlaylistCaptionMedia(ffmpeg,"convert")),
        ("Playlist captions media automatic caption requests retain declared provenance",()=>PlaylistCaptionMedia(ffmpeg,"auto")),
        ("Playlist captions media caption API bearer stays scoped and content stays job-owned",()=>PlaylistCaptionMedia(ffmpeg,"api")),
        ("Playlist captions media signed caption rejection renews exact media-language resource",()=>PlaylistCaptionMedia(ffmpeg,"renew")),
        ("Playlist captions media disappeared captions cannot publish success",()=>PlaylistCaptionMedia(ffmpeg,"revoked")),
        ("Playlist captions media caption HTML protects prior files and archive",()=>PlaylistCaptionMedia(ffmpeg,"html")),
        ("Playlist captions media cancelled playlist jobs protect prior files",()=>PlaylistCaptionMedia(ffmpeg,"cancel")),
        ("Playlist captions media caption-only jobs transfer no media",()=>PlaylistCaptionMedia(ffmpeg,"caption-only")),
        ("Hosting collections media Nowness story retains complete public sources",()=>HostingCollectionMedia(ffmpeg,"story")),
        ("Hosting collections media Nowness series loads only the chosen story",()=>HostingCollectionMedia(ffmpeg,"series")),
        ("Hosting collections media Wistia channel resolves fresh selected playback",()=>HostingCollectionMedia(ffmpeg,"wistia")),
        ("Hosting collections media JW paging retains selected source position",()=>HostingCollectionMedia(ffmpeg,"jw")),
        ("Hosting collections media Brightcove paging retains selected source position",()=>HostingCollectionMedia(ffmpeg,"bc")),
        ("Hosting collections media protected Wistia entries preserve prior output",()=>HostingCollectionMedia(ffmpeg,"protected")),
        ("Hosting collections media repeated JW positions preserve per-video archive semantics",()=>HostingCollectionMedia(ffmpeg,"duplicate")),
        ("Hosting collections media duplicate JW occurrence renews its own playback",()=>HostingCollectionMedia(ffmpeg,"jw-renew")),
        ("Hosting collections media Brightcove playlist renewal keeps issuer credentials scoped",()=>HostingCollectionMedia(ffmpeg,"bc-renew")),
        ("Hosting collections media failed later collection pages cannot publish files",()=>HostingCollectionMedia(ffmpeg,"foreign")),
        ("Hosting collections media changed delegated story identity protects publication",()=>HostingCollectionMedia(ffmpeg,"changed")),
        ("Consumer DASH media Business Insider delegation retains complete source",()=>ConsumerDashMedia(ffmpeg,"bi")),
        ("Consumer DASH media Al Jazeera delegation retains complete source",()=>ConsumerDashMedia(ffmpeg,"aj")),
        ("Consumer DASH media scoped consumer signing stays at the issuer",()=>ConsumerDashMedia(ffmpeg,"bi-signed")),
        ("Consumer DASH media changed primary video preserves prior files",()=>ConsumerDashMedia(ffmpeg,"bi-switched")),
        ("Consumer DASH media paid consumer pages preserve prior files",()=>ConsumerDashMedia(ffmpeg,"bi-paid")),
        ("Consumer DASH media Kaltura chooses a fresh offered profile before transfer",()=>ConsumerDashMedia(ffmpeg,"k-auto")),
        ("Consumer DASH media explicit stale Kaltura profiles do not silently switch",()=>ConsumerDashMedia(ffmpeg,"k-explicit")),
        ("Consumer DASH media all stale Kaltura profiles stop before media",()=>ConsumerDashMedia(ffmpeg,"k-all-expired")),
        ("Consumer DASH media CDN denials do not switch after selected playback",()=>ConsumerDashMedia(ffmpeg,"k-denied")),
        ("Consumer DASH media valid selected Kaltura graphs can renew during transfer",()=>ConsumerDashMedia(ffmpeg,"k-renew")),
        ("Consumer DASH media TS metadata retains reordered picture timing",()=>ConsumerTransportTiming(ffmpeg,true)),
        ("Consumer DASH media TS remux retains reordered picture timing",()=>ConsumerTransportTiming(ffmpeg,false)),
        ("Shared hosting media Wistia API renewal verifies full source packets",()=>SharedHostingMedia(ffmpeg,"wistia-progressive")),
        ("Shared hosting media JW token renewal verifies full source packets",()=>SharedHostingMedia(ffmpeg,"jw-progressive")),
        ("Shared hosting media Brightcove JWT captions renew and embed",()=>SharedHostingMedia(ffmpeg,"brightcove-progressive")),
        ("Shared hosting media Kaltura supplied-session renewal retains complete media",()=>SharedHostingMedia(ffmpeg,"kaltura-progressive")),
        ("Shared hosting media Wistia API HLS renews complete separate tracks",()=>SharedHostingMedia(ffmpeg,"wistia-hls")),
        ("Shared hosting media JW HLS renews complete separate tracks",()=>SharedHostingMedia(ffmpeg,"jw-hls")),
        ("Shared hosting media Brightcove DASH renews complete separate tracks",()=>SharedHostingMedia(ffmpeg,"brightcove-dash")),
        ("Shared hosting media Kaltura HLS renews complete separate tracks",()=>SharedHostingMedia(ffmpeg,"kaltura-hls")),
        ("Shared hosting media delegated Wistia credentials stay at the API",()=>SharedHostingMedia(ffmpeg,"wistia-delegated")),
        ("Shared hosting media delegated JW signing stays at delivery",()=>SharedHostingMedia(ffmpeg,"jw-delegated-signed")),
        ("Shared hosting media Kaltura account renewal preserves its private session",()=>SharedHostingMedia(ffmpeg,"kaltura-login")),
        ("Shared hosting media JW playlist selection carries resource renewal",()=>SharedHostingMedia(ffmpeg,"jw-playlist")),
        ("Shared hosting media revoked access preserves prior files",()=>SharedHostingMedia(ffmpeg,"revoked")),
        ("Shared hosting media changed formats preserve prior files",()=>SharedHostingMedia(ffmpeg,"changed")),
        ("Shared hosting media cancellation preserves prior files",()=>SharedHostingMedia(ffmpeg,"cancel")),
        ("Adaptive account media Vimeo HLS verifies complete renewed tracks",()=>AdaptiveAccountMedia(ffmpeg,"vimeo-hls")),
        ("Adaptive account media Vimeo DASH verifies complete renewed tracks",()=>AdaptiveAccountMedia(ffmpeg,"vimeo-dash")),
        ("Adaptive account media live DASH renewal retains its complete requested window",()=>AdaptiveAccountMedia(ffmpeg,"live-dash")),
        ("Adaptive account media Vimeo OAuth DASH keeps issuer tokens scoped",()=>AdaptiveAccountMedia(ffmpeg,"vimeo-oauth-dash")),
        ("Adaptive account media Vimeo API captions renew and embed",()=>AdaptiveAccountMedia(ffmpeg,"vimeo-api-caption")),
        ("Adaptive account media YouTube HLS keeps account renewal bound",()=>AdaptiveAccountMedia(ffmpeg,"youtube-hls")),
        ("Adaptive account media YouTube DASH keeps account renewal bound",()=>AdaptiveAccountMedia(ffmpeg,"youtube-dash")),
        ("Adaptive account media revocation preserves prior files and archive",()=>AdaptiveAccountMedia(ffmpeg,"revoked")),
        ("Adaptive account media changed representation preserves prior files",()=>AdaptiveAccountMedia(ffmpeg,"changed")),
        ("Adaptive account media cancellation preserves prior files and archive",()=>AdaptiveAccountMedia(ffmpeg,"cancel")),
        ("Account renewal media vimeo-password verifies complete protected publication",()=>AccountMedia(ffmpeg,"vimeo-password")),
        ("Account renewal media vimeo-token verifies complete protected publication",()=>AccountMedia(ffmpeg,"vimeo-token")),
        ("Account renewal media vimeo-renew verifies complete protected publication",()=>AccountMedia(ffmpeg,"vimeo-renew")),
        ("Account renewal media vimeo-expiry verifies complete protected publication",()=>AccountMedia(ffmpeg,"vimeo-expiry")),
        ("Account renewal media vimeo-revoked verifies complete protected publication",()=>AccountMedia(ffmpeg,"vimeo-revoked")),
        ("Account renewal media youtube-cookie verifies complete protected publication",()=>AccountMedia(ffmpeg,"youtube-cookie")),
        ("Account renewal media youtube-renew verifies complete protected publication",()=>AccountMedia(ffmpeg,"youtube-renew")),
        ("Account renewal media SABR http preserves selected tracks and prior data",()=>AccountSabrMedia(ffmpeg,"http")),
        ("Account renewal media SABR reload preserves selected tracks and prior data",()=>AccountSabrMedia(ffmpeg,"reload")),
        ("Account renewal media SABR protection preserves selected tracks and prior data",()=>AccountSabrMedia(ffmpeg,"protection")),
        ("Live captions and clipping media hls verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls")),
        ("Live captions and clipping media bounded complete HLS preserves static captions",()=>LiveCaptionMedia(ffmpeg,"hls-static")),
        ("Live captions and clipping media small audio delays preserve AAC priming packets",()=>SmallDelayedPriming(ffmpeg)),
        ("Live captions and clipping media moving HLS subtitles retain expired fragments",()=>LiveCaptionMedia(ffmpeg,"hls-moving")),
        ("Live captions and clipping media hls-srt verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls-srt")),
        ("Live captions and clipping media hls-encrypted verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls-encrypted")),
        ("Live captions and clipping media hls-range verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls-range")),
        ("Live captions and clipping media hls-reset verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls-reset")),
        ("Live captions and clipping media hls-gap verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"hls-gap")),
        ("Live captions and clipping media dash-wvtt verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-wvtt")),
        ("Live captions and clipping media lossless audio retains every decoded sample",()=>LiveCaptionMedia(ffmpeg,"dash-lossless")),
        ("Live captions and clipping media dash-stpp verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-stpp")),
        ("Live captions and clipping media dash-range verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-range")),
        ("Live captions and clipping media dash-video verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-video")),
        ("Live captions and clipping media dash-audio verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-audio")),
        ("Live captions and clipping media dash-unselected verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-unselected")),
        ("Live captions and clipping media dash-gap verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-gap")),
        ("Live captions and clipping media dash-corrupt verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-corrupt")),
        ("Live captions and clipping media dash-cancel verifies complete synchronized publication",()=>LiveCaptionMedia(ffmpeg,"dash-cancel")),
        ("DASH continuity media template captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"template")),
        ("DASH continuity media timeline captures reset clocks through period transitions",()=>DashContinuityMedia(ffmpeg,"timeline")),
        ("DASH continuity media late entry records the available window across periods",()=>DashContinuityMedia(ffmpeg,"late")),
        ("DASH continuity media list captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"list")),
        ("DASH continuity media range captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"range")),
        ("DASH continuity media video captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"video")),
        ("DASH continuity media audio captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"audio")),
        ("DASH continuity media codec captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"codec")),
        ("DASH continuity media gap captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"gap")),
        ("DASH continuity media missing captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"missing")),
        ("DASH continuity media cancel captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"cancel")),
        ("DASH continuity media init captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"init")),
        ("DASH continuity media decoder captures or protects complete period publication",()=>DashContinuityMedia(ffmpeg,"decoder")),
        ("Adaptive live media paired HLS preserves both tracks and originals",()=>AdaptiveLiveMedia(ffmpeg,"hls")),
        ("Adaptive live media declared audio offset remains synchronized",()=>AdaptiveLiveMedia(ffmpeg,"offset")),
        ("Adaptive live media ended tracks close one shared recording window",()=>AdaptiveLiveMedia(ffmpeg,"ended")),
        ("Adaptive live media dynamic DASH records synchronized available tracks",()=>AdaptiveLiveMedia(ffmpeg,"dash")),
        ("Adaptive live media a missing audio interval protects output and archive",()=>AdaptiveLiveMedia(ffmpeg,"gap")),
        ("Adaptive live media cancellation drains both selected tracks",()=>AdaptiveLiveMedia(ffmpeg,"cancel")),
        ("Adaptive live media private paired HLS renews without credential leakage",()=>AdaptiveLiveMedia(ffmpeg,"private")),
        ("Authentication batch media protected playback retains originals callbacks and archives",()=>AuthenticatedBatchMedia(ffmpeg)),
        ("Live batch media default capture begins near the live edge",()=>LiveBatchMedia(ffmpeg,"tail")),
        ("Live batch media ended moving window preserves every captured frame",()=>LiveBatchMedia(ffmpeg,"end")),
        ("Live batch media bounded capture stops on complete segment boundaries",()=>LiveBatchMedia(ffmpeg,"bounded")),
        ("Live batch media event from-start retains the exposed beginning",()=>LiveBatchMedia(ffmpeg,"from-start")),
        ("Live batch media AES encryption preserves complete decoded playback",()=>LiveBatchMedia(ffmpeg,"encrypted")),
        ("Live batch media fragmented MP4 retains initialization and complete frames",()=>LiveBatchMedia(ffmpeg,"fmp4")),
        ("Live batch media missing segments protect prior published output",()=>LiveBatchMedia(ffmpeg,"gap")),
        ("Live batch media changed segment identities protect prior publication",()=>LiveBatchMedia(ffmpeg,"changed")),
        ("Live batch media stalled playlist fails and clears session resources",()=>LiveBatchFailure(ffmpeg,true)),
        ("Live batch media cancellation preserves output and clears recording resources",()=>LiveBatchFailure(ffmpeg,false)),
        ("Batched arguments media AAC extraction retains complete original and parsed job policy",()=>BatchedArgumentsMedia(ffmpeg,"m4a",true)),
        ("Batched arguments media FLAC extraction preserves exact decoded source audio",()=>BatchedArgumentsMedia(ffmpeg,"flac",true)),
        ("Batched arguments media WAV extraction preserves exact decoded source audio",()=>BatchedArgumentsMedia(ffmpeg,"wav",true)),
        ("Batched arguments media Opus extraction verifies complete playback and archives",()=>BatchedArgumentsMedia(ffmpeg,"opus",true)),
        ("Batched arguments media Vorbis extraction verifies complete playback and archives",()=>BatchedArgumentsMedia(ffmpeg,"vorbis",true)),
        ("Batched arguments media no-keep-video emits one complete converted output",()=>BatchedArgumentsMedia(ffmpeg,"m4a",false)),
        ("Clyp native public MP3 streams preserve complete samples originals and archives",()=>ClypMedia(ffmpeg,"mp3")),
        ("Clyp native offered Ogg preserves complete decoded audio and original bytes",()=>ClypMedia(ffmpeg,"ogg")),
        ("Clyp native widgets preserve selected complete playback and exact originals",()=>ClypMedia(ffmpeg,"widget")),
        ("Clyp native direct stream aliases retain requested encoding and stable archives",()=>ClypMedia(ffmpeg,"direct")),
        ("Clyp native permitted download aliases use the supplied complete resource",()=>ClypMedia(ffmpeg,"download")),
        ("Clyp native AAC conversion retains complete published MP3 and metadata",()=>ClypMedia(ffmpeg,"convert")),
        ("Clyp native precise duration floor refuses subtly incomplete sources before publication",()=>ClypMedia(ffmpeg,"short")),
        ("Clyp native cancelled transfers preserve prior media archive and callbacks",()=>ClypMedia(ffmpeg,"cancel")),
        ("Vocaroo native current recordings preserve complete decoded audio exact originals and archives",()=>VocarooMedia(ffmpeg,"modern")),
        ("Vocaroo native legacy recordings use their published original gateway",()=>VocarooMedia(ffmpeg,"legacy")),
        ("Vocaroo native embeds preserve selected recording bytes and complete samples",()=>VocarooMedia(ffmpeg,"embed")),
        ("Vocaroo native direct recording aliases retain stable archives and complete audio",()=>VocarooMedia(ffmpeg,"direct")),
        ("Vocaroo native conversion retains the exact supplied MP3 and complete AAC output",()=>VocarooMedia(ffmpeg,"convert")),
        ("Vocaroo native digest refuses a different valid same-size recording before publication",()=>VocarooMedia(ffmpeg,"changed")),
        ("Vocaroo native incomplete objects preserve prior audio archive and callbacks",()=>VocarooMedia(ffmpeg,"short")),
        ("Vocaroo native cancelled transfers preserve prior complete audio and archives",()=>VocarooMedia(ffmpeg,"cancel")),
        ("Metadata parsing media retains full audio exact originals embedded tags and archives", () => MetadataParsingMedia(ffmpeg,"normal")),
        ("Metadata parsing media conversion retains full source and parsed AAC metadata", () => MetadataParsingMedia(ffmpeg,"convert")),
        ("Metadata parsing media disabled embedding preserves original tags and samples", () => MetadataParsingMedia(ffmpeg,"noembed")),
        ("Metadata parsing media explicit embedded overrides preserve descriptive JSON", () => MetadataParsingMedia(ffmpeg,"overrides")),
        ("Metadata parsing media inspected selection preserves source position and complete playback", () => MetadataParsingMedia(ffmpeg,"snapshot")),
        ("Metadata parsing media verification failure protects prior output archive and callbacks", () => MetadataParsingMedia(ffmpeg,"failure")),
        ("Native ReverbNation MP3 retains full decoded audio exact originals and archives", () => ReverbNationMedia(ffmpeg, "normal")),
        ("Native ReverbNation widgets retain selected complete public audio", () => ReverbNationMedia(ffmpeg, "widget")),
        ("Native ReverbNation counted artist selections retain source positions and originals", () => ReverbNationMedia(ffmpeg, "selected")),
        ("Native ReverbNation inspected selections refresh chosen complete playback", () => ReverbNationMedia(ffmpeg, "snapshot")),
        ("Native ReverbNation conversion retains full original and processed audio", () => ReverbNationMedia(ffmpeg, "convert")),
        ("Native ReverbNation short sources cannot publish media or archive success", () => ReverbNationMedia(ffmpeg, "short")),
        ("Native hearthis MP3 retains complete audio samples exact originals and archives", () => HearThisMedia(ffmpeg, "normal")),
        ("Native hearthis public players retain the exact complete recording", () => HearThisMedia(ffmpeg, "embed")),
        ("Native hearthis artist selections retain original positions and complete playback", () => HearThisMedia(ffmpeg, "selected")),
        ("Native hearthis inspected selections refresh chosen complete playback only", () => HearThisMedia(ffmpeg, "snapshot")),
        ("Native hearthis audio conversion retains complete originals and decoded output", () => HearThisMedia(ffmpeg, "convert")),
        ("Native hearthis short sources cannot publish output or archive success", () => HearThisMedia(ffmpeg, "short")),
        ("Native BandLab public audio retains complete decoded samples exact originals and archives", () => BandlabMedia(ffmpeg, "normal")),
        ("Native BandLab embeds bind public revisions and preserve complete audio", () => BandlabMedia(ffmpeg, "embed")),
        ("Native BandLab selected playlists retain original source positions and media", () => BandlabMedia(ffmpeg, "selected")),
        ("Native BandLab inspected selections refresh only complete selected playback", () => BandlabMedia(ffmpeg, "snapshot")),
        ("Native BandLab conversion retains complete source and processed audio", () => BandlabMedia(ffmpeg, "convert")),
        ("Native BandLab short advertised sources cannot publish media or archive success", () => BandlabMedia(ffmpeg, "short")),
        ("Native BandLab later selected cancellation preserves earlier completed media", () => BandlabMedia(ffmpeg, "cancel")),
        ("Range chunks indexed DASH preserve exact offsets and fully decoded tracks", () => IndexedDashMedia(ffmpeg, httpChunkSize: "128")),
        ("Range chunks parallel indexed DASH preserve full picture and audio frames", () => IndexedDashMedia(ffmpeg, 3, "128")),
        ("Range chunks HLS captions preserve cue text timing and complete embedded media", () => HlsCaptionMedia(ffmpeg, "clear", "8")),
        ("HTTP chunks progressive pairs retain complete pictures audio and exact originals", () => DelayMedia(ffmpeg, "pair", httpChunkSize: "2K")),
        ("HTTP chunks HLS pairs retain complete pictures audio and exact originals", () => DelayMedia(ffmpeg, "hls", httpChunkSize: "3K")),
        ("HTTP chunks whole DASH pairs retain complete pictures audio and exact originals", () => DelayMedia(ffmpeg, "dash", httpChunkSize: "4K")),
        ("HTTP chunks paired cancellation preserves previous media and archives", () => DelayMedia(ffmpeg, "cancel", httpChunkSize: "2K")),
        ("Download buffers fixed progressive pairs retain full decoded media and originals", () => DelayMedia(ffmpeg, "pair", bufferSize: "3K", noResizeBuffer: true)),
        ("Download buffers automatic HLS pairs retain full decoded media and originals", () => DelayMedia(ffmpeg, "hls", bufferSize: "2K")),
        ("Download buffers fixed DASH pairs retain full decoded media and originals", () => DelayMedia(ffmpeg, "dash", bufferSize: "5K", noResizeBuffer: true)),
        ("Download buffers cancelled paired transfers preserve previous media and archives", () => DelayMedia(ffmpeg, "cancel", bufferSize: "7K", noResizeBuffer: true)),
        ("No-part separate progressive tracks retain fully decoded pictures audio and originals", () => DelayMedia(ffmpeg, "pair", noPart: true)),
        ("No-part separate HLS tracks retain fully decoded pictures audio and originals", () => DelayMedia(ffmpeg, "hls", noPart: true)),
        ("No-part separate DASH tracks retain fully decoded pictures audio and originals", () => DelayMedia(ffmpeg, "dash", noPart: true)),
        ("No-part audio conversion retains complete originals remote timestamps and archive skips", () => FileTimeMedia(ffmpeg, "convert", noPart: true)),
        ("Native Audiomack high-quality M4A preserves complete audio originals and archives", () => AudiomackMedia(ffmpeg, "hq")),
        ("Native Audiomack standard MP3 preserves complete selected encoding", () => AudiomackMedia(ffmpeg, "standard")),
        ("Native Audiomack public embeds resolve and preserve original audio", () => AudiomackMedia(ffmpeg, "embed")),
        ("Native Audiomack album selection preserves source positions and selected-only media", () => AudiomackMedia(ffmpeg, "album")),
        ("Native Audiomack inspected album snapshots refresh full selected sources", () => AudiomackMedia(ffmpeg, "snapshot")),
        ("Native Audiomack conversion preserves full original and processed audio", () => AudiomackMedia(ffmpeg, "convert")),
        ("Native Audiomack short source refusal protects prior media and archives", () => AudiomackMedia(ffmpeg, "short")),
        ("Native Audiomack ordered cancellation retains completed files and identities", () => AudiomackMedia(ffmpeg, "cancel")),
        ("File timestamps conversion and originals preserve the remote modification time", () => FileTimeMedia(ffmpeg, "convert")),
        ("File timestamps no-mtime keeps newly published media and originals current", () => FileTimeMedia(ffmpeg, "disabled")),
        ("File timestamps retained progressive components preserve distinct entity dates", () => FileTimeMedia(ffmpeg, "pair")),
        ("File timestamps failed processing publication preserves prior dates and archives", () => FileTimeMedia(ffmpeg, "failure")),
        ("Native Newgrounds MP3 playback preserves full audio originals and archives", () => NewgroundsMedia(ffmpeg, "audio")),         ("Native Newgrounds movies preserve every picture frame and original bytes", () => NewgroundsMedia(ffmpeg, "video")),         ("Native Newgrounds movie audio extraction retains complete original video", () => NewgroundsMedia(ffmpeg, "video-audio")),         ("Native Newgrounds creator selection preserves selected-only media and source position", () => NewgroundsMedia(ffmpeg, "selected")),         ("Native Newgrounds inspected snapshots retain immutable selected audio", () => NewgroundsMedia(ffmpeg, "snapshot")),         ("Native Newgrounds conversion preserves complete processed and original audio", () => NewgroundsMedia(ffmpeg, "convert")),         ("Native Newgrounds incomplete source refusal protects previous publication", () => NewgroundsMedia(ffmpeg, "short")),         ("Native Newgrounds later cancellation preserves earlier completed files and archives", () => NewgroundsMedia(ffmpeg, "cancel")),
        ("Native Jamendo complete MP3 playback preserves audio originals and archives", () => JamendoMedia(ffmpeg, "single")),
        ("Native Jamendo Ogg playback preserves complete audio and exact source bytes", () => JamendoMedia(ffmpeg, "ogg")),
        ("Native Jamendo supplied stream queries preserve full selected audio", () => JamendoMedia(ffmpeg, "stream")),
        ("Native Jamendo album selection preserves original positions and selected-only media", () => JamendoMedia(ffmpeg, "selected")),
        ("Native Jamendo inspected album snapshots refresh and save selected full tracks", () => JamendoMedia(ffmpeg, "snapshot")),
        ("Native Jamendo audio conversion retains full original and processed media", () => JamendoMedia(ffmpeg, "convert")),
        ("Native Jamendo short source refusals preserve prior files archives and callbacks", () => JamendoMedia(ffmpeg, "short")),
        ("Native Jamendo near-duration short sources cannot exploit shared rounding tolerance", () => JamendoMedia(ffmpeg, "short-rounding")),
        ("Native Jamendo ordered selection cancellation retains earlier publication", () => JamendoMedia(ffmpeg, "cancel")),
        ("Download delay separate progressive tracks each wait and preserve complete media", () => DelayMedia(ffmpeg, "pair")),
        ("Download delay separate HLS tracks each wait and preserve complete media", () => DelayMedia(ffmpeg, "hls")),
        ("Download delay separate DASH tracks each wait and preserve complete media", () => DelayMedia(ffmpeg, "dash")),
        ("Download delay cancellation before a second track preserves prior publication", () => DelayMedia(ffmpeg, "cancel")),
        ("File-size bounds measure complete original media before audio conversion",()=>FileSizeMedia(ffmpeg,"convert")),
        ("File-size bounds cannot publish oversized static HLS or change prior output",()=>FileSizeMedia(ffmpeg,"hls")),
        ("File-size bounds cannot publish undersized static HLS or archive success",()=>FileSizeMedia(ffmpeg,"hls-min")),
        ("Native Omny audio preserves complete playback originals packets and archives",()=>OmnyMedia(ffmpeg,"normal")),
        ("Native Omny program selection preserves original position and selected-only playback",()=>OmnyMedia(ffmpeg,"selected")),
        ("Native Omny playlist snapshots refresh immutable selected audio",()=>OmnyMedia(ffmpeg,"snapshot")),
        ("Native Omny conversion retains full original and processed audio",()=>OmnyMedia(ffmpeg,"convert")),
        ("Native Omny dynamic audio preserves longer offered delivery",()=>OmnyMedia(ffmpeg,"dynamic")),
        ("Native Omny progressive video preserves complete picture audio and originals",()=>OmnyMedia(ffmpeg,"video")),
        ("Native Omny short audio cannot publish or record archive success",()=>OmnyMedia(ffmpeg,"incomplete")),
        ("Native Omny HLS retains complete video audio and original transport",()=>OmnyMedia(ffmpeg,"hls")),
        ("Native Omny short HLS cannot bypass the program-duration guard",()=>OmnyMedia(ffmpeg,"hls-incomplete")),
        ("Native Omny static video captions and chapters preserve full media timing",()=>OmnyMedia(ffmpeg,"timed")),
        ("Native iHeart MP3 retains complete audio exact originals and stable archives", () => IHeartMedia(ffmpeg, "normal")),
        ("Native iHeart selected podcast audio retains original positions and skips unselected playback", () => IHeartMedia(ffmpeg, "selected")),
        ("Native iHeart inspected selections refresh only chosen complete audio", () => IHeartMedia(ffmpeg, "snapshot")),
        ("Native iHeart audio conversion retains the complete source and decoded output", () => IHeartMedia(ffmpeg, "convert")),
        ("Native iHeart dynamic delivery preserves audio longer than the listed program", () => IHeartMedia(ffmpeg, "dynamic")),
        ("Native iHeart offered M4A preserves full original and decoded AAC audio", () => IHeartMedia(ffmpeg, "m4a")),
        ("Native iHeart offered ADTS becomes complete best-audio output without losing samples", () => IHeartMedia(ffmpeg, "aac")),
        ("Native iHeart short source refusal preserves prior output and archive state", () => IHeartMedia(ffmpeg, "incomplete")),
        ("Native Mixcloud clear M4A preserves complete playback exact originals and archives", () => MixcloudMedia(ffmpeg, "normal")),
        ("Native Mixcloud selected creator uploads preserve original positions and full audio", () => MixcloudMedia(ffmpeg, "selected")),
        ("Native Mixcloud inspected snapshots refresh only chosen clear playback", () => MixcloudMedia(ffmpeg, "snapshot")),
        ("Native Mixcloud audio conversion preserves complete playback and original source", () => MixcloudMedia(ffmpeg, "convert")),
        ("Native Mixcloud incomplete clear sources cannot publish files or archive success", () => MixcloudMedia(ffmpeg, "incomplete")),
        ("Native Mixcloud clear static HLS preserves complete audio playback originals and archives", () => MixcloudAdaptive(ffmpeg, "hls")),
        ("Native Mixcloud clear segmented DASH preserves complete audio playback originals and archives", () => MixcloudAdaptive(ffmpeg, "dash")),
        ("Native migrated Podomatic feed selection preserves complete audio original bytes and GUID archives", () => PodomaticFeedMedia(ffmpeg, "normal")),
        ("Native migrated feed snapshot refreshes playback while retaining original GUID and source position", () => PodomaticFeedMedia(ffmpeg, "snapshot")),
        ("Native migrated feed audio conversion retains complete playback and original source", () => PodomaticFeedMedia(ffmpeg, "convert")),
        ("Native migrated feed cannot publish source audio shorter than its declared program", () => PodomaticFeedMedia(ffmpeg, "incomplete")),
        ("Native migrated feed refuses invalid chapter embedding without changing prior media or archives", () => PodomaticFeedMedia(ffmpeg, "bad-chapters")),
        ("Native MP3 remux preserves decoded boundary samples without source gapless metadata", () => Mp3Boundary(ffmpeg, "remux")),
        ("Native MP3 audio copy does not invent encoder delay or remove source samples", () => Mp3Boundary(ffmpeg, "copy")),
        ("Native MP3 metadata embedding preserves decoded samples without a source Xing header", () => Mp3Boundary(ffmpeg, "embed")),
        ("Native MP3 metadata embedding retains existing gapless boundary samples", () => Mp3Boundary(ffmpeg, "embed", true)),
        ("Native AudioBoom audio preserves full decoding exact originals and encoded payloads", () => AudioBoomMedia(ffmpeg, "normal")),
        ("Native AudioBoom selected show episodes retain source positions stable archives and complete audio", () => AudioBoomMedia(ffmpeg, "selected")),
        ("Native AudioBoom inspected show snapshots refresh only selected complete playback", () => AudioBoomMedia(ffmpeg, "snapshot")),
        ("Native AudioBoom audio conversion preserves full playback and original source", () => AudioBoomMedia(ffmpeg, "convert")),
        ("Native incomplete AudioBoom enclosure cannot publish audio or archive success", () => AudioBoomMedia(ffmpeg, "incomplete")),
        ("Native Simplecast dynamic audio accepts longer delivery while preserving all source frames", () => SimplecastMedia(ffmpeg, "dynamic")),
        ("Native Simplecast dynamic audio rejects sources shorter than the complete program", () => SimplecastMedia(ffmpeg, "dynamic-incomplete")),
        ("Native Simplecast audio preserves full decoding exact originals and encoded payloads", () => SimplecastMedia(ffmpeg, "normal")),
        ("Native Simplecast selected show episodes retain source positions stable archives and complete audio", () => SimplecastMedia(ffmpeg, "selected")),
        ("Native Simplecast inspected show snapshots refresh only selected complete playback", () => SimplecastMedia(ffmpeg, "snapshot")),
        ("Native Simplecast audio conversion preserves full playback and original source", () => SimplecastMedia(ffmpeg, "convert")),
        ("Native incomplete Simplecast enclosure cannot publish audio or archive success", () => SimplecastMedia(ffmpeg, "incomplete")),
        ("Native Podomatic video preserves full playback exact originals and stable archives", () => PodomaticVideo(ffmpeg, "video")),
        ("Native Podomatic video-only processing preserves all source video frames", () => PodomaticVideo(ffmpeg, "video-only")),
        ("Native Podomatic audio extraction from video preserves complete playback and original source", () => PodomaticVideo(ffmpeg, "audio")),
        ("Native Libsyn video preserves full playback exact originals and stable archives", () => LibsynVideo(ffmpeg, "video")),
        ("Native Libsyn video-only processing preserves all source video frames", () => LibsynVideo(ffmpeg, "video-only")),
        ("Native Libsyn audio extraction from video preserves complete playback and original source", () => LibsynVideo(ffmpeg, "audio")),
        ("Native Libsyn audio preserves full decoding exact originals and encoded payloads", () => LibsynMedia(ffmpeg, "normal")),
        ("Native Libsyn selected show episodes retain source positions stable archives and complete audio", () => LibsynMedia(ffmpeg, "selected")),
        ("Native Libsyn inspected show snapshots refresh only selected complete playback", () => LibsynMedia(ffmpeg, "snapshot")),
        ("Native Libsyn audio conversion preserves full playback and original source", () => LibsynMedia(ffmpeg, "convert")),
        ("Native incomplete Libsyn enclosure cannot publish audio or archive success", () => LibsynMedia(ffmpeg, "incomplete")),
        ("Native Megaphone audio preserves full decoding exact originals and encoded payloads", () => MegaphoneMedia(ffmpeg, "normal")),
        ("Native Megaphone selected show episodes retain source positions stable archives and complete audio", () => MegaphoneMedia(ffmpeg, "selected")),
        ("Native Megaphone inspected show snapshots refresh only selected complete playback", () => MegaphoneMedia(ffmpeg, "snapshot")),
        ("Native Megaphone audio conversion preserves full playback and original source", () => MegaphoneMedia(ffmpeg, "convert")),
        ("Native incomplete Megaphone enclosure cannot publish audio or archive success", () => MegaphoneMedia(ffmpeg, "incomplete")),
        ("Native Podomatic audio preserves full decoding exact originals and encoded payloads", () => PodomaticMedia(ffmpeg, "normal")),
        ("Native Podomatic selected show episodes retain source positions stable archives and complete audio", () => PodomaticMedia(ffmpeg, "selected")),
        ("Native Podomatic inspected show snapshots refresh only selected complete playback", () => PodomaticMedia(ffmpeg, "snapshot")),
        ("Native Podomatic audio conversion preserves full playback and original source", () => PodomaticMedia(ffmpeg, "convert")),
        ("Native incomplete Podomatic enclosure cannot publish audio or archive success", () => PodomaticMedia(ffmpeg, "incomplete")),
        ("Native Audius audio preserves full decoding exact originals and encoded payloads", () => AudiusMedia(ffmpeg, "normal")),
        ("Native Audius selected show episodes retain source positions stable archives and complete audio", () => AudiusMedia(ffmpeg, "selected")),
        ("Native Audius inspected show snapshots refresh only selected complete playback", () => AudiusMedia(ffmpeg, "snapshot")),
        ("Native Audius audio conversion preserves full playback and original source", () => AudiusMedia(ffmpeg, "convert")),
        ("Native incomplete Audius enclosure cannot publish audio or archive success", () => AudiusMedia(ffmpeg, "incomplete")),
        ("Native Spreaker audio preserves full decoding exact originals and encoded payloads", () => SpreakerMedia(ffmpeg, "normal")),
        ("Native Spreaker selected show episodes retain source positions stable archives and complete audio", () => SpreakerMedia(ffmpeg, "selected")),
        ("Native Spreaker inspected show snapshots refresh only selected complete playback", () => SpreakerMedia(ffmpeg, "snapshot")),
        ("Native Spreaker audio conversion preserves full playback and original source", () => SpreakerMedia(ffmpeg, "convert")),
        ("Native incomplete Spreaker enclosure cannot publish audio or archive success", () => SpreakerMedia(ffmpeg, "incomplete")),
        ("Native Acast audio preserves full decoding exact originals and encoded payloads", () => AcastMedia(ffmpeg, "normal")),
        ("Native Acast selected show episodes retain source positions stable archives and complete audio", () => AcastMedia(ffmpeg, "selected")),
        ("Native Acast inspected show snapshots refresh only selected complete playback", () => AcastMedia(ffmpeg, "snapshot")),
        ("Native Acast audio conversion preserves full playback and original source", () => AcastMedia(ffmpeg, "convert")),
        ("Native incomplete Acast enclosure cannot publish audio or archive success", () => AcastMedia(ffmpeg, "incomplete")),
        ("Native Medal HLS service preserves opaque tokens complete playback and originals", () => MedalHlsMedia(ffmpeg, "normal")),
        ("Native Medal HLS video-only output preserves all frames and retained source", () => MedalHlsMedia(ffmpeg, "video")),
        ("Native Medal HLS audio-only output preserves full audio and original source", () => MedalHlsMedia(ffmpeg, "audio")),
        ("Native Medal HLS silent clips do not invent a soundtrack", () => MedalHlsMedia(ffmpeg, "silent")),
        ("Native incomplete Medal HLS cannot publish files or archive success", () => MedalHlsMedia(ffmpeg, "incomplete")),
        ("Native denied Medal HLS does not fall back to other resources or modify prior files", () => MedalHlsMedia(ffmpeg, "denied")),
        ("Native Medal source MP4 retains complete playback signed links originals and archives", () => MedalMedia(ffmpeg, "source")),
        ("Native Medal supplied ready rendition remains selected without unavailable fallback", () => MedalMedia(ffmpeg, "rendition")),
        ("Native Medal video-only processing preserves all source frames and original audio", () => MedalMedia(ffmpeg, "video")),
        ("Native Medal audio-only processing retains complete source and copied audio", () => MedalMedia(ffmpeg, "audio")),
        ("Native Medal silent source retains complete video without invented audio", () => MedalMedia(ffmpeg, "silent")),
        ("Native incomplete Medal source cannot publish media or archive success", () => MedalMedia(ffmpeg, "incomplete")),
        ("Native Coub default playback repeats video through complete audio with exact originals", () => CoubMedia(ffmpeg, "default")),
        ("Native Coub explicit paired qualities retain encoded loop and audio payloads", () => CoubMedia(ffmpeg, "pair")),
        ("Native Coub shorter audio loops through one complete video source cycle", () => CoubMedia(ffmpeg, "short-audio")),
        ("Native Coub video-only output retains one complete source cycle", () => CoubMedia(ffmpeg, "video")),
        ("Native Coub explicit single-video selector does not gain a soundtrack", () => CoubMedia(ffmpeg, "single")),
        ("Native Coub audio-only output retains the complete supplied audio track", () => CoubMedia(ffmpeg, "audio")),
        ("Native Coub silent clip retains one cycle without fabricated audio", () => CoubMedia(ffmpeg, "silent")),
        ("Native changed Coub source size cannot publish outputs or archive success", () => CoubMedia(ffmpeg, "wrong-size")),
        ("Native PBS Kids MP4 preserves complete playback originals and asset archives", () => PbsKidsMedia(ffmpeg, "mp4")),
        ("Native PBS Kids HLS preserves every frame and original media components", () => PbsKidsMedia(ffmpeg, "hls")),
        ("Native PBS Kids inspected selected MP4 retains source identity positions and callbacks", () => PbsKidsMedia(ffmpeg, "selected-mp4")),
        ("Native PBS Kids inspected selected HLS retains complete media and archive retries", () => PbsKidsMedia(ffmpeg, "selected-hls")),
        ("Native incomplete PBS Kids source cannot publish outputs or archive success", () => PbsKidsMedia(ffmpeg, "incomplete")),
        ("Native region-denied PBS Kids source preserves prior files and archive state", () => PbsKidsMedia(ffmpeg, "denied")),
        ("MP4 legacy RTP hints can remux while preserving all encoded video and audio payloads", () => LegacyHintMedia(ffmpeg, false)),
        ("MP4 legacy RTP hints can save metadata while retaining original bytes and complete playback", () => LegacyHintMedia(ffmpeg, true)),
        ("Native PBS MP4 preserves complete media original bytes captions and archives", () => PbsMedia(ffmpeg, "mp4")),
        ("Native PBS HLS preserves complete frames captions and original components", () => PbsMedia(ffmpeg, "hls")),
        ("Native PBS offered SRT caption selection preserves and embeds original text", () => PbsMedia(ffmpeg, "srt")),
        ("Native PBS offered DFXP captions retain originals and embed native TTML text", () => PbsMedia(ffmpeg, "ttml")),
        ("Native PBS numeric aliases retain complete media and skip known archives before discovery", () => PbsMedia(ffmpeg, "numeric")),
        ("Native incomplete PBS media cannot publish files or archive success", () => PbsMedia(ffmpeg, "incomplete")),
        ("Native Microsoft Embed literal MP4 retains complete playback original bytes and captions", () => MicrosoftEmbedMedia(ffmpeg, "mp4")),
        ("Native Microsoft Embed flat TTML settings convert and embed complete caption text", () => MicrosoftEmbedMedia(ffmpeg, "flat-ttml")),
        ("Native Microsoft Embed supplied HLS retains complete frames captions and originals", () => MicrosoftEmbedMedia(ffmpeg, "hls")),
        ("Native Microsoft Embed supplied DASH retains complete frames captions and components", () => MicrosoftEmbedMedia(ffmpeg, "dash")),
        ("Native Microsoft Embed mapped Cascade video retains complete playback and stable archives", () => MicrosoftEmbedMedia(ffmpeg, "mapped")),
        ("Native incomplete Microsoft Embed Cascade source cannot publish media or archive success", () => MicrosoftEmbedMedia(ffmpeg, "incomplete")),
        ("Native Microsoft Learn MP4 preserves complete playback originals captions and archives", () => MicrosoftLearnMedia(ffmpeg, "mp4")),
        ("Native Microsoft Learn separate progressive encodings retain complete video and audio", () => MicrosoftLearnMedia(ffmpeg, "split")),
        ("Native Microsoft Learn HLS preserves all frames original components and caption text", () => MicrosoftLearnMedia(ffmpeg, "hls")),
        ("Native Microsoft Learn DASH verifies complete playback before publishing originals and captions", () => MicrosoftLearnMedia(ffmpeg, "dash")),
        ("Native Microsoft Learn MP4 audio retains full decoding and audio-only output", () => MicrosoftLearnMedia(ffmpeg, "audio")),
        ("Native Microsoft Learn selected show episodes preserve unavailable source positions", () => MicrosoftLearnMedia(ffmpeg, "show")),
        ("Native incomplete Microsoft Learn DASH cannot publish media or archive success", () => MicrosoftLearnMedia(ffmpeg, "incomplete-dash")),
        ("Native Mastodon video preserves complete frames originals and federated archives", () => MastodonMedia(ffmpeg, "video")),
        ("Native Mastodon silent GIFV preserves original playback without invented audio", () => MastodonMedia(ffmpeg, "gifv")),
        ("Native Mastodon audio retains full decoding and audio-only output", () => MastodonMedia(ffmpeg, "audio")),
        ("Native Mastodon selected attachments retain source positions and unique completions", () => MastodonMedia(ffmpeg, "multi")),
        ("Native incomplete Mastodon media cannot publish files or archive success", () => MastodonMedia(ffmpeg, "incomplete")),
        ("Native Pinterest MP4 preserves complete media originals automatic captions and archives", () => PinterestMedia(ffmpeg, "mp4")),
        ("Native Tumblr MP4 retains complete playback original bytes and archives", () => TumblrMedia(ffmpeg, "mp4")),
        ("Native Tumblr resized MOV retains actual quality limits complete frames and offered variants", () => TumblrMedia(ffmpeg, "mov")),
        ("Native Tumblr audio blocks retain audio-only playback and originals", () => TumblrMedia(ffmpeg, "audio")),
        ("Native Tumblr multi-media selection excludes unselected and suggested post assets", () => TumblrMedia(ffmpeg, "multi")),
        ("Native incomplete Tumblr media cannot publish outputs or archive success", () => TumblrMedia(ffmpeg, "incomplete")),
        ("Nested playlist Pinterest board story MP4 retains all media parent positions and unique callbacks", () => NestedPinterestMedia(ffmpeg, false)),
        ("Nested playlist Pinterest board story HLS retains complete playback originals and archive retries", () => NestedPinterestMedia(ffmpeg, true)),
        ("Native Pinterest story MP4 preserves full media captions originals and post archives", () => PinterestMedia(ffmpeg, "mp4", story: true)),
        ("Native Pinterest story HLS preserves complete media and original caption timing", () => PinterestMedia(ffmpeg, "hls", story: true)),
        ("Native Pinterest story multi-page selection transfers only the original selected asset", () => PinterestMedia(ffmpeg, "multi", story: true)),
        ("Native incomplete Pinterest story video cannot publish media or archive success", () => PinterestMedia(ffmpeg, "incomplete-mp4", story: true)),
        ("Native Pinterest board MP4 selection preserves source positions and skips unselected pins", () => PinterestMedia(ffmpeg, "mp4", true)),
        ("Playlist snapshot Pinterest MP4 keeps inspected selection while refreshing full media", () => PinterestMedia(ffmpeg, "mp4", true, true)),
        ("Playlist snapshot Pinterest HLS retains complete media captions and original positions", () => PinterestMedia(ffmpeg, "hls", true, true)),
        ("Native Pinterest board HLS selection retains complete media captions originals and archives", () => PinterestMedia(ffmpeg, "hls", true)),
        ("Native Pinterest HLS preserves offered child queries complete media and caption timing", () => PinterestMedia(ffmpeg, "hls")),
        ("Native incomplete Pinterest MP4 cannot publish files or archive success", () => PinterestMedia(ffmpeg, "incomplete-mp4")),
        ("Native incomplete Pinterest HLS cannot publish files or archive success", () => PinterestMedia(ffmpeg, "incomplete-hls")),
        ("Native Flickr MP4 preserves complete frames original bytes and video archives", () => FlickrMedia(ffmpeg, "mp4")),
        ("Native Flickr HLS preserves complete playback and offered child queries", () => FlickrMedia(ffmpeg, "hls")),
        ("Native incomplete Flickr playback cannot publish outputs or archive success", () => FlickrMedia(ffmpeg, "incomplete")),
        ("Native Twitter MP4 preserves complete playback original bytes and post archives", () => TwitterMedia(ffmpeg, "mp4")),
        ("Native Twitter HLS preserves full audio video and literal access parameters", () => TwitterMedia(ffmpeg, "hls")),
        ("Native Twitter animated GIF video stays silent with complete frames", () => TwitterMedia(ffmpeg, "gif")),
        ("Native Twitter multi-media selection transfers only the requested original position", () => TwitterMedia(ffmpeg, "multi")),
        ("Native Loom MP4 preserves full frames original bytes automatic captions and archives", () => LoomMedia(ffmpeg, "mp4")),
        ("Native Loom relative HLS resources retain scoped query credentials and complete playback", () => LoomMedia(ffmpeg, "hls")),
        ("Native incomplete Loom playback cannot publish files or change archive state", () => LoomMedia(ffmpeg, "incomplete")),
        ("Native Panopto podcast preserves frames original bytes captions and chapters", () => PanoptoMedia(ffmpeg, "podcast")),
        ("Native Panopto HLS preserves complete audio video captions and chapters", () => PanoptoMedia(ffmpeg, "hls")),
        ("Native incomplete Panopto media cannot publish files or update its archive", () => PanoptoMedia(ffmpeg, "incomplete")),
        ("Native Panopto long chapters and replacement captions retain valid MP4 chapter references", () => PanoptoMedia(ffmpeg, "timescale")),
        ("Native MP4 chapters and captions remain valid through later metadata and MOV remux processing", () => MovieClockPersistence(ffmpeg)),
        ("Native movie chapter gaps retain leading silence and exact named chapter intervals", () => MovieClockPersistence(ffmpeg, true)),
        ("Native Panopto folder selection preserves complete media positions originals and archives", () => PanoptoCollectionMedia(ffmpeg, false)),
        ("Native Panopto playlist selection preserves complete media positions originals and archives", () => PanoptoCollectionMedia(ffmpeg, true)),
        ("Native merge preference preserves H264 AAC packets using the Matroska fallback", () => MergePreferenceMedia(ffmpeg, "mkv")),
        ("Native merge preference preserves VP9 Opus packets using WebM", () => MergePreferenceMedia(ffmpeg, "webm")),
        ("Native merge preference honors the saved Ogg container with Theora Vorbis", () => MergePreferenceMedia(ffmpeg, "ogg")),
        ("Native merge preference leaves a single complete MP4 byte-for-byte unchanged", () => MergeUnpairedMedia(ffmpeg)),
        ("Native merge preference resolves unknown codecs before choosing and protecting the final filename", () => MergePreferenceMedia(ffmpeg, "mkv", unknown: true)),
        ("Native incompatible merge fails without publishing callbacks or archive entries", () => MergePreferenceMedia(ffmpeg, "mkv", unknown: true, failure: true)),
        ("Native HLS merge preference uses codec fallback and protects processed output", () => HlsRenditions(ffmpeg, "keep", "webm/mkv")),
        ("Native DASH merge preference uses codec fallback and protects processed output", () => DashMedia(ffmpeg, true, mergeFormat: "webm/mkv")),
        ("Native adaptive downloads merge real video/audio and retain literal metadata", () => Media(ffmpeg, "merge")),
        ("Concurrent native merges sharing an output stem preserve complete files", () => Media(ffmpeg, "concurrent")),
        ("Native video-only processing removes audio from a combined stream", () => Media(ffmpeg, "video")),
        ("Native audio conversion honors a fixed MP3 bitrate", () => Media(ffmpeg, "audio")),
        ("Best Matroska AAC extraction preserves codec delay, packets and decoded samples", () => AutomaticAudio(ffmpeg, "aac")),
        ("Best MP4 AAC extraction uses M4A and preserves packets and decoded samples", () => AutomaticAudio(ffmpeg, "aac", true)),
        ("Best MP3 extraction preserves source audio packets and samples", () => AutomaticAudio(ffmpeg, "libmp3lame")),
        ("Best Matroska Opus extraction preserves codec delay, packets and decoded samples", () => AutomaticAudio(ffmpeg, "libopus")),
        ("Best Ogg Opus extraction preserves source packets and decoded samples", () => AutomaticAudio(ffmpeg, "libopus", true)),
        ("Best Matroska Vorbis extraction preserves codec delay, packets and decoded samples", () => AutomaticAudio(ffmpeg, "libvorbis")),
        ("Best Ogg Vorbis extraction preserves source packets and decoded samples", () => AutomaticAudio(ffmpeg, "libvorbis", true)),
        ("Best FLAC extraction preserves lossless packets and samples", () => AutomaticAudio(ffmpeg, "flac")),
        ("Best 24-bit PCM extraction uses WAV without reducing sample precision", () => AutomaticAudio(ffmpeg, "pcm_s24le")),
        ("Audio extraction honors the source default track before channel-count preference", () => DefaultAudio(ffmpeg)),
        ("Explicit AAC extraction creates tagged ADTS audio", () => AudioConversion(ffmpeg, "aac", "128K")),
        ("Explicit M4A extraction creates decodable AAC audio", () => AudioConversion(ffmpeg, "m4a", "5")),
        ("Opus extraction honors an explicit fixed bitrate", () => AudioConversion(ffmpeg, "opus", "128K")),
        ("Vorbis extraction creates decodable Ogg audio", () => AudioConversion(ffmpeg, "vorbis", "5")),
        ("FLAC conversion preserves 24-bit PCM samples", () => AudioConversion(ffmpeg, "flac", "5")),
        ("WAV conversion preserves 24-bit PCM samples", () => AudioConversion(ffmpeg, "wav", "5")),
        ("ALAC conversion preserves 24-bit PCM samples", () => AudioConversion(ffmpeg, "alac", "5")),
        ("AAC quality levels keep the highest-to-lowest direction", () => AudioQualityDirection(ffmpeg, "m4a")),
        ("Vorbis quality levels keep the highest-to-lowest direction", () => AudioQualityDirection(ffmpeg, "vorbis")),
        ("Opus quality levels use valid VBR targets in the requested direction", () => AudioQualityDirection(ffmpeg, "opus")),
        ("Best-format no-overwrite checks protect the resolved destination and clean staging", () => AudioNoOverwrite(ffmpeg)),
        ("Audio extraction rejects silent video while preserving published files and archive", () => AudioMissing(ffmpeg)),
        ("Automatic audio naming cannot mistake the download archive for a skipped media file", () => AudioArchiveCollision(ffmpeg)),
        ("Keep-original saves converted audio and exact source bytes in separate template folders", () => KeepOriginal(ffmpeg, "folders")),
        ("Keep-original resolves same-container audio names without overwriting the source", () => KeepOriginal(ffmpeg, "same-audio")),
        ("Keep-original preserves combined media when extracting video-only output", () => KeepOriginal(ffmpeg, "video")),
        ("Keep-original avoids duplicating an unchanged direct download", () => KeepOriginal(ffmpeg, "noop")),
        ("Keep-original preserves separate adaptive video and audio components", () => KeepPair(ffmpeg, false)),
        ("Keep-original retains components and merged source before audio conversion", () => KeepPair(ffmpeg, true)),
        ("No-overwrite fills a missing original without reprocessing existing audio", () => KeepExisting(ffmpeg, true)),
        ("No-overwrite preserves an existing original and reports only newly converted audio", () => KeepExisting(ffmpeg, false)),
        ("A locked retained original rolls back audio and sidecars across output directories", () => KeepRollback(ffmpeg)),
        ("Concurrent conversions sharing a retained original commit without deadlocking", () => KeepConcurrent(ffmpeg)),
        ("Keep-original retains decodable separate HLS renditions", () => HlsRenditions(ffmpeg, "keep")),
        ("Keep-original retains decodable segmented DASH components", () => DashMedia(ffmpeg, true)),
        ("Keep-original retains decodable native YouTube SABR components", () => SabrMedia(ffmpeg, "pair", true)),
        ("Archive-write failures retain and report the complete keep-original output set", () => KeepArchiveFailure(ffmpeg)),
        ("Native encrypted HLS output decodes as real media", () => HlsMedia(ffmpeg, encrypted: true)),
        ("Native fragmented MP4 HLS output retains initialization and decodes", () => HlsMedia(ffmpeg, encrypted: false)),
        ("Native segmented DASH downloads merge and decode real tracks", () => DashMedia(ffmpeg)),
        ("Native indexed DASH preserves MP4 file layout and decodes both tracks", () => IndexedDashMedia(ffmpeg)),
        ("Parallel encrypted HLS retains complete decoded video frames", () => HlsMedia(ffmpeg, true, 3)),
        ("Parallel fragmented HLS retains initialization and all decoded video frames", () => HlsMedia(ffmpeg, false, 3)),
        ("Parallel segmented DASH merges complete tracks and retains original components", () => DashMedia(ffmpeg, true, 3)),
        ("Parallel indexed DASH preserves complete decoded tracks", () => IndexedDashMedia(ffmpeg, 3)),
        ("Native Vimeo clear media retains complete frames, captions and metadata alongside protected alternatives", () => VimeoMedia(ffmpeg, false)),
        ("Native Vimeo supplied signed file links retain complete decoded media", () => VimeoMedia(ffmpeg, true)),
        ("Native Reddit HLS keeps complete video and audio instead of its silent fallback", () => RedditMedia(ffmpeg, false)),
        ("Native Reddit silent GIF-like MP4 retains exact media and all frames", () => RedditMedia(ffmpeg, true)),
        ("Native Odysee metadata processing preserves complete media, original bytes and timecode", () => OdyseeTimecode(ffmpeg, false)),
        ("Native Bilibili adaptive merge retains all frames, literal access URLs and originals", () => BilibiliMedia(ffmpeg, "pair")),
        ("Native Bilibili video-only selection retains every frame without audio requests", () => BilibiliMedia(ffmpeg, "video")),
        ("Native Bilibili audio-only selection retains every frame without video requests", () => BilibiliMedia(ffmpeg, "audio")),
        ("Native Bilibili rejects corrupt input before requesting companion audio or merging", () => BilibiliMedia(ffmpeg, "corrupt")),
        ("Native Bilibili video-only download rejects corrupt media before publication", () => BilibiliMedia(ffmpeg, "corrupt-video")),
        ("Native Bilibili audio-only download rejects corrupt media before publication", () => BilibiliMedia(ffmpeg, "corrupt-audio")),
        ("Native Bilibili rejects a resource that lacks its declared media role", () => BilibiliMedia(ffmpeg, "wrong-role")),
        ("Native Bilibili rejects an initialization-only MP4 with no decoded frames", () => BilibiliMedia(ffmpeg, "empty-video")),
        ("Native Bilibili rejects a decodable short resource advertised as a complete longer video", () => BilibiliMedia(ffmpeg, "short-video")),
        ("Native Bilibili complete progressive MP4 decodes before preserving exact source bytes", () => BilibiliProgressive(ffmpeg, "mp4")),
        ("Native Bilibili complete progressive FLV decodes before preserving exact source bytes", () => BilibiliProgressive(ffmpeg, "flv")),
        ("Native Bilibili selected multipart playback publishes only a verified part and archive", () => BilibiliProgressive(ffmpeg, "playlist")),
        ("Native Bilibili ordered MP4 files join all frames and retain exact originals", () => BilibiliSequence(ffmpeg, "mp4")),
        ("Native Bilibili ordered FLV files join all frames and retain exact originals", () => BilibiliSequence(ffmpeg, "flv")),
        ("Native Bilibili ordered files support complete video-only output", () => BilibiliSequence(ffmpeg, "video")),
        ("Native Bilibili ordered files support complete audio-only output", () => BilibiliSequence(ffmpeg, "audio")),
        ("Native Bilibili ordered files honor independent fragment retries", () => BilibiliSequence(ffmpeg, "retry")),
        ("Native Bilibili parallel ordered transfers retain frame order and drain workers", () => BilibiliSequence(ffmpeg, "parallel")),
        ("Native Bilibili corrupt ordered files preserve previous output and archive", () => BilibiliSequence(ffmpeg, "corrupt")),
        ("Native Bilibili missing ordered files cannot publish a partial video", () => BilibiliSequence(ffmpeg, "missing")),
        ("Native Bilibili ordered files reject declared-size mismatches", () => BilibiliSequence(ffmpeg, "size")),
        ("Native Bilibili ordered files reject incompatible decoder configurations", () => BilibiliSequence(ffmpeg, "incompatible")),
        ("Native Bilibili ordered download cancellation preserves prior output", () => BilibiliSequence(ffmpeg, "cancel")),
        ("Native Rumble MP4 playback preserves complete frames metadata and original bytes", () => RumbleMedia(ffmpeg, "mp4")),
        ("Native Rumble WebM playback verifies and retains complete selected media", () => RumbleMedia(ffmpeg, "webm")),
        ("Native Rumble archive-backed HLS preserves literal queries and every media frame", () => RumbleMedia(ffmpeg, "hls")),
        ("Native Rumble HLS audio-only output retains every offered audio frame", () => RumbleMedia(ffmpeg, "hls-audio")),
        ("Native Rumble HLS video-only output retains every offered video frame", () => RumbleMedia(ffmpeg, "hls-video")),
        ("Native Rumble shortened HLS cannot publish incomplete playback or archive it", () => RumbleMedia(ffmpeg, "hls-short")),
        ("Native Rumble channel selection preserves full media positions metadata and archives", () => RumbleCollection(ffmpeg, false)),
        ("Native Rumble channel unavailable entries preserve successful selected downloads", () => RumbleCollection(ffmpeg, true)),
        ("Structured media encoding selection preserves complete frames and exact originals", () => StructuredMedia(ffmpeg, "encodings")),
        ("Structured audio downloads retain every frame and native metadata", () => StructuredMedia(ffmpeg, "audio")),
        ("Structured media collections transfer only selected complete videos", () => StructuredMedia(ffmpeg, "collection")),
        ("Native NFB original playback preserves all frames signed URLs and manifest captions", () => NfbMedia(ffmpeg, false)),
        ("Native NFB described-audio selection preserves full media and captions", () => NfbMedia(ffmpeg, true)),
        ("Native NFB verifies the complete manifest timeline instead of rounded catalog duration", () => NfbMedia(ffmpeg, false, true)),
        ("Native NFB series selection retains full media captions positions and known archive skips", () => NfbMedia(ffmpeg, false, series: true)),
        ("Native Imgur selected album video preserves audio frames originals and archive identity", () => ImgurMedia(ffmpeg, true)),
        ("Native Imgur silent video completes without inventing an audio track", () => ImgurMedia(ffmpeg, false)),
        ("Native Imgur incomplete video cannot publish or archive an album entry", () => ImgurMedia(ffmpeg, true, true)),
        ("Native Bluesky original upload preserves media captions and per-author archives", () => BlueskyMedia(ffmpeg, false)),
        ("Native Bluesky HLS retains all video frames original segments and supplied captions", () => BlueskyMedia(ffmpeg, true)),
        ("Native Bluesky silent original completes without inventing audio", () => BlueskyMedia(ffmpeg, false, true)),
        ("Native Bluesky mismatched original size preserves prior output and archive", () => BlueskyMedia(ffmpeg, false, wrongSize: true)),
        ("Native selection limits count complete media items with multiple retained outputs", () => LimitedOriginals(ffmpeg)),
        ("Observed YouTube sessions reach native media transfer with exact client context and complete originals", () => SabrMedia(ffmpeg, "pair", keep: true, observed: true)),
        ("Native recode MP4 conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "mp4")),
        ("Native recode Matroska conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "mkv")),
        ("Native recode WebM conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "webm")),
        ("Native recode AVI conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "avi")),
        ("Native recode FLV conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "flv")),
        ("Native recode Ogg conversion preserves full frames and originals", () => RecodeMedia(ffmpeg, "ogg")),
        ("Native recode source captions survive compatible container conversion", () => RecodeMedia(ffmpeg, "mp4", auxiliary: true)),
        ("Native recode odd video dimensions are retained without scaling", () => RecodeMedia(ffmpeg, "mp4", odd: true)),
        ("Native recode corrupt input preserves prior outputs and archive state", () => RecodeMedia(ffmpeg, "webm", corrupt: true)),
        ("Native recode preserves multiple audio tracks and their languages", () => RecodeMedia(ffmpeg, "webm", multipleAudio: true)),
        ("Native recode silent video does not gain an audio stream", () => RecodeMedia(ffmpeg, "webm", silent: true)),
        ("Native recode display rotation is baked once into the output geometry", () => RecodeMedia(ffmpeg, "mp4", rotated: true)),
        ("Native recode already-target formats retain encoded video packets", () => RecodeMedia(ffmpeg, "mp4", same: true)),
        ("Native recode cancellation drains the encoder and preserves published files", () => CancelRecode(ffmpeg)),
        ("Wikimedia complete video alternatives preserve frames originals and captions", () => WikimediaMedia(ffmpeg, "video")),
        ("Wikimedia silent video alternatives complete without adding audio", () => WikimediaMedia(ffmpeg, "silent")),
        ("Wikimedia audio originals decode and preserve all source frames", () => WikimediaMedia(ffmpeg, "audio")),
        ("Wikimedia standalone caption conversion does not request media", () => WikimediaMedia(ffmpeg, "captions")),
        ("MP4 embedding preserves the complete visible timeline of overlapping WebVTT cues", () => OverlapCaptions(ffmpeg, false)),
        ("MP4 embedding preserves the complete visible timeline of overlapping SRT cues", () => OverlapCaptions(ffmpeg, true)),
        ("MP4 to MOV remux preserves drop-frame timecode packets and media frames", () => OdyseeTimecode(ffmpeg, true)),
        ("Native YouTube SABR tracks merge as complete decodable audio and video", () => SabrMedia(ffmpeg, "pair")),
        ("Native YouTube SABR video-only downloads retain every frame", () => SabrMedia(ffmpeg, "video")),
        ("Native YouTube SABR audio-only downloads retain every audio frame", () => SabrMedia(ffmpeg, "audio")),
        ("MP4 covers preserve multiple video tracks, audio, captions and chapters", () => CoverMedia(ffmpeg, "mp4")),
        ("Matroska WebP covers become image attachments without changing media", () => CoverMedia(ffmpeg, "mkv")),
        ("Automatic WebM cover remux preserves every video and audio track in Matroska", () => CoverMedia(ffmpeg, "webm")),
        ("Matroska audio cover attachments preserve JPEG pixels and audio", () => CoverMedia(ffmpeg, "mka")),
        ("MP3 covers use the audio-only picture index and replace prior artwork", () => CoverMedia(ffmpeg, "mp3")),
        ("M4A cover embedding preserves all audio frames and metadata", () => CoverMedia(ffmpeg, "m4a")),
        ("FLAC cover embedding converts WebP and preserves lossless audio", () => CoverMedia(ffmpeg, "flac")),
        ("Opus cover comments retain decoded artwork and all audio frames", () => CoverMedia(ffmpeg, "opus")),
        ("Ogg Vorbis cover comments retain decoded artwork and all audio frames", () => CoverMedia(ffmpeg, "ogg")),
        ("Malformed covers preserve published media, thumbnails and archive entries", () => CoverFailure(ffmpeg)),
        ("All-thumbnail GIF saving preserves originals and embeds the preferred first frame", () => AllThumbnailMedia(ffmpeg, "gif")),
        ("All-thumbnail BMP saving preserves originals and embeds matching pixels", () => AllThumbnailMedia(ffmpeg, "bmp")),
        ("All-thumbnail AVIF saving preserves originals and embeds matching pixels", () => AllThumbnailMedia(ffmpeg, "avif")),
        ("Native SoundCloud AAC HLS preserves complete audio without video constraints", () => SoundCloudHls(ffmpeg, false)),
        ("Native SoundCloud audio master playlists preserve complete AAC media", () => SoundCloudHls(ffmpeg, true)),
        ("Native feed episodes retain video, timed transcripts, artwork, chapters and identity sidecars", () => FeedMedia(ffmpeg)),
        ("Native static HLS timeline joins preserve every video frame across timestamp resets", () => HlsPeriods(ffmpeg, false)),
        ("Incompatible HLS timeline layouts cannot replace previously published media", () => HlsPeriods(ffmpeg, true)),
        ("HLS timeline joins preserve variable-rate video without frame duplication", () => HlsPeriods(ffmpeg, false, true)),
        ("Separate HLS timelines retain their original audio/video start offset", () => HlsPeriodOffset(ffmpeg)),
        ("Fragmented MP4 HLS timeline resets retain all decoded video and encoded audio packets", () => HlsFragmentedPeriods(ffmpeg, "combined")),
        ("Fragmented audio HLS timeline resets retain every AAC packet", () => HlsFragmentedPeriods(ffmpeg, "audio")),
        ("Fragmented HLS timeline decoder changes cannot replace published media", () => HlsFragmentedPeriods(ffmpeg, "config")),
        ("Fragmented HLS timelines preserve initialization and media byte ranges", () => HlsFragmentedPeriods(ffmpeg, "ranges")),
        ("Separate fragmented HLS timelines retain audio/video offsets and original components", () => HlsPeriodOffset(ffmpeg, true)),
        ("Fragmented HLS timelines require verifiable decoder configuration", () => HlsFragmentedPeriods(ffmpeg, "missing-config")),
        ("Separate HLS audio/video renditions merge into playable media", () => HlsRenditions(ffmpeg, "merge")),
        ("HLS audio-only downloads omit video segments", () => HlsRenditions(ffmpeg, "audio")),
        ("HLS video-only downloads omit alternate audio requests", () => HlsRenditions(ffmpeg, "video")),
        ("Native MP4 caption embedding retains separate language tracks and saved captions", () => CaptionMedia(ffmpeg, "mp4")),
        ("Native Matroska caption embedding cleans temporary language files", () => CaptionMedia(ffmpeg, "mkv")),
        ("Subtitle SRT conversion retains Unicode text and exact cue boundaries", () => SubtitleConversion(ffmpeg, "srt")),
        ("Subtitle VTT conversion retains Unicode text and exact cue boundaries", () => SubtitleConversion(ffmpeg, "vtt")),
        ("Subtitle ASS conversion retains text and centisecond-aligned cue boundaries", () => SubtitleConversion(ffmpeg, "ass")),
        ("Subtitle LRC conversion retains Unicode text and cue start times", () => SubtitleConversion(ffmpeg, "lrc")),
        ("Subtitle ASS sources convert back to timed SubRip", () => AssSubtitleConversion(ffmpeg)),
        ("Subtitle converted SRT embeds with complete media and original cue boundaries", () => ConvertedSubtitleEmbedding(ffmpeg, "srt")),
        ("Subtitle converted LRC saves lyrics while embedding original cue end times", () => ConvertedSubtitleEmbedding(ffmpeg, "lrc")),
        ("TTML originals save unchanged while native timed text embeds into complete media", () => TtmlEmbedding(ffmpeg, "")),
        ("TTML native SRT conversion embeds with exact cue boundaries and Unicode text", () => TtmlEmbedding(ffmpeg, "srt")),
        ("TTML ASS conversion bridges native timing into the retained ffmpeg pipeline", () => TtmlEmbedding(ffmpeg, "ass")),
        ("TTML LRC saving retains original cue ends in embedded captions", () => TtmlEmbedding(ffmpeg, "lrc")),
        ("MP4 TTML captions retain sample-clipped timing in saved and embedded media", () => Mp4TtmlMedia(ffmpeg)),
        ("Native DASH MP4 WebVTT converts, embeds and preserves decoded cue timing", () => DashCaptionMedia(ffmpeg)),
        ("Native ranged HLS WebVTT aligns to transport timestamps and embeds without duplicate cues", () => HlsCaptionMedia(ffmpeg, "clear")),
        ("Native AES-encrypted HLS WebVTT decrypts headers and sequenced segments", () => HlsCaptionMedia(ffmpeg, "encrypted")),
        ("Explicit HLS video/audio pairs preserve selected native subtitles", () => HlsCaptionMedia(ffmpeg, "pair")),
        ("Failed HLS caption retrieval preserves existing media, captions and archive state", () => HlsCaptionMedia(ffmpeg, "failure")),
        ("Native MP4 chapter embedding retains literal titles and boundaries", () => ChapterMedia(ffmpeg, "mp4")),
        ("Native Matroska chapter embedding cleans temporary metadata", () => ChapterMedia(ffmpeg, "mkv")),
        ("Failed ffmpeg processing preserves previously published media and sidecars", () => PublicationChecks.ProcessingFailure(ffmpeg)),
        ("Output templates use the final converted audio extension and folder", () => TemplateAudio(ffmpeg)),
        ("Native explicit format selectors merge the requested real video/audio pair", () => Media(ffmpeg, "selector")),
        ("A native single-video selector publishes real media without adding audio", () => Media(ffmpeg, "selector-video")),
        ("Video-only processing removes audio from an explicitly selected merged pair", () => Media(ffmpeg, "selector-video-pair")),
        ("Audio-only processing converts an explicitly selected merged pair", () => Media(ffmpeg, "selector-audio-pair"))
    ];

    private static async Task Cancellation()
    {
        using var files = new Files();
        using var cancellation = new CancellationTokenSource();
        var workerFile = Path.Combine(files.Directory, "worker.pid");
        const string variable = "YOUTUBEDL_NATIVE_TEST_WORKER_FILE";
        var previous = Environment.GetEnvironmentVariable(variable);
        int? processId = null;
        Task<bool>? run = null;
        try
        {
            Environment.SetEnvironmentVariable(variable, workerFile);
            var executable = Path.Combine(AppContext.BaseDirectory, "YoutubeDl.Engine.Tests.exe");
            run = FfmpegTool.IsAvailableAsync(executable, cancellation.Token);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(workerFile))
            {
                if (run.IsCompleted || DateTime.UtcNow >= deadline) throw new InvalidOperationException("The test child did not start.");
                await Task.Delay(10);
            }
            processId = int.Parse(await File.ReadAllTextAsync(workerFile));
            cancellation.Cancel();
            try { await run; throw new InvalidOperationException("Cancellation was swallowed."); }
            catch (OperationCanceledException) { }
            try
            {
                using var process = Process.GetProcessById(processId.Value);
                if (!process.HasExited) throw new InvalidOperationException("The canceled child is still running.");
            }
            catch (ArgumentException) { }
        }
        finally
        {
            cancellation.Cancel();
            if (run is not null)
            {
                try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
            }
            Environment.SetEnvironmentVariable(variable, previous);
            if (processId is { } id)
            {
                try { using var process = Process.GetProcessById(id); if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task Media(string ffmpeg, string mode)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ffmpeg))!, "ffprobe.exe");
        if (!File.Exists(ffmpeg) || !File.Exists(ffprobe)) throw new FileNotFoundException("These checks require ffmpeg and ffprobe in the supplied folder.");
        using var files = new Files();
        var video = Path.Combine(files.Directory, "source-video.mp4");
        var audio = Path.Combine(files.Directory, "source-audio.m4a");
        var combined = Path.Combine(files.Directory, "source-combined.mp4");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=128x96:r=10:d=0.3", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]);
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.3", "-vn", "-c:a", "aac", audio]);
        await FfmpegTool.MergeAsync(ffmpeg, video, audio, combined, default);
        var node = JsonNode.Parse(YouTubeChecks.FixtureJson)!;
        node["videoDetails"]!["title"] = "Fixture; artist=not-a-tag";
        var html = "var ytInitialPlayerResponse=" + node.ToJsonString() + ";";
        var videoBytes = await File.ReadAllBytesAsync(video);
        var audioBytes = await File.ReadAllBytesAsync(audio);
        var combinedBytes = await File.ReadAllBytesAsync(combined);
        var mediaRequests = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            if (request.RequestUri!.Host == "www.youtube.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
            var query = request.RequestUri.Query;
            mediaRequests.Add(query);
            var bytes = query.Contains("fixture=video", StringComparison.Ordinal) ? videoBytes
                : query.Contains("fixture=audio", StringComparison.Ordinal) ? audioBytes
                : query.Contains("fixture=combined", StringComparison.Ordinal) ? combinedBytes
                : throw new InvalidOperationException("The native plan requested the wrong stream.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        })));
        var request = new NativeDownloadRequest
        {
            Url = new Uri("https://www.youtube.com/watch?v=abcdefghijk"), OutputDirectory = files.Directory,
            FileName = "result", FfmpegPath = ffmpeg, Retries = 0, AddMetadata = mode == "merge",
            VideoOnly = mode is "video" or "selector-video-pair", AudioOnly = mode is "audio" or "selector-audio-pair", AudioFormat = "mp3", AudioQuality = "192K",
            FormatId = mode == "video" ? "18" : mode == "audio" ? "140:en.0" : null,
            FormatSelector = mode is "selector" or "selector-video-pair" or "selector-audio-pair" ? "137+140:en.0" : mode == "selector-video" ? "137" : ""
        };
        NativeDownloadResult result;
        if (mode == "concurrent")
        {
            var results = await Task.WhenAll(engine.DownloadAsync(request, null, null, default), engine.DownloadAsync(request, null, null, default));
            if (results.Any(item => !item.Succeeded)) throw new InvalidOperationException("One concurrent native merge failed.");
            result = results[0];
        }
        else result = await engine.DownloadAsync(request, null, null, default);
        if (!result.Succeeded || result.OutputPath is null) throw new InvalidOperationException(result.Error);
        var probe = await Run(ffprobe, ["-v", "error", "-show_entries", "stream=codec_type,bit_rate:format_tags=title,artist", "-of", "json", result.OutputPath]);
        using var document = JsonDocument.Parse(probe);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var types = streams.Select(stream => stream.GetProperty("codec_type").GetString()).ToArray();
        if (mode == "merge" || mode == "concurrent" || mode == "selector")
        {
            if (!types.Contains("video") || !types.Contains("audio") || mediaRequests.Count != (mode == "concurrent" ? 4 : 2))
                throw new InvalidOperationException("The merged result lost a track or fetched the wrong streams.");
            if (mode == "merge")
            {
                var tags = document.RootElement.GetProperty("format").GetProperty("tags");
                if (tags.GetProperty("title").GetString() != "Fixture; artist=not-a-tag" || tags.GetProperty("artist").GetString() != "Test creator")
                    throw new InvalidOperationException("Literal metadata changed the artist tag.");
            }
        }
        else if (mode is "video" or "selector-video" or "selector-video-pair" && (types.Length != 1 || types[0] != "video"))
            throw new InvalidOperationException("Video-only output retained audio.");
        else if (mode is "audio" or "selector-audio-pair" && (types.Length != 1 || types[0] != "audio" || streams[0].GetProperty("bit_rate").GetString() != "192000"))
            throw new InvalidOperationException("Audio-only output or bitrate is wrong.");
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", result.OutputPath, "-f", "null", "-"]);
        if (System.IO.Directory.EnumerateFiles(files.Directory).Any(path => Path.GetFileName(path).StartsWith(".process-", StringComparison.Ordinal) || Path.GetFileName(path).StartsWith(".merge-", StringComparison.Ordinal)))
            throw new InvalidOperationException("Processing staging files survived successful completion.");
    }

    internal static async Task<string> Run(string executable, IReadOnlyList<string> arguments, bool rejectDiagnostics = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        var finalArgument = arguments.LastOrDefault();
        if (finalArgument is not null && Path.IsPathRooted(finalArgument)) start.WorkingDirectory = Path.GetDirectoryName(finalArgument)!;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the media test tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await Task.WhenAll(output, error);
        var diagnostic = await error;
        if (process.ExitCode != 0 || rejectDiagnostics && !string.IsNullOrWhiteSpace(diagnostic)) throw new InvalidOperationException("Media tool failed: " + diagnostic);
        return await output;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class Files : IDisposable
    {
        public string Directory { get; }
        private readonly bool _cleanup;
        public Files(string? directory=null,bool cleanup=true)
        {
            Directory=directory??Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));_cleanup=cleanup;
            var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"test-data"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(Directory).StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidOperationException("The test directory escaped its owned root.");
            System.IO.Directory.CreateDirectory(Directory);
        }
        public void Dispose()
        {
            if(!_cleanup)return;
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Directory);
            if (!target.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("The test directory escaped its owned root.");
            System.IO.Directory.Delete(target, recursive: true);
        }
    }
}
