using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.SoundCloud;

public sealed partial class SoundCloudProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("soundcloud", "SoundCloud tracks, sets, profiles, related tracks, searches and embedded players", 200);
    public bool CanHandle(Uri url) => SoundCloudAddress.IsHost(url);
    public Download.DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = SoundCloudAddress.Parse(request.Url);
        return address.Collection is null && !address.Playlist && address.ApiId is { } id ? new("soundcloud", id) : null;
    }

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = SoundCloudAddress.Parse(request.Url);
        if (address.Collection is not null) return await InventoryAsync(address, request, http, ct).ConfigureAwait(false);
        if (address.Playlist || address.ShortLink) return await ReadSingleAsync(request, http, ct).ConfigureAwait(false);
        return await http.SessionState("playback:soundcloud:" + address.Url.AbsoluteUri,
            () => new ProviderPlaybackSession(http, request, "soundcloud", (controlled, token) => ReadSingleAsync(request, controlled, token), reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }

    private static async Task<PageMetadata> ReadSingleAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = SoundCloudAddress.Parse(request.Url);
        var document = await http.GetDocumentAsync(address.ApiId is null ? address.Url : new Uri("https://soundcloud.com/"), 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url.Host is not ("soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com") || document.Url.Scheme != "https" || !document.Url.IsDefaultPort || document.Url.UserInfo.Length != 0)
            throw new InvalidDataException("The SoundCloud page redirected outside its supported site.");
        if (address.ApiId is null) address = SoundCloudAddress.Parse(document.Url);
        if (address.Collection is not null) throw new InvalidDataException("The SoundCloud media redirected to a collection.");
        var page = SoundCloudPage.Read(document.Text);
        var client = page.ClientId ?? throw new NotSupportedException("The SoundCloud page did not supply a supported public playback client.");
        var item = page.Item;
        if (address.ApiId is not null)
        {
            item = await ReadApiAsync(http, SoundCloudAddress.WithQuery(address.Url, ("client_id", client)), ct).ConfigureAwait(false);
            if (Id(item) != address.ApiId) throw new InvalidDataException("SoundCloud returned another media identity.");
        }
        ValidateIdentity(item, address);
        return address.Playlist ? await PlaylistAsync(item, client, request, http, ct).ConfigureAwait(false) :
            await TrackAsync(item, client, http, ct).ConfigureAwait(false);
    }

    private static void ValidateIdentity(JsonElement item, SoundCloudAddress address)
    {
        _ = Id(item);
        if (Text(item, "kind") != (address.Playlist ? "playlist" : "track")) throw new InvalidDataException("SoundCloud returned the wrong media type.");
        var permalink = Permalink(item, address.Playlist);
        if (address.ApiId is null && permalink.AbsolutePath.TrimEnd('/') != address.Url.AbsolutePath.TrimEnd('/'))
            throw new InvalidDataException("The SoundCloud page data belongs to another address.");
    }

    private static PageMetadata Metadata(JsonElement item) => new()
    {
        Id = Id(item), Extractor = "soundcloud", Title = Text(item, "title") ?? Id(item), Description = Text(item, "description"),
        DurationSeconds = (Number(item, "full_duration") ?? Number(item, "duration")) / 1000,
        Uploader = Text(Object(item, "user"), "username"),
        ThumbnailUrl = Image(Text(item, "artwork_url") ?? Text(Object(item, "user"), "avatar_url"))
    };

    private static async Task<PageMetadata> TrackAsync(JsonElement track, string client, MediaHttp http, CancellationToken ct)
    {
        var metadata = Metadata(track);
        if (Unavailable(track) is { } reason) return metadata with { Warnings = [reason] };
        var sources = Array(Object(track, "media"), "transcodings");
        if (sources.Length > 100) throw new InvalidDataException("SoundCloud returned too many stream formats.");
        var media = new List<MediaCandidate>(); var formats = new List<NativeFormat>(); var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var authorization = Text(track, "track_authorization");
        if (authorization?.Length > 8192) throw new InvalidDataException("The SoundCloud playback authorization exceeds its limit.");
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var format = Object(source, "format"); var protocol = Text(format, "protocol") ?? "unknown";
            var preset = Text(source, "preset") ?? "format";
            if (!Regex.IsMatch(preset, @"\A[A-Za-z0-9_-]{1,128}\z") || !Regex.IsMatch(protocol, @"\A[A-Za-z0-9_-]{1,64}\z")) throw new InvalidDataException("SoundCloud returned an invalid format name.");
            var baseId = preset + "-" + protocol;
            var id = baseId;
            for (var suffix = 2; !identifiers.Add(id); suffix++) id = baseId + "-" + suffix;
            var mime = Text(format, "mime_type");
            if (!MediaTypeHeaderValue.TryParse(mime, out var contentType)) throw new InvalidDataException("SoundCloud returned an invalid media type.");
            var type = contentType.MediaType?.ToLowerInvariant();
            var extension = type switch { "audio/mpeg" => "mp3", "audio/mp4" or "audio/aac" => "m4a", "audio/ogg" => "ogg", _ => null };
            var codec = type switch { "audio/mpeg" => "mp3", "audio/mp4" or "audio/aac" => "aac", "audio/ogg" when mime!.Contains("opus") => "opus", _ => null };
            var match = Regex.Match(preset, @"(?:\A|_)(?<rate>[0-9]{2,3})k(?:_|\z)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            double? bitrate = match.Success ? int.Parse(match.Groups["rate"].Value, CultureInfo.InvariantCulture) * 1000 : null;
            string? unavailable = True(source, "snipped") || Number(source, "duration") is { } duration && metadata.DurationSeconds is { } full && duration + 1000 < full * 1000
                ? "SoundCloud offered only a preview for this format." : protocol is not ("hls" or "progressive") ? "This SoundCloud streaming protocol is not supported: " + protocol : null;
            if (unavailable is null && (extension is null && (protocol != "hls" || type is not ("audio/mpegurl" or "application/vnd.apple.mpegurl" or "application/x-mpegurl"))))
                unavailable = "This SoundCloud media type is not supported: " + type;
            Uri? url = null;
            if (unavailable is null)
            {
                var api = ApiUrl(Text(source, "url"));
                if (!api.AbsolutePath.StartsWith("/media/", StringComparison.Ordinal)) throw new InvalidDataException("SoundCloud returned an unexpected stream endpoint.");
                try
                {
                    var reply = await ReadApiAsync(http, SoundCloudAddress.WithQuery(api, ("client_id", client), ("track_authorization", authorization)), ct).ConfigureAwait(false);
                    if (!Uri.TryCreate(Text(reply, "url"), UriKind.Absolute, out url) || !MediaUrl(url)) throw new InvalidDataException("SoundCloud returned an invalid media address.");
                }
                catch (HttpRequestException) { unavailable = "SoundCloud did not grant access to this stream format."; }
                catch (TimeoutException) { unavailable = "The SoundCloud stream lookup timed out."; }
            }
            formats.Add(new() { Id = id, Url = url, MimeType = mime, Extension = extension, HasAudio = true, HasVideo = false, AudioOnly = true,
                AudioCodec = codec, Bandwidth = bitrate, Protocol = protocol == "hls" ? "m3u8" : "https", Note = Text(source, "quality"),
                Availability = unavailable is null ? FormatAvailability.Ready : FormatAvailability.MissingUrl, UnavailableReason = unavailable });
            if (url is not null && unavailable is null) media.Add(new() { Id = id, Url = url, Kind = protocol == "hls" ? MediaKind.Hls : MediaKind.Progressive,
                Extension = extension, MimeType = mime, HasAudio = true, HasVideo = false, AudioCodec = codec, Bandwidth = bitrate, Source = "soundcloud" });
        }
        return metadata with { Media = media, ReportedFormats = formats,
            Warnings = sources.Length == 0 ? ["SoundCloud did not expose playable streams for this track."] : formats.Where(format => format.UnavailableReason is not null).Select(format => format.UnavailableReason!).Distinct().ToArray() };
    }

    private static async Task<PageMetadata> PlaylistAsync(JsonElement item, string client, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        var tracks = Array(item, "tracks"); var count = Integer(item, "track_count");
        if (count is null || count != tracks.Length) throw new InvalidDataException("SoundCloud returned an incomplete or changing playlist track list.");
        if (count > request.MaximumPlaylistEntries) throw new InvalidDataException("The SoundCloud playlist exceeds the configured item limit; it was not truncated.");
        var full = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            if (Text(track, "permalink_url") is null) continue;
            if (Text(track, "kind") != "track") throw new InvalidDataException("SoundCloud returned a non-track playlist entry.");
            _ = Permalink(track, false);
            full.TryAdd(Id(track), track);
        }
        var missing = tracks.Select(Id).Where(id => !full.ContainsKey(id)).Distinct().ToArray();
        foreach (var batch in missing.Chunk(50))
        {
            var reply = await ReadApiAsync(http, SoundCloudAddress.WithQuery(new Uri("https://api-v2.soundcloud.com/tracks"), ("ids", string.Join(',', batch)), ("client_id", client)), ct).ConfigureAwait(false);
            if (reply.ValueKind != JsonValueKind.Array || reply.GetArrayLength() > batch.Length) throw new InvalidDataException("SoundCloud returned an invalid playlist detail batch.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in reply.EnumerateArray())
            {
                var id = Id(track);
                if (!batch.Contains(id) || !seen.Add(id) || Text(track, "kind") != "track") throw new InvalidDataException("SoundCloud returned unexpected playlist track identities.");
                full[id] = track.Clone();
            }
        }
        var entries = tracks.Select((stub, index) =>
        {
            var id = Id(stub);
            if (!full.TryGetValue(id, out var track)) return new NativePlaylistEntry(index + 1, id, "Unavailable track " + id, null, null, null, false);
            var metadata = Metadata(track); var unavailable = Unavailable(track);
            return new NativePlaylistEntry(index + 1, id, metadata.Title, Permalink(track, false), metadata.DurationSeconds, metadata.ThumbnailUrl, unavailable is null,
                unavailable is null ? null : metadata with { Warnings = [unavailable] },
                new("soundcloud", id), id);
        }).ToArray();
        return Metadata(item) with { IsPlaylist = true, PlaylistId = Id(item), Entries = entries, RevalidateCollectionMembership = true };
    }

    private static string? Unavailable(JsonElement item)
    {
        var policy = Text(item, "policy");
        if (!True(item, "streamable") || policy is not (null or "ALLOW" or "MONETIZE") || Text(item, "access") is "blocked" or "preview")
            return "This SoundCloud track requires access not supplied by the current playback session.";
        if (Number(item, "full_duration") is { } full && Number(item, "duration") is { } offered && offered + 1000 < full)
            return "SoundCloud exposed only a preview of this track.";
        return null;
    }
    private static string Id(JsonElement item) => Long(item, "id") is > 0 and var id ? id.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("A SoundCloud item has no valid identity.");
    private static Uri Permalink(JsonElement item, bool playlist)
    {
        if (!Uri.TryCreate(Text(item, "permalink_url"), UriKind.Absolute, out var url)) throw new InvalidDataException("SoundCloud returned an invalid permalink.");
        var address = SoundCloudAddress.Parse(url);
        if (address.ApiId is not null || address.ShortLink || address.Collection is not null || address.Playlist != playlist) throw new InvalidDataException("SoundCloud returned the wrong permalink type.");
        return address.Url;
    }
    private static Uri ApiUrl(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "api-v2.soundcloud.com" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("SoundCloud returned an invalid API address.");
        return url;
    }
    private static bool MediaUrl(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
        (url.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".soundcloud.cloud", StringComparison.OrdinalIgnoreCase));
    private static string? Image(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && MediaUrl(url) ? url.AbsoluteUri : null;
    private static async Task<JsonElement> ReadApiAsync(MediaHttp http, Uri url, CancellationToken ct)
    {
        _ = ApiUrl(url.AbsoluteUri);
        var response = await http.GetDocumentAsync(url, 4 * 1024 * 1024, ct,
            new Dictionary<string, string> { ["Origin"] = "https://soundcloud.com", ["Referer"] = "https://soundcloud.com/" }).ConfigureAwait(false);
        _ = ApiUrl(response.Url.AbsoluteUri);
        if (response.Url != url) throw new InvalidDataException("SoundCloud changed its requested API resource.");
        using var document = JsonDocument.Parse(response.Text);
        return document.RootElement.Clone();
    }
}
