using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Panopto;

public sealed partial class PanoptoProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("panopto", "Panopto recordings, folders, playlists and embedded players", 200);
    public bool CanHandle(Uri url) => PanoptoAddress.Matches(url) || PanoptoCollectionAddress.IsList(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (PanoptoCollectionAddress.Matches(request.Url))
        {
            var collection = PanoptoCollectionAddress.Parse(request.Url);
            if (!request.DownloadPlaylist && collection.VideoId is not null)
                return await ExtractAsync(request with { Url = PanoptoCollectionAddress.Single(request.Url) }, http, ct).ConfigureAwait(false);
            return await ReadCollectionAsync(request, collection, http, ct).ConfigureAwait(false);
        }
        var address = PanoptoAddress.Parse(request.Url);
        var headers = new Dictionary<string, string> { ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? address.Page.AbsoluteUri : request.Referer,
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent };
        using var session = http.WithHeaders(headers);
        var page = await session.GetDocumentAsync(address.Page, 3 * 1024 * 1024, ct).ConfigureAwait(false);
        address.CheckPage(page.Url);
        var html = PageMetadataReader.Read(page.Text, page.Url);
        using var json = await ReadAsync(session, address, new() { ["deliveryId"] = address.Id, ["isEmbed"] = "true", ["responseType"] = "json" }, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        var root = json.RootElement; var delivery = Object(root, "Delivery");
        if (delivery.ValueKind != JsonValueKind.Object) throw new NotSupportedException("Panopto did not provide a playable recording. It may require sign-in or permission.");
        if (GuidValue(delivery, "PublicID") != address.Id) throw new InvalidDataException("Panopto returned another delivery identity.");
        var sessionId = GuidValue(delivery, "SessionPublicID");
        if (GuidValue(root, "SessionId") != sessionId || GuidValue(delivery, "SessionFileId") != sessionId)
            throw new InvalidDataException("Panopto returned inconsistent session identities.");
        if (True(delivery, "IsActiveBroadcast") || True(delivery, "IsBroadcast") && !True(delivery, "BroadcastEnded"))
            throw new NotSupportedException("Native live Panopto recordings are not supported yet.");
        if (True(delivery, "IsPurgedEncode") || True(delivery, "IsPurgedLegacyEncode") || True(delivery, "IsReferenceCopy") ||
            True(delivery, "DrmProtected") || True(delivery, "RequiresDrm")) throw new NotSupportedException("Panopto did not expose an ordinary complete clear recording.");
        var duration = Number(delivery, "Duration");
        if (duration is not > 0 || duration > 7 * 24 * 3600) throw new InvalidDataException("Panopto omitted a supported complete recording duration.");
        var title = Text(delivery, "SessionName");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 4096) throw new InvalidDataException("Panopto omitted the recording title.");
        var media = new List<MediaCandidate>(); var unavailable = new List<NativeFormat>(); var warnings = new List<string>();
        var podcasts = Array(delivery, "PodcastStreams"); var originals = Array(delivery, "Streams");
        if (podcasts.Length > 32 || originals.Length > 32) throw new InvalidDataException("Panopto returned too many source streams.");
        foreach (var stream in podcasts)
        {
            var kind = Text(stream, "ViewerMediaFileTypeName");
            var audioOnly = kind is "mp3" or "m4a" or "aac" || True(delivery, "IsPrimaryAudioOnly");
            if (!(audioOnly ? True(delivery, "IsAudioPodcastEncodeComplete") : True(delivery, "IsPodcastEncodeComplete")))
            { warnings.Add("A Panopto podcast encode is incomplete and was excluded."); continue; }
            Add(stream, "podcast", audioOnly, fallback: false);
        }
        foreach (var stream in originals)
        {
            if (originals.Length != 1 || True(delivery, "HasSplices") || Array(stream, "RelativeSegments").Length > 0)
            {
                unavailable.Add(new() { Id = "source-" + GuidValue(stream, "PublicID"), Extension = Text(stream, "ViewerMediaFileTypeName"),
                    Availability = FormatAvailability.AdaptiveTransportRequired, UnavailableReason = "This source needs Panopto multi-view or edit-timeline composition. Choose a complete podcast encode when available." });
                continue;
            }
            Add(stream, "source", True(delivery, "IsPrimaryAudioOnly"), fallback: podcasts.Length > 0);
        }
        if (media.Count == 0) throw new NotSupportedException("Panopto exposed no supported complete recording. Multi-view composition, edited source timelines or this encoding still need support.");
        var subtitles = CaptionTracks(delivery, address, warnings);
        var selected = SubtitleSelection.Select(subtitles, request);
        if (selected.Count > 16) throw new NotSupportedException("At most sixteen Panopto caption languages can be fetched per recording.");
        var resolved = new Dictionary<SubtitleTrack, string>();
        foreach (var track in selected)
        {
            ct.ThrowIfCancellationRequested();
            using var captions = await ReadAsync(session, address, new() { ["deliveryId"] = address.Id, ["getCaptions"] = "true", ["language"] = LanguageId(track.Language).ToString(CultureInfo.InvariantCulture), ["responseType"] = "json" }, 2 * 1024 * 1024, ct).ConfigureAwait(false);
            resolved.Add(track, RenderCaptions(captions.RootElement, duration.Value, sessionId));
        }
        return new() { Id = address.Id, Extractor = Info.Id, ArchiveIdentity = new("panopto:delivery", address.Page.Host + "/" + address.Id),
            Title = WebUtility.HtmlDecode(title), Description = Text(delivery, "SessionAbstract"), Uploader = Text(delivery, "OwnerDisplayName"),
            DurationSeconds = duration, Media = media, ReportedFormats = unavailable, Warnings = warnings, SourcePageUrl = address.Page,
            ThumbnailUrl = Thumbnail(html.ThumbnailUrl, sessionId), Chapters = Chapters(delivery, duration.Value),
            Subtitles = subtitles.Select(track => new SubtitleTrack { Url = track.Url, Language = track.Language, Extension = "vtt", Content = resolved.GetValueOrDefault(track) }).ToArray(),
            AnonymousPlayback = true, PlaybackHeaders = headers };

        void Add(JsonElement stream, string role, bool audioOnly, bool fallback)
        {
            var id = GuidValue(stream, "PublicID"); var type = Text(stream, "ViewerMediaFileTypeName");
            if (True(stream, "Interrupted") || Number(stream, "RelativeStart") is not { } start || start > 0.001 ||
                Number(stream, "RelativeEnd") is not { } end || Math.Abs(end - duration.Value) > 0.1)
                throw new NotSupportedException("Panopto supplied an incomplete or offset source timeline.");
            var kind = type switch { "mp4" or "mp3" or "m4a" => MediaKind.Progressive, "hls" => MediaKind.Hls, _ => MediaKind.Unknown };
            if (kind == MediaKind.Unknown) { warnings.Add("An unsupported Panopto source encoding was excluded."); return; }
            var url = Resource(Text(stream, "StreamUrl"), sessionId, address.Id);
            media.Add(new() { Id = role + "-" + id, Url = url, Extension = type == "hls" ? "ts" : type, Kind = kind,
                HasVideo = !audioOnly, HasAudio = true, IsCompleteMedia = true, IsFallback = fallback, Source = "panopto", Label = role + " " + type,
                VerificationDemuxer = type switch { "mp4" or "m4a" => "mov", "mp3" => "mp3", _ => "mpegts" }, VerificationDurationSeconds = duration });
        }
    }

    private static async Task<JsonDocument> ReadAsync(MediaHttp http, PanoptoAddress address, Dictionary<string, string> fields, int limit, CancellationToken ct)
    {
        var document = await http.PostFormDocumentAsync(address.Api, fields, limit, ct).ConfigureAwait(false);
        if (document.Url.GetLeftPart(UriPartial.Authority) != address.Origin || document.Url.AbsolutePath != address.Api.AbsolutePath)
            throw new NotSupportedException("Panopto redirected its playback request. Sign-in or a different player layout may be required.");
        JsonDocument json;
        try { json = JsonDocument.Parse(document.Text, new() { MaxDepth = 48 }); }
        catch (JsonException) { throw new NotSupportedException("Panopto returned no supported playback document. Sign-in or permission may be required."); }
        if (json.RootElement.ValueKind == JsonValueKind.Object && (Integer(json.RootElement, "ErrorCode") is > 0 || True(json.RootElement, "Error")))
        { json.Dispose(); throw new NotSupportedException("Panopto refused this playback request. Check that the recording is available to this session."); }
        return json;
    }
    private static string GuidValue(JsonElement element, string name) => Text(element, name) is { } text && Guid.TryParseExact(text, "D", out var value) && value != Guid.Empty
        ? value.ToString("D") : throw new InvalidDataException("Panopto omitted a valid " + name + " identity.");
    private static Uri Resource(string? value, string session, string delivery)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Panopto returned an unsupported media address.");
        var parts = Uri.UnescapeDataString(uri.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "sessions" || parts[1] != session || !parts[2].StartsWith(delivery + "-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Panopto media belongs to another recording or session.");
        return uri;
    }
    private static string? Thumbnail(string? value, string session)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 3 && parts[0] == "sessions" && parts[1] == session ? uri.AbsoluteUri : null;
    }
}
