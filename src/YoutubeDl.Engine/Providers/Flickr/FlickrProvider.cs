using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Flickr;

/// <summary>Reads public Flickr video declarations and offered player streams without executing scripts.</summary>
public sealed class FlickrProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("flickr", "Flickr public video posts", 200);
    public bool CanHandle(Uri url) => FlickrAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("flickr", FlickrAddress.Parse(request.Url).Id);

    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = FlickrAddress.Parse(request.Url);
        using var anonymous = http.CreateAnonymousSession();
        using var session = anonymous.WithHeaders(new Dictionary<string, string> { ["Referer"] = address.Page.AbsoluteUri,
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? MediaHttp.DefaultUserAgent : request.UserAgent });
        var document = await session.GetDocumentAsync(address.Page, 6 * 1024 * 1024, ct).ConfigureAwait(false);
        if (FlickrAddress.Parse(document.Url).Id != address.Id) throw new InvalidDataException("Flickr redirected to another post.");
        using var state = ReadState(document.Text); var main = Object(state.RootElement, "main");
        var photos = Array(main, "photo-models").Select(item => Object(item, "data")).Where(item => Text(item, "id") == address.Id).ToArray();
        if (photos.Length != 1) throw new InvalidDataException("Flickr did not return one requested photo/video model.");
        var photo = photos[0];
        if (Text(photo, "mediaType") != "video") throw new NotSupportedException("This Flickr post is a still image, not an offered video.");
        var privacy = Array(main, "photo-privacy-models").Select(item => Object(item, "data")).Where(item => Text(item, "id") == address.Id).ToArray();
        if (privacy.Length != 1 || !True(privacy[0], "isPublic")) throw new NotSupportedException("This Flickr video needs additional native account/private-sharing support.");
        var secret = Text(photo, "secret");
        if (secret is null || !Regex.IsMatch(secret, @"\A[a-f0-9]{8,64}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Flickr omitted a supported public video resource identity.");
        var key = ReadKey(document.Text);
        using var metadata = await Api("flickr.photos.getInfo").ConfigureAwait(false); var info = Object(metadata.RootElement, "photo");
        if (Text(info, "id") != address.Id || Text(info, "secret") != secret) throw new InvalidDataException("Flickr returned another video's metadata.");
        if (Text(info, "media") != "video" || Integer(Object(info, "visibility"), "ispublic") != 1)
            throw new NotSupportedException("Flickr did not confirm public video access.");
        var video = Object(info, "video");
        if (Integer(video, "ready") != 1 || Integer(video, "failed") != 0 || Integer(video, "pending") != 0)
            throw new NotSupportedException("This Flickr video is incomplete, failed or still processing.");
        var duration = Number(video, "duration");
        if (duration is not > 0 || duration > 7 * 24 * 3600) throw new InvalidDataException("Flickr omitted a supported complete video duration.");
        var pageOwner = Object(Object(photo, "owner"), "data"); var owner = Object(info, "owner");
        if (Text(pageOwner, "id") is not { Length: > 0 } ownerId || Text(owner, "nsid") != ownerId)
            throw new InvalidDataException("Flickr returned conflicting video ownership.");
        using var playback = await Api("flickr.video.getStreamInfo").ConfigureAwait(false);
        var streams = Array(Object(playback.RootElement, "streams"), "stream");
        if (streams.Length is < 1 or > 64) throw new InvalidDataException("Flickr returned an empty or excessive playback inventory.");
        var media = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stream in streams)
        {
            var type = Text(stream, "type") ?? Integer(stream, "type")?.ToString(CultureInfo.InvariantCulture);
            if (type is null || !Regex.IsMatch(type, @"\A[A-Za-z0-9_-]{1,40}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || !seen.Add(type))
                throw new InvalidDataException("Flickr returned a missing or repeated stream type.");
            var hls = type == "hls"; var url = Resource(Text(stream, "_content"), address.Id, secret, type, hls);
            int? height = !hls && Regex.IsMatch(type, @"\A[1-9][0-9]{1,4}p\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ?
                int.Parse(type[..^1], CultureInfo.InvariantCulture) : null;
            media.Add(new() { Id = type, Url = url, Kind = hls ? MediaKind.Hls : MediaKind.Progressive, Extension = hls ? "ts" : "mp4",
                MimeType = hls ? "application/x-mpegURL" : "video/mp4", Height = height, HasVideo = true, HasAudio = null, IsCompleteMedia = true,
                Source = "flickr", Label = type, VerificationDemuxer = hls ? "mpegts" : "mov", VerificationDurationSeconds = duration });
        }
        var title = Text(Object(info, "title"), "_content") ?? Text(photo, "title") ?? "Flickr video " + address.Id;
        var posters = Object(Object(photo, "sizes"), "data"); var thumbnails = new List<ThumbnailCandidate>();
        if (posters.ValueKind == JsonValueKind.Object)
        {
            if (posters.EnumerateObject().Count() > 256) throw new InvalidDataException("Flickr returned too many poster variants.");
            foreach (var size in posters.EnumerateObject())
            {
                var data = Object(size.Value, "data"); var image = Image(Text(data, "displayUrl"), address.Id);
                if (image is not null) thumbnails.Add(new() { Id = size.Name, Url = image, Width = Integer(data, "width"), Height = Integer(data, "height") });
            }
        }
        var poster = thumbnails.OrderByDescending(item => (long)(item.Width ?? 0) * (item.Height ?? 0)).FirstOrDefault();
        return new() { Id = address.Id, Extractor = Info.Id, ArchiveIdentity = new("flickr", address.Id), Title = title,
            Description = Text(Object(info, "description"), "_content") ?? Text(photo, "description"), Uploader = Text(owner, "realname") ?? Text(owner, "username"),
            DurationSeconds = duration, ThumbnailUrl = poster?.Url.AbsoluteUri, Thumbnails = thumbnails, Media = media, SourcePageUrl = document.Url, AnonymousPlayback = true };

        async Task<JsonDocument> Api(string method)
        {
            var endpoint = new Uri("https://api.flickr.com/services/rest/?method=" + method + "&api_key=" + key + "&photo_id=" + address.Id + "&secret=" + secret + "&format=json&nojsoncallback=1");
            var response = await session.GetDocumentAsync(endpoint, 2 * 1024 * 1024, ct).ConfigureAwait(false);
            if (response.Url != endpoint) throw new InvalidDataException("Flickr redirected its public media lookup.");
            var json = JsonDocument.Parse(response.Text, new() { MaxDepth = 48 });
            if (Text(json.RootElement, "stat") == "ok") return json;
            var code = Integer(json.RootElement, "code"); json.Dispose();
            throw new NotSupportedException("Flickr refused the public video lookup" + (code is { } number ? " (code " + number + ")." : "."));
        }
    }
    internal static JsonDocument ReadState(string html)
    {
        var scripts = Scripts(html).Where(match => Regex.IsMatch(match.Groups["attrs"].Value, "\\bclass\\s*=\\s*([\"'])modelExport\\1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).ToArray();
        if (scripts.Length != 1) throw new NotSupportedException("Flickr did not expose one supported public page model.");
        var body = scripts[0].Groups["body"].Value;
        var starts = Regex.Matches(body, @"\bmodelExport\s*:\s*(?=\{)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (starts.Count != 1) throw new InvalidDataException("Flickr returned conflicting or missing public page models.");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body[(starts[0].Index + starts[0].Length)..]);
            var reader = new Utf8JsonReader(bytes, new() { MaxDepth = 48 }); return JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException) { throw new InvalidDataException("Flickr returned invalid page model JSON."); }
    }
    private static string ReadKey(string html)
    {
        var values = Scripts(html).SelectMany(match => Regex.Matches(match.Groups["body"].Value, "\\broot\\.YUI_config\\.flickr\\.api\\.site_key\\s*=\\s*\"(?<value>[a-f0-9]{32})\"\\s*;", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>())
            .Select(match => match.Groups["value"].Value).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 ? values[0] : throw new NotSupportedException("Flickr did not expose one supported public player key.");
    }
    private static IEnumerable<Match> Scripts(string html)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->|<(?:template|textarea)\b[^>]*>.*?</(?:template|textarea)\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        return Regex.Matches(clean, @"<script\b(?<attrs>[^>]*)>(?<body>.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)).Cast<Match>();
    }
    private static Uri Resource(string? value, string id, string secret, string type, bool hls)
    {
        var expected = hls ? "/stream/" + id + "/" + secret + "/master.m3u8" : "/video/" + id + "/" + secret + "/" + type + ".mp4";
        return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "live.staticflickr.com" && url.IsDefaultPort &&
            url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.AbsolutePath == expected ? url : throw new InvalidDataException("Flickr returned an unsupported or mismatched video resource.");
    }
    private static Uri? Image(string? value, string id)
    {
        if (value?.StartsWith("//", StringComparison.Ordinal) == true) value = "https:" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "live.staticflickr.com" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 &&
            Regex.IsMatch(url.AbsolutePath, @"\A/[0-9]+/" + id + @"_[a-f0-9]+(?:_[A-Za-z0-9]+)?\.(?:jpg|png)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? url : null;
    }
}
