using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.IHeart;

public sealed partial class IHeartProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("iheartradio", "iHeart public podcast episodes widgets and collections", 200);
    public bool CanHandle(Uri url) => IHeartAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => IHeartAddress.Parse(request.Url).EpisodeId is { } id ? new("iheartradio", id) : null;

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = IHeartAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var website = await anonymous.GetDocumentAsync(address.Web, 5 * 1024 * 1024, ct).ConfigureAwait(false);
        IHeartAddress redirected;
        try { redirected = IHeartAddress.Parse(website.Url); }
        catch (ArgumentException) { throw new InvalidDataException("iHeart redirected to an unsupported public website identity."); }
        if (website.Url.Scheme != "https" || redirected != address) throw new InvalidDataException("iHeart redirected to another podcast or episode.");
        var api = IHeartHydration.Api(website.Text, ct);
        using var podcast = await JsonAsync(new(api, "api/v3/podcast/podcasts/" + address.PodcastId), anonymous, ct).ConfigureAwait(false);
        ValidatePodcast(podcast.RootElement, address.PodcastId);
        if (address.EpisodeId is null) return await CollectionAsync(address, request, api, podcast.RootElement, anonymous, ct).ConfigureAwait(false);
        using var episode = await JsonAsync(new(api, "api/v3/podcast/episodes/" + address.EpisodeId + "?withPageKey=true"), anonymous, ct).ConfigureAwait(false);
        var media = Object(episode.RootElement, "episode"); ValidateEpisode(media, address.PodcastId, address.EpisodeId);
        return Episode(media, podcast.RootElement, address, request);
    }

    private static void ValidatePodcast(JsonElement root, string id)
    {
        if (Id(root) != id || string.IsNullOrWhiteSpace(Text(root, "title"))) throw new InvalidDataException("iHeart returned another or unidentified podcast.");
        RefuseRestricted(root);
    }
    private static void ValidateEpisode(JsonElement root, string podcast, string? id = null)
    {
        if (root.ValueKind != JsonValueKind.Object || Id(root) is not { } actual || id is not null && actual != id || Id(root, "podcastId") != podcast)
            throw new InvalidDataException("iHeart returned another requested episode or podcast owner.");
    }
    private static PageMetadata Episode(JsonElement root, JsonElement podcast, IHeartAddress address, NativeDownloadRequest request)
    {
        RefuseRestricted(root);
        if (!Published(root, requireEnd: true)) throw new NotSupportedException("This iHeart episode is unpublished or no longer offered.");
        var duration = Number(root, "duration");
        if (duration is not > 0 or > 604800) throw new NotSupportedException("iHeart did not declare a supported complete episode duration.");
        var types = Types(root); if (types.Length != 1) throw new NotSupportedException("Multiple or missing iHeart media descriptors require additional native support.");
        var (extension, demuxer) = types[0] switch
        {
            "audio/mpeg" or "audio/mp3" => ("mp3", "mp3"),
            "audio/mp4" or "audio/x-m4a" => ("m4a", "mov"),
            "audio/aac" or "audio/x-aac" => ("aac", "aac"),
            _ => throw new NotSupportedException("This iHeart video or audio encoding requires additional native support.")
        };
        var source = PublicUrl(Text(root, "mediaUrl"));
        var offeredExtension = Path.GetExtension(source.AbsolutePath).TrimStart('.').ToLowerInvariant();
        if (offeredExtension is "mp3" or "m4a" or "aac" or "mp4" or "m3u8" or "mpd" && offeredExtension != extension)
            throw new InvalidDataException("iHeart's declared audio type conflicts with its offered media source.");
        if (Flag(root, "transcriptionAvailable") && (request.WriteSubs || request.WriteAutoSubs || request.EmbedSubs))
            throw new NotSupportedException("iHeart transcript discovery requires additional native support.");
        return new()
        {
            Id = address.EpisodeId, Extractor = "iheartradio", ArchiveIdentity = new("iheartradio", address.EpisodeId!),
            Title = Text(root, "title") ?? address.EpisodeId!, Description = Text(root, "description"), Uploader = Text(podcast, "title"),
            DurationSeconds = duration, ThumbnailUrl = Poster(Text(root, "imageUrl")), SourcePageUrl = address.Web, AnonymousPlayback = true,
            UploadTimestamp = MetadataFacts.Unix(Long(root, "startDate"), milliseconds: true),
            UploadDate = MetadataFacts.Day(MetadataFacts.Unix(Long(root, "startDate"), milliseconds: true)),
            Media = [new() { Id = "audio", Url = source, Extension = extension, MimeType = types[0], Kind = MediaKind.Progressive, Source = "iheartradio",
                HasAudio = true, HasVideo = false, IsCompleteMedia = true, VerificationDemuxer = demuxer, VerificationMinimumDurationSeconds = duration }],
            Warnings = Flag(root, "transcriptionAvailable") ? ["iHeart advertises a transcript; native transcript retrieval remains pending."] : []
        };
    }
    private static bool Published(JsonElement root, bool requireEnd)
    {
        var start = Long(root, "startDate"); var end = Long(root, "endDate"); var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (start is null || start > 253402300799999 || requireEnd && end is null || end is > 253402300799999 || end is not null && end <= start)
            throw new InvalidDataException("iHeart returned invalid episode publication bounds.");
        return start <= now && (end is null || end > now);
    }
    private static string[] Types(JsonElement root)
    {
        if (!root.TryGetProperty("mimeTypes", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 16)
            throw new InvalidDataException("iHeart omitted its offered media types.");
        if (values.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString())))
            throw new InvalidDataException("iHeart returned invalid offered media types.");
        return values.EnumerateArray().Select(item => item.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
    }
    private static bool Flag(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return false;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ValueKind == JsonValueKind.True : throw new InvalidDataException("iHeart returned an invalid playback state.");
    }
    private static void RefuseRestricted(JsonElement root)
    {
        foreach (var flag in new[] { "isPrivate", "isPremium", "requiresLogin", "subscriptionRequired", "isPreview", "isLive", "drmProtected" })
            if (Flag(root, flag)) throw new NotSupportedException("This iHeart recording requires additional account, preview, live or protected-playback support.");
    }
    private static string Id(JsonElement root, string name = "id") => Long(root, name) is > 0 and < 1000000000000000 ? Long(root, name)!.Value.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("iHeart returned an invalid numeric identity.");
    private static Uri PublicUrl(string? raw)
    {
        if (raw is null || raw.Length > 32768 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns)
            throw new InvalidDataException("iHeart returned an invalid public media resource.");
        return uri;
    }
    private static string? Poster(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : PublicUrl(raw).AbsoluteUri;
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var document = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != endpoint) throw new InvalidDataException("iHeart redirected its public API identity.");
        var json = IHeartHydration.Parse(document.Text, ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object) { json.Dispose(); throw new InvalidDataException("iHeart returned an invalid public API object."); }
        return json;
    }
}
