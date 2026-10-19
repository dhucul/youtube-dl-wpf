using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Medal;

public sealed class MedalProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("medaltv", "Medal public clips and supplied MP4/HLS resources", 200);
    public bool CanHandle(Uri url) => MedalAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("medaltv", MedalAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = MedalAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var response = await anonymous.GetDocumentAsync(address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false); var returned = MedalAddress.Parse(response.Url);
        if (returned.Id != address.Id) throw new InvalidDataException("Medal redirected to another clip identity.");
        var clip = ReadClip(response.Text, address.Id); var share = MedalAddress.Parse(new Uri(Text(clip, "contentShareUrl") ?? throw new InvalidDataException("Medal omitted its public clip URL.")));
        if (share.Id != address.Id || address.Game is not null && Text(Object(clip, "category"), "slug") != address.Game || share.Game != returned.Game)
            throw new InvalidDataException("Medal's clip route and supplied category/share identities disagree.");
        var state = Object(clip, "state");
        if (Integer(clip, "contentType") != 15 || Text(state, "type") != "published" || !True(state, "isSuccess") || !True(state, "isShareable") || Integer(clip, "processed") != 1 || Integer(clip, "hasAccess") != 1 ||
            !clip.TryGetProperty("requireLogin", out var login) || login.ValueKind != JsonValueKind.False || Integer(clip, "privacy") != 0 || clip.TryGetProperty("archivedAt", out var archived) && archived.ValueKind != JsonValueKind.Null)
            throw new NotSupportedException("This Medal clip is unavailable to the current public workflow.");
        var owner = Text(Object(clip, "poster"), "userId"); if (owner is null || !Regex.IsMatch(owner, @"\A[1-9][0-9]{0,19}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidDataException("Medal omitted its public owner identity.");
        var duration = Number(clip, "videoLengthSeconds"); var width = Integer(clip, "sourceWidth"); var height = Integer(clip, "sourceHeight");
        if (duration is not > 0 || duration > 604800 || width is not > 0 || height is not > 0 || width > 16384 || height > 16384) throw new InvalidDataException("Medal returned invalid source dimensions or duration.");
        var source = Resource(Text(clip, "contentUrl"), address.Id, owner, false); var media = new List<MediaCandidate>(); var reported = new List<NativeFormat>(); var urls = new HashSet<string>(StringComparer.Ordinal);
        Add("source", source, width, height);
        foreach (var resolution in new[] { 1080, 720, 480, 360, 240, 144 })
        {
            if (Text(clip, "contentUrl" + resolution + "p") is not { Length: > 0 } raw) continue;
            var url = Resource(raw, address.Id, owner, false);
            if (Query(url).ContainsKey("missing"))
            { reported.Add(new() { Id = resolution + "p", Height = resolution, Extension = "mp4", HasVideo = true, Availability = FormatAvailability.MissingUrl, UnavailableReason = "The publisher marked this named rendition as not presently available." }); continue; }
            Add(resolution + "p", url, null, resolution);
        }
        if (Text(clip, "contentUrlHls") is { Length: > 0 } hls)
            media.Add(new() { Id = "hls", Url = HlsResource(hls, address.Id), Kind = MediaKind.Hls, Extension = "ts", HasVideo = true, HasAudio = Integer(clip, "hasSound") == 0 ? false : null,
                IsCompleteMedia = true, Source = Info.Id, VerificationDemuxer = "mpegts", VerificationDurationSeconds = duration });
        var image = Text(clip, "thumbnailUrl") is { Length: > 0 } poster ? Resource(poster, address.Id, owner, true).AbsoluteUri : null;
        return new() { Id = address.Id, Extractor = Info.Id, ArchiveIdentity = new("medaltv", address.Id), Title = Text(clip, "contentTitle") ?? address.Id, Description = Text(clip, "contentDescription"),
            DurationSeconds = duration, Uploader = Text(Object(clip, "poster"), "userName") ?? owner, ThumbnailUrl = image, Media = media,
            ReportedFormats = media.Select(item => new NativeFormat { Id = item.Id!, Url = item.Url, Width = item.Width, Height = item.Height, Extension = item.Extension,
                HasVideo = item.HasVideo, HasAudio = item.HasAudio, Availability = FormatAvailability.Ready, Protocol = item.Kind == MediaKind.Hls ? "m3u8" : "https" }).Concat(reported).ToArray(), SourcePageUrl = share.Page, AnonymousPlayback = true };
        void Add(string id, Uri url, int? width, int? height)
        {
            if (!urls.Add(url.AbsoluteUri)) return;
            media.Add(new() { Id = id, Url = url, Kind = MediaKind.Progressive, Extension = "mp4", Width = width, Height = height, HasVideo = true, HasAudio = Integer(clip, "hasSound") == 0 ? false : null,
                IsCompleteMedia = true, Source = Info.Id, VerificationDemuxer = "mov", VerificationDurationSeconds = duration });
        }
    }
    internal static JsonElement ReadClip(string html, string id)
    {
        var found = new List<JsonElement>(); var count = 0;
        foreach (var root in NextFlightReader.Read(html))
        {
            var stack = new Stack<JsonElement>(); stack.Push(root);
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Medal returned excessive page metadata.");
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (Text(item, "contentId") == id && item.TryGetProperty("contentUrl", out _)) found.Add(item.Clone());
                    foreach (var property in item.EnumerateObject()) stack.Push(property.Value);
                }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
        }
        var unique = found.DistinctBy(item => item.GetRawText()).ToArray();
        return unique.Length == 1 ? unique[0] : throw new NotSupportedException("Medal did not expose one complete identified requested public clip record.");
    }
    private static Uri Resource(string? raw, string id, string owner, bool image)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "cdn.medal.tv" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
            !image && (!url.AbsolutePath.StartsWith("/mediac/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) || image && !url.AbsolutePath.StartsWith("/ugcc/content-thumbnail/", StringComparison.Ordinal))
            throw new NotSupportedException("Medal returned an unverified supplied media origin or container.");
        var query = Query(url);
        if (!query.TryGetValue("auth", out var auth)) throw new NotSupportedException("Medal omitted a supported current media authorization descriptor.");
        var match = Regex.Match(auth, @"\Aexp=(?<expiry>[0-9]{1,20})~data=(?<data>[A-Za-z0-9_-]{1,1024})~hmac=[a-f0-9]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new InvalidDataException("Medal returned an invalid media authorization descriptor.");
        var encoded = match.Groups["data"].Value.Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
        string decoded; try { decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)); } catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { throw new InvalidDataException("Medal's media identity payload is malformed."); }
        var fields = decoded.Split(','); if (fields.Length != 4 || fields[0] != owner || fields[1] != id) throw new InvalidDataException("Medal's supplied media resource belongs to another clip/owner.");
        return url;
    }
    private static Dictionary<string, string> Query(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = pair.Split('=', 2); var key = Uri.UnescapeDataString(fields[0]); var value = fields.Length == 2 ? Uri.UnescapeDataString(fields[1]) : "";
            if (!result.TryAdd(key, value)) throw new InvalidDataException("Medal returned repeated media query fields.");
        }
        return result;
    }
    private static Uri HlsResource(string raw, string id)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "medal.tv" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/api/hls/" + id + "/master.m3u8") throw new InvalidDataException("Medal returned another clip or an unsupported HLS service route.");
        var query = Query(uri);
        if (query.Count != 1 || !query.TryGetValue("bebit", out var token) || token.Length is < 40 or > 65536 || !Regex.IsMatch(token, @"\A[A-Za-z0-9_+/=-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Medal returned an unsupported HLS authorization descriptor.");
        return uri;
    }
}
