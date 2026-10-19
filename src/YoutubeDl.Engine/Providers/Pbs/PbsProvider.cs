using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Pbs;

public sealed class PbsProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("pbs", "PBS public video pages and players", 200);
    public bool CanHandle(Uri url) => PbsAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => PbsAddress.Parse(request.Url).Id is { } id ? new("pbs", id) : null;
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = PbsAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession(); PbsPageReader.Asset? selected = null;
        if (address.Slug is { } slug)
        {
            var document = await anonymous.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (PbsAddress.Parse(document.Url).Slug != slug) throw new InvalidDataException("PBS redirected to another requested video page.");
            selected = PbsPageReader.Selected(document.Text, slug); address = new(selected.Id, null);
        }
        var player = await anonymous.GetDocumentAsync(address.Page, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (PbsAddress.Parse(player.Url).Id != address.Id) throw new InvalidDataException("PBS redirected to another player identity.");
        using var bridge = PbsPageReader.Bridge(player.Text); var root = bridge.RootElement;
        var id = Long(root, "id")?.ToString(System.Globalization.CultureInfo.InvariantCulture); var cid = PbsPageReader.Cid(Text(root, "cid"));
        if (id != address.Id || selected is not null && (selected.Cid != cid || Text(root, "slug") != selected.Slug)) throw new InvalidDataException("PBS's page and player content identities disagree.");
        if (Text(root, "availability") != "available" || !Flag(root, "is_playable")) throw new NotSupportedException("This PBS video is unavailable to the current public session.");
        if (Flag(root, "is_mvod")) throw new NotSupportedException("This PBS video needs native Passport/account support.");
        if (Flag(root, "has_drm")) throw new NotSupportedException("This PBS video exposes protected playback that is not supported by the native engine.");
        if (Text(root, "video_type") is "livestream" or "live") throw new NotSupportedException("PBS live recording needs additional native support.");
        var duration = Number(root, "duration"); if (duration is <= 0 or > 604800 || root.TryGetProperty("duration", out var declaredDuration) && declaredDuration.ValueKind != JsonValueKind.Null && duration is null)
            throw new InvalidDataException("PBS returned an unsupported video duration.");
        var encodings = Array(root, "encodings"); if (encodings.Length is < 1 or > 32) throw new NotSupportedException("PBS did not expose a bounded complete public encoding inventory.");
        var media = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var encoding in encodings)
        {
            if (encoding.ValueKind != JsonValueKind.String) throw new InvalidDataException("PBS returned an invalid public encoding descriptor.");
            var url = await ResolveEncodingAsync(encoding.GetString(), cid, anonymous, ct).ConfigureAwait(false); if (!seen.Add(url.AbsoluteUri)) continue;
            var kind = url.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ? MediaKind.Hls : url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? MediaKind.Progressive : MediaKind.Unknown;
            if (kind == MediaKind.Unknown) throw new NotSupportedException("This PBS encoding container needs additional native support.");
            var rendition = Regex.Match(url.AbsolutePath, @"-mp4-(?<height>[1-9][0-9]{1,4})p-[0-9]+k\.mp4\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var format = kind == MediaKind.Hls ? "hls" : rendition.Success ? "mp4-" + rendition.Groups["height"].Value : "mp4";
            if (media.Any(candidate => candidate.Id == format)) format += "-" + (media.Count + 1);
            media.Add(new() { Id = format, Url = url, Kind = kind, Extension = kind == MediaKind.Hls ? "ts" : "mp4", Height = rendition.Success ? int.Parse(rendition.Groups["height"].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                HasVideo = true, HasAudio = null, IsCompleteMedia = true, Source = "pbs", VerificationDemuxer = kind == MediaKind.Hls ? "mpegts" : "mov", VerificationDurationSeconds = duration });
        }
        var captions = Captions(root, cid); var image = Text(root, "image_url") is { Length: > 0 } poster ? Image(poster).AbsoluteUri : null;
        return new() { Id = id, Extractor = "pbs", ArchiveIdentity = new("pbs", id!), Title = Text(root, "title") ?? id!, Description = Text(root, "long_description") ?? Text(root, "short_description"),
            DurationSeconds = duration, Uploader = Text(Object(root, "program"), "title") ?? Text(Object(root, "program"), "producer") ?? "PBS", Media = media, Subtitles = captions,
            ThumbnailUrl = image, SourcePageUrl = selected is null ? address.Page : new PbsAddress(null, selected.Slug).Page, AnonymousPlayback = true };
    }
    private static IReadOnlyList<SubtitleTrack> Captions(JsonElement root, string cid)
    {
        var tracks = new List<SubtitleTrack>(); var descriptors = Array(root, "cc_multiple_languages");
        if (descriptors.Length > 128) throw new InvalidDataException("PBS returned too many caption resources.");
        foreach (var descriptor in descriptors)
        {
            var extension = Profile(Text(descriptor, "profile")); if (extension is null) continue;
            var language = Text(descriptor, "language");
            if (language is null || !Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidDataException("PBS returned an invalid caption language.");
            tracks.Add(new() { Url = Resource(Text(descriptor, "url"), cid), Language = language.ToLowerInvariant(), Extension = extension });
        }
        if (tracks.Count == 0 && Object(root, "cc") is { ValueKind: JsonValueKind.Object } legacy)
            foreach (var profile in legacy.EnumerateObject()) if (Profile(profile.Name) is { } extension && profile.Value.ValueKind == JsonValueKind.String)
                tracks.Add(new() { Url = Resource(profile.Value.GetString(), cid), Language = "en", Extension = extension });
        foreach (var group in tracks.GroupBy(track => (track.Language, track.Extension)))
            if (group.Select(track => track.Url).Distinct().Count() != 1) throw new InvalidDataException("PBS returned ambiguous same-language caption formats.");
        return tracks.DistinctBy(track => (track.Language, track.Extension, track.Url)).ToArray();
    }
    private static string? Profile(string? profile) => profile switch { "WebVTT" => "vtt", "SRT" => "srt", "DFXP" or "TTML" => "ttml", _ => null };
    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.ValueKind == JsonValueKind.True :
        throw new NotSupportedException("PBS did not expose a supported explicit player availability/protection state.");
    internal static async Task<Uri> ResolveEncodingAsync(string? raw, string cid, MediaHttp http, CancellationToken ct)
    {
        var endpoint = Resolver(raw); var response = await http.GetDocumentAsync(endpoint, 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("PBS redirected its encoding metadata to another resource.");
        using var resolved = PbsPageReader.Json(response.Text); var data = resolved.RootElement;
        if (Text(data, "status") != "ok" || Integer(data, "http_code") is not (200 or 302)) throw new NotSupportedException("PBS refused public playback for an offered encoding, including any current regional/access restriction.");
        return Resource(Text(data, "url"), cid);
    }
    private static Uri Resolver(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "urs.pbs.org" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/redirect/[a-f0-9]{32}/\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new NotSupportedException("PBS returned an unsupported public encoding resolver.");
        return new(uri.AbsoluteUri + "?format=json");
    }
    private static Uri Resource(string? raw, string cid)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !(uri.Host.EndsWith(".pbs-video.pbs.org", StringComparison.Ordinal) || Regex.IsMatch(uri.Host, @"\A[a-z0-9-]+\.video\.cdn\.pbs\.org\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
            throw new NotSupportedException("PBS returned an unverified public media origin.");
        var asset = Regex.Match(uri.AbsolutePath, @"/(?:videos|captions)/[^/]+/(?<id>[A-Fa-f0-9-]{36})/", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (asset.Success && PbsPageReader.Cid(asset.Groups["id"].Value) != cid) throw new InvalidDataException("PBS's media resource belongs to another content asset.");
        return uri;
    }
    private static Uri Image(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "image.pbs.org" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri :
        throw new NotSupportedException("PBS returned an unsupported poster origin.");
}
