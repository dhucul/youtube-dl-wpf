using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Music;
using static YoutubeDl.Engine.Providers.ProviderJson;
using static YoutubeDl.Engine.Providers.PublicProviderApi;

namespace YoutubeDl.Engine.Providers.Orf;

internal sealed record OrfPodcastAddress(string Station, string Series, string? Episode)
{
    internal string Path => Station + "/" + Series + (Episode is null ? "" : "/" + Episode);
    internal Uri Url => new("https://sound.orf.at/podcast/" + Path);
    internal static OrfPodcastAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Host != "sound.orf.at" || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) throw new ArgumentException("Use a canonical ORF Sound podcast or episode URL.");
        var match = Match(url.AbsolutePath, @"\A/podcast/(?<station>[a-z0-9-]{1,24})/(?<series>[a-z0-9-]{1,160})(?:/(?<episode>[a-z0-9-]{1,240}))?/?\z");
        if (!match.Success) throw new NotSupportedException("This ORF Sound route is not supported yet.");
        return new(match.Groups["station"].Value, match.Groups["series"].Value, match.Groups["episode"].Success ? match.Groups["episode"].Value : null);
    }
}
public sealed class OrfPodcastProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("orf-podcast", "ORF Sound public podcasts", 200);
    public bool SupportsGeoVerificationProxy => true;
    public bool CanHandle(Uri url) => url.IsAbsoluteUri && url.Host == "sound.orf.at";
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        NativeGeoVerification.ValidateProvider(request, this); var address = OrfPodcastAddress.Parse(request.Url);
        if (address.Episode is null)
        {
            using var verification = http.CreateVerificationSession(request.GeoVerification?.Proxy); var parent = await Fetch(verification, address, true, ct).ConfigureAwait(false); ValidateParent(parent, address);
            var rows = Rows(parent, "episodes"); if (Integer(parent, "episodeCount") != rows.Length) throw new InvalidDataException("ORF returned a partial podcast inventory."); Limit(rows.Length, request);
            var entries = new List<NativePlaylistEntry>();
            foreach (var row in rows)
            {
                var metadata = Metadata(row, parent); var item = OrfPodcastAddress.Parse(metadata.SourcePageUrl!);
                if (item.Station != address.Station || item.Series != address.Series || item.Episode is null) throw new InvalidDataException("ORF changed podcast membership.");
                entries.Add(new(entries.Count + 1, metadata.Id, metadata.Title, metadata.SourcePageUrl, metadata.DurationSeconds, metadata.ThumbnailUrl, True(parent, "isOnline") && !Expired(row), ExpectedArchiveIdentity: metadata.ArchiveIdentity, CollectionMemberId: metadata.Id));
            }
            return Inventory("orf:podcast", Numeric(parent), Title(parent), entries, request);
        }
        async Task<PageMetadata> ReadCurrent(MediaHttp controlled, CancellationToken token)
        {
            using var verification = controlled.CreateVerificationSession(request.GeoVerification?.Proxy); var row = await Fetch(verification, address, false, token).ConfigureAwait(false); var parent = Object(row, "podcast"); ValidateParent(parent, address);
            var page = Metadata(row, parent); if (OrfPodcastAddress.Parse(page.SourcePageUrl!) != address) throw new InvalidDataException("ORF returned another podcast episode.");
            if (!True(parent, "isOnline") || Expired(row)) return page with { Warnings = ["ORF has not published this podcast episode for the current session."] };
            if (Drm(row) || True(row, "requiresLogin") || True(row, "payable")) return page with { DrmRefused = Drm(row), RefusalReason = "ORF requires protected or entitled playback for this episode.", Warnings = ["ORF did not grant public full playback."] };
            if (await MusicCatalog.RecordedAsync(request, page, token).ConfigureAwait(false)) return page;
            var sources = Rows(row, "enclosures", 32); var formats = new List<MediaCandidate>();
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index]; if (Text(source, "type") != "audio/mpeg") continue;
                var resource = Secure(Text(source, "url"), "podcast.orf.at"); var length = Long(source, "length");
                if (!resource.AbsolutePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || length is not (> 0 and <= 68719476736)) throw new InvalidDataException("ORF omitted a complete MP3 enclosure.");
                if (Path.GetFileName(resource.AbsolutePath) != page.Id + ".mp3") throw new InvalidDataException("ORF returned another episode's audio enclosure.");
                formats.Add(MusicCatalog.Audio(resource, "mp3-" + index, "mp3", page.DurationSeconds!.Value, Info.Id, length));
            }
            if (formats.Count == 0) throw new NotSupportedException("This ORF episode has no supported complete MP3 enclosure.");
            return page with { Media = formats };
        }
        return await http.SessionState(Key(Info.Id, request), () => new ProviderPlaybackSession(http, request, Info.Id, ReadCurrent, reorderedFormats: false)).ReadAsync(ct).ConfigureAwait(false);
    }
    private static bool Expired(JsonElement row) => Date(Text(row, "expiry")) is { } end && end <= DateTimeOffset.UtcNow;
    private static void ValidateParent(JsonElement parent, OrfPodcastAddress address)
    {
        _ = Numeric(parent); var declared = OrfPodcastAddress.Parse(Secure(Text(Object(parent, "link"), "url"), "sound.orf.at"));
        if (Text(parent, "entity") != "Podcast" || declared.Station != address.Station || declared.Series != address.Series || declared.Episode is not null || Text(parent, "station") != address.Station || Text(parent, "slug") != address.Series)
            throw new InvalidDataException("ORF returned another podcast identity.");
    }
    private static PageMetadata Metadata(JsonElement row, JsonElement parent)
    {
        var guid = Text(row, "guid"); if (guid is null || !Match(guid, @"\A[A-Za-z0-9_-]{1,160}\z").Success || Text(row, "entity") != "Episode") throw new InvalidDataException("ORF omitted its episode identity.");
        var duration = Number(row, "duration"); if (duration is not (> 0 and <= 604800000)) throw new InvalidDataException("ORF omitted the complete episode duration.");
        var url = Secure(Text(Object(row, "link"), "url"), "sound.orf.at"); var address = OrfPodcastAddress.Parse(url);
        if (address.Episode is null || address.Episode != Text(row, "slug")) throw new InvalidDataException("ORF returned another episode page.");
        var date = Date(Text(row, "published")); var image = Text(Object(Object(Object(parent, "image"), "versions"), "premium"), "path") ?? Text(Object(Object(Object(parent, "image"), "versions"), "id3art"), "path");
        return new() { Id = guid, Extractor = "orf:podcast", ArchiveIdentity = new("orf:podcast", guid), Title = Title(row), Description = Text(row, "description"), DurationSeconds = duration / 1000,
            UploadTimestamp = date, UploadDate = date is null ? null : DateOnly.FromDateTime(date.Value.UtcDateTime), Uploader = Text(parent, "author") ?? Title(parent), ThumbnailUrl = image is null ? null : Secure(image, "podcast.orf.at").AbsoluteUri, AnonymousPlayback = true, SourcePageUrl = url };
    }
    private static async Task<JsonElement> Fetch(MediaHttp http, OrfPodcastAddress address, bool episodes, CancellationToken ct)
    {
        var root = await Read(http, new("https://audioapi.orf.at/radiothek/api/2.0/podcast/" + address.Path + (episodes ? "?episodes" : "")), ct).ConfigureAwait(false);
        var payload = Object(root, "payload"); if (payload.ValueKind != JsonValueKind.Object) throw new InvalidDataException("ORF omitted its podcast response envelope."); return payload;
    }
}
