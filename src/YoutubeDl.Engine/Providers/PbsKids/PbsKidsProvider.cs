using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Pbs;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.PbsKids;

public sealed class PbsKidsProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("pbskids", "PBS Kids current public videos and collections", 200);
    public bool CanHandle(Uri url) => PbsKidsAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = PbsKidsAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var response = await anonymous.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (PbsKidsAddress.Parse(response.Url) != address) throw new InvalidDataException("PBS Kids redirected to another video/collection identity.");
        using var state = Read(response.Text); var root = state.RootElement; var query = Array(Object(root, "query"), "slugsIds");
        var expected = address.Context is null ? new[] { address.Slug, address.Id } : new[] { address.Context, address.Slug, address.Id };
        if (Text(root, "page") != "/videos/" + address.Mode + "/[...slugsIds]" || True(root, "isFallback") || query.Length != expected.Length ||
            query.Where((value, index) => value.ValueKind != JsonValueKind.String || value.GetString() != expected[index]).Any()) throw new InvalidDataException("PBS Kids returned mismatched or incomplete route metadata.");
        var props = Object(Object(root, "props"), "pageProps");
        if (Text(props, "queriedContextSlug") != (address.Context ?? "")) throw new InvalidDataException("PBS Kids returned another collection/property context.");
        if (address.Mode == "playlist") return Collection(address, props, request.MaximumPlaylistEntries);
        var video = Object(props, "videoData");
        if (Text(video, "id") != address.Id || Text(video, "slug") != address.Slug || Text(props, "queriedVideoSlug") != address.Slug) throw new InvalidDataException("PBS Kids returned another requested video record.");
        if (Text(video, "videoType") is "live" or "livestream" or "podcast") throw new NotSupportedException("This PBS Kids live/audio workflow needs additional native support.");
        if (Text(video, "expiryDate") is { Length: > 0 } expiry)
        {
            if (!DateTimeOffset.TryParse(expiry, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var expires)) throw new InvalidDataException("PBS Kids returned an invalid expiry date.");
            if (expires <= DateTimeOffset.UtcNow) throw new NotSupportedException("This PBS Kids video has expired.");
        }
        var asset = Object(video, "mediaManagerAsset"); var cid = PbsPageReader.Cid(Text(asset, "id"));
        if (!asset.TryGetProperty("drm_enabled", out var drm) || drm.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new NotSupportedException("PBS Kids omitted a supported explicit protection state.");
        if (drm.ValueKind == JsonValueKind.True) throw new NotSupportedException("This PBS Kids asset requires protected playback support.");
        var duration = Number(asset, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("PBS Kids omitted a valid video duration.");
        if (!asset.TryGetProperty("videos", out var supplied) || supplied.ValueKind != JsonValueKind.Array || supplied.GetArrayLength() is < 1 or > 64) throw new NotSupportedException("PBS Kids did not expose a bounded public encoding inventory.");
        var media = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal); var resolved = new HashSet<string>(StringComparer.Ordinal); var protectedCount = 0;
        foreach (var descriptor in supplied.EnumerateArray())
        {
            var profile = Text(descriptor, "profile");
            if (profile?.EndsWith("-drm", StringComparison.Ordinal) == true || Text(descriptor, "widevine_license") is { Length: > 0 } || Text(descriptor, "fairplay_license") is { Length: > 0 }) { protectedCount++; continue; }
            var match = profile is null ? Match.Empty : Regex.Match(profile, @"\A(?<kind>mp4|hls)-[0-9]+x[0-9]+-(?<quality>baseline|[1-9][0-9]{1,4}p)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) throw new NotSupportedException("This PBS Kids encoding profile needs additional native support.");
            var raw = Text(descriptor, "url") ?? throw new InvalidDataException("PBS Kids omitted an encoding identity."); if (!resolved.Add(raw)) continue;
            var url = await PbsProvider.ResolveEncodingAsync(raw, cid, anonymous, ct).ConfigureAwait(false);
            if (!seen.Add(url.AbsoluteUri)) continue;
            var hls = match.Groups["kind"].Value == "hls";
            if (!url.AbsolutePath.EndsWith(hls ? ".m3u8" : ".mp4", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PBS Kids's resolved container disagrees with its offered profile.");
            var quality = match.Groups["quality"].Value; var id = profile!; if (media.Any(item => item.Id == id)) id += "-" + (media.Count + 1);
            media.Add(new() { Id = id, Label = profile, Url = url, Kind = hls ? MediaKind.Hls : MediaKind.Progressive, Extension = hls ? "ts" : "mp4",
                Height = !hls && quality != "baseline" ? int.Parse(quality[..^1], System.Globalization.CultureInfo.InvariantCulture) : null, HasVideo = true, HasAudio = null, IsCompleteMedia = true,
                Source = Info.Id, VerificationDemuxer = hls ? "mpegts" : "mov", VerificationDurationSeconds = duration });
        }
        if (media.Count == 0) throw new NotSupportedException("PBS Kids did not expose a ready clear playback encoding.");
        return new() { Id = address.Id, Extractor = Info.Id, ArchiveIdentity = new("pbskids", cid), Title = Text(video, "title") ?? Text(asset, "title") ?? address.Id,
            Description = Text(asset, "description_long") ?? Text(asset, "description_short"), DurationSeconds = duration, Media = media, ThumbnailUrl = Poster(asset), SourcePageUrl = address.Page,
            Uploader = Uploader(video), AnonymousPlayback = true, Warnings = protectedCount > 0 ? ["Protected PBS Kids profiles were not made available as native clear formats."] : [] };
    }
    private static PageMetadata Collection(PbsKidsAddress address, JsonElement props, int limit)
    {
        var collection = Object(props, "collectionData");
        if (Text(collection, "__typename") != "PbsKidsVideoPlaylist" || Text(collection, "id") != address.Id || Text(collection, "slug") != address.Slug || Text(props, "queriedCollectionSlug") != address.Slug)
            throw new InvalidDataException("PBS Kids returned another typed collection identity.");
        if (!collection.TryGetProperty("entries", out var items) || items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PBS Kids omitted its collection entries.");
        if (items.GetArrayLength() > limit || items.GetArrayLength() > 10000) throw new NotSupportedException("The PBS Kids collection exceeds the configured inventory limit.");
        foreach (var key in new[] { "count", "totalCount", "entryCount" }) if (collection.TryGetProperty(key, out _) && Integer(collection, key) != items.GetArrayLength())
            throw new InvalidDataException("PBS Kids's supplied collection does not match its declared complete count.");
        foreach (var key in new[] { "hasMore", "hasNextPage", "nextPage", "nextCursor" }) if (collection.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null))
            throw new NotSupportedException("This PBS Kids collection needs additional pagination support; its first page is not complete.");
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            var id = Text(item, "id"); var slug = Text(item, "slug"); var asset = Object(item, "mediaManagerAsset");
            if (Text(item, "__typename") != "PbsKidsVideo" || id is null || !PbsAddress.Numeric(id) || !ids.Add(id) || slug is null || !PbsKidsAddress.SlugValid(slug)) throw new InvalidDataException("PBS Kids returned an invalid or repeated collection video identity.");
            var available = asset.ValueKind == JsonValueKind.Object && Text(item, "guid") is { Length: > 0 };
            var cid = available ? PbsPageReader.Cid(Text(item, "guid")) : null;
            entries.Add(new(entries.Count + 1, id, Text(item, "title") ?? id, available ? new PbsKidsAddress("watch", slug, id).Page : null,
                Number(asset, "duration"), asset.ValueKind == JsonValueKind.Object ? Poster(asset) : null, available, ExpectedArchiveIdentity: cid is null ? null : new("pbskids", cid)));
        }
        return new() { Id = address.Id, PlaylistId = address.Id, Extractor = "pbskids:collection", IsPlaylist = true, Title = Text(collection, "title") ?? address.Id,
            Description = Text(collection, "shortDescriptionPlainText"), Entries = entries, Uploader = "PBS Kids", SourcePageUrl = address.Page };
    }
    private static string? Uploader(JsonElement video) => Array(video, "properties").Select(item => Text(item, "title")).FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)) ?? "PBS Kids";
    private static string? Poster(JsonElement asset)
    {
        var images = Array(asset, "images"); if (images.Length > 128) throw new InvalidDataException("PBS Kids returned too many artwork variants.");
        var selected = images.OrderByDescending(item => Text(item, "profile")?.EndsWith("16x9", StringComparison.Ordinal) == true).Select(item => Text(item, "image")).FirstOrDefault(raw => !string.IsNullOrWhiteSpace(raw));
        if (selected is null) return null;
        if (!Uri.TryCreate(selected, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("image.pbs.org" or "image.pbskids.org")) throw new NotSupportedException("PBS Kids returned an unsupported poster origin.");
        return uri.AbsoluteUri;
    }
    private static JsonDocument Read(string html)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        var scripts = Regex.Matches(clean, @"<script\b[^>]*\bid\s*=\s*([""'])__NEXT_DATA__\1[^>]*>(?<data>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        if (scripts.Count != 1 || scripts[0].Groups["data"].Length > 8 * 1024 * 1024) throw new NotSupportedException("PBS Kids did not expose one bounded public page-data record.");
        return PbsPageReader.Json(scripts[0].Groups["data"].Value);
    }
}
