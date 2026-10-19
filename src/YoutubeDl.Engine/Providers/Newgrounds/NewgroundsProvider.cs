using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Newgrounds;

public sealed partial class NewgroundsProvider(INewgroundsPageSource? browser = null) : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("newgrounds", "Newgrounds public audio movies and collections", 200);
    public bool CanHandle(Uri url) => NewgroundsAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => NewgroundsAddress.Parse(request.Url) is { Kind: "audio" or "video" } address ? Archive(address) : null;
    public static bool IsMetadataAddress(Uri url, Uri root) => NewgroundsAddress.IsMetadataAddress(url, root);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = NewgroundsAddress.Parse(request.Url); request = request with { Url = address.Web };
        try
        {
            var document = request.CaptureText is { Length: > 0 } captured ? new MediaDocument(address.Web, captured, "text/html") :
                await http.GetDocumentAsync(address.Web, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            if (!NewgroundsAddress.IsMetadataAddress(document.Url, address.Web)) throw new InvalidDataException("Newgrounds redirected to another requested identity.");
            if (document.Text.Contains("<title>NG Guard</title>", StringComparison.Ordinal)) throw new HttpRequestException("Newgrounds requires a public browser session.", null, HttpStatusCode.Forbidden);
            if (address.Kind is "playlist" or "user-audio" or "user-movies") return await CollectionAsync(address, request, document, http, ct).ConfigureAwait(false);
            return address.Kind == "audio" ? Audio(address, document.Text, ct) : await VideoAsync(address, document.Text, http, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden && browser is not null && request.CaptureText is null)
        { return await browser.ExtractAsync(request, ct).ConfigureAwait(false); }
    }
    private static DownloadArchiveIdentity Archive(NewgroundsAddress address) => new("newgrounds:" + address.Kind, address.Id);
    internal static PageMetadata Audio(NewgroundsAddress address, string html, CancellationToken ct)
    {
        const string marker = "NgAudioPlayer.fromListenPage("; string? json = null;
        foreach (Match script in Regex.Matches(html, @"<script\b[^>]*>(?<body>[\s\S]*?)</script>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
        {
            ct.ThrowIfCancellationRequested(); var body = script.Groups["body"].Value; var offset = body.IndexOf(marker, StringComparison.Ordinal); if (offset < 0) continue;
            if (json is not null || body.IndexOf(marker, offset + marker.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("Newgrounds returned conflicting player objects.");
            var data = JavaScriptDataLiteral.ObjectArgument(body, offset + marker.Length, out var end, ct);
            if (body[end..].TrimStart() is not { Length: > 0 } tail || tail[0] is not (',' or ')')) throw new InvalidDataException("Newgrounds uses a computed player argument.");
            json = data.ToJsonString();
        }
        if (json is null) throw new NotSupportedException("This Newgrounds audio page is unavailable, restricted or uses an unsupported player layout.");
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (Id(root, "generic_id") != address.Id || Integer(root, "type_id") != 3) throw new InvalidDataException("Newgrounds returned another audio identity.");
        var url = PublicUrl(Text(root, "url")); var name = Path.GetFileName(url.AbsolutePath);
        if (url.Host != "audio.ngfiles.com" || !name.StartsWith(address.Id + "_", StringComparison.Ordinal) || !name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Newgrounds returned audio outside the requested submission.");
        var duration = Number(root, "duration"); if (duration is not > 0 or > 604800) throw new InvalidDataException("Newgrounds omitted a supported complete audio duration.");
        var title = Text(root, "title") ?? throw new InvalidDataException("Newgrounds omitted its player title."); var author = Text(root, "author");
        var metadata = Facts(address, html); var poster = Text(root, "icon_url");
        if (poster is not null) ValidateImage(poster, address);
        return metadata with { Title = title, Uploader = author, DurationSeconds = duration, ThumbnailUrl = poster ?? metadata.ThumbnailUrl,
            Media = [new() { Id = "mp3", Url = url, Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false, Kind = MediaKind.Progressive,
                Source = "newgrounds", IsCompleteMedia = true, VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static async Task<PageMetadata> VideoAsync(NewgroundsAddress address, string html, MediaHttp http, CancellationToken ct)
    {
        var movie = Regex.Match(html, @"data-movie-id\s*=\s*[""'](?<id>[0-9]+)[""']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!movie.Success)
        {
            const string marker = "ngutils.components.video.player.getGlobal("; var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var options = JavaScriptDataLiteral.ObjectArgument(html, start + marker.Length, out _, ct);
                var barrier = options["barrier"]?.GetValue<string>();
                if (barrier is not null) movie = Regex.Match(barrier, @"data-movie-id\s*=\s*[""'](?<id>[0-9]+)[""']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
        }
        if (!movie.Success || movie.Groups["id"].Value != address.Id) throw new NotSupportedException("This Newgrounds submission is not an offered native video player.");
        var projects = Regex.Matches(html, @"authorlinks(?<id>[1-9][0-9]{0,14})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Select(match => match.Groups["id"].Value).Distinct().ToArray();
        if (projects.Length != 1) throw new InvalidDataException("Newgrounds did not bind an unambiguous video project.");
        var endpoint = new Uri("https://www.newgrounds.com/portal/video/" + address.Id);
        var response = await http.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct, new Dictionary<string, string> { ["X-Requested-With"] = "XMLHttpRequest", ["Referer"] = address.Web.AbsoluteUri, ["Accept"] = "application/json" }).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("Newgrounds redirected video metadata to another identity.");
        using var document = JsonDocument.Parse(response.Text); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) throw new NotSupportedException("Newgrounds refused complete public video metadata.");
        var canonical = Text(root, "url"); if (Id(root, "id") != address.Id || canonical is not null && NewgroundsAddress.Parse(new Uri(address.Web, canonical)) != address) throw new InvalidDataException("Newgrounds returned another canonical movie.");
        var sources = Object(root, "sources"); if (sources.ValueKind != JsonValueKind.Object) throw new NotSupportedException("Newgrounds did not expose clear complete movie resources.");
        var media = new List<MediaCandidate>(); var ids = new HashSet<string>(StringComparer.Ordinal); var project = projects[0];
        foreach (var group in sources.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested(); if (!ids.Add(group.Name) || group.Value.ValueKind != JsonValueKind.Array || group.Value.GetArrayLength() is < 1 or > 8)
                throw new InvalidDataException("Newgrounds returned an invalid movie format group.");
            var height = group.Name is "4k" ? 2160 : group.Name is "8k" ? 4320 : int.TryParse(group.Name.TrimEnd('p'), out var pixels) && pixels is > 0 and <= 4320 ? pixels : (int?)null;
            if (height is null) throw new NotSupportedException("This Newgrounds movie quality label needs additional native support.");
            var index = 0;
            foreach (var source in group.Value.EnumerateArray())
            {
                var raw = source.ValueKind == JsonValueKind.String ? source.GetString() : Text(source, "src"); var url = PublicUrl(raw);
                if (url.Host != "uploads.ungrounded.net" || !Regex.IsMatch(url.AbsolutePath, @"\A/alternate/[0-9]+/" + project + @"_alternate_[0-9]+(?:\.[^/]*)?\.mp4\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidDataException("Newgrounds returned movie media outside the declared project.");
                var mime = source.ValueKind == JsonValueKind.Object ? Text(source, "type") : null; if (mime is not null && mime != "video/mp4") throw new NotSupportedException("This Newgrounds movie encoding needs additional support.");
                media.Add(new() { Id = group.Name + (index == 0 ? "" : "-" + index), Url = url, Height = height, Extension = "mp4", MimeType = "video/mp4", Kind = MediaKind.Progressive,
                    HasVideo = true, HasAudio = null, Source = "newgrounds", IsCompleteMedia = true, VerificationDemuxer = "mov", VerificationDurationSeconds = Number(root, "duration") }); index++;
            }
        }
        if (media.Count is < 1 or > 40) throw new InvalidDataException("Newgrounds returned an empty or excessive movie inventory.");
        var metadata = Facts(address, html);
        return metadata with { Title = Text(root, "title") ?? metadata.Title, Uploader = Text(root, "author") ?? metadata.Uploader, DurationSeconds = Number(root, "duration"), Media = media };
    }
    private static PageMetadata Facts(NewgroundsAddress address, string html)
    {
        var generic = PageMetadataReader.Read(html, address.Web); var title = MatchText(html, @"<h2\b[^>]*>(?<text>[\s\S]*?)</h2>") ?? generic.Title;
        var uploaded = MatchText(html, @"<dt\b[^>]*>\s*Uploaded\s*</dt>\s*<dd\b[^>]*>(?<text>[\s\S]*?)</dd>");
        var day = uploaded is not null && Regex.Match(uploaded, @"[A-Za-z]{3}\s+[0-9]{1,2},\s+[0-9]{4}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) is { Success: true } date &&
            DateOnly.TryParseExact(date.Value, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (DateOnly?)null;
        var views = MatchText(html, @"<dt\b[^>]*>\s*(?:Listens|Views)\s*</dt>\s*<dd\b[^>]*>(?<text>[\s\S]*?)</dd>");
        var author = MatchText(html, @"<h4\b[^>]*>\s*<a\b[^>]*>(?<text>[^<]+)</a>\s*</h4>");
        var age = Regex.Match(html, @"(?:rated-|rating-)(?<rating>[etma])\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var description = MatchText(html, @"<div\b[^>]*id=[""']author_comments[""'][^>]*>(?<text>[\s\S]*?)</div>");
        var poster = generic.ThumbnailUrl; if (poster is not null) ValidateImage(poster, address);
        return new() { Id = address.Id, ArchiveIdentity = Archive(address), Extractor = "newgrounds", Title = title, Uploader = author, Description = description,
            ThumbnailUrl = poster, SourcePageUrl = address.Web, AnonymousPlayback = true, UploadDate = day, ViewCount = long.TryParse(views?.Replace(",", ""), out var count) ? count : null,
            AgeLimit = age.Success ? age.Groups["rating"].Value switch { "e" => 0, "t" => 13, "m" => 17, _ => 18 } : null,
            PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = address.Web.AbsoluteUri } };
    }
    private static string? MatchText(string html, string pattern)
    {
        var match = Regex.Match(html, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        return match.Success ? WebUtility.HtmlDecode(Regex.Replace(match.Groups["text"].Value, @"<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).Trim() : null;
    }
    private static void ValidateImage(string raw, NewgroundsAddress address)
    {
        var uri = PublicUrl(raw); if (uri.Host is not ("aicon.ngfiles.com" or "picon.ngfiles.com" or "img.ngfiles.com") ||
            !Path.GetFileName(uri.AbsolutePath).Contains(address.Id, StringComparison.Ordinal)) throw new InvalidDataException("Newgrounds returned artwork outside the requested submission.");
    }
    private static Uri PublicUrl(string? raw) => raw is not null && raw.Length <= 32768 && Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Newgrounds returned an invalid public resource.");
    private static string Id(JsonElement root, string field) => Long(root, field) is > 0 and < 1000000000000000 ? Long(root, field)!.Value.ToString(CultureInfo.InvariantCulture) : throw new InvalidDataException("Newgrounds returned an invalid numeric identity.");
}
