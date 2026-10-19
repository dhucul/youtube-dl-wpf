using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bluesky;

public sealed class BlueskyProvider : IMediaProvider
{
    private const string Api = "https://public.api.bsky.app/xrpc/";
    public MediaProviderInfo Info { get; } = new("bluesky", "Bluesky public post videos and captions", 200);
    public bool CanHandle(Uri url) { try { BlueskyAddress.Parse(url); return true; } catch (ArgumentException) { return false; } }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = BlueskyAddress.Parse(request.Url);
        // Public AppView, DID directory and federated blob origins need no caller credentials.
        using var session = http.CreateAnonymousSession();
        var did = address.Actor;
        if (!BlueskyAddress.Did(did))
        {
            using var resolved = await JsonAsync(new(Api + "com.atproto.identity.resolveHandle?handle=" + Uri.EscapeDataString(address.Actor)), session, ct).ConfigureAwait(false);
            did = Text(resolved.RootElement, "did") ?? "";
            if (!BlueskyAddress.Did(did)) throw new InvalidDataException("Bluesky did not resolve the requested handle to a supported account identity.");
        }
        string? expectedCid = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        var servers = new Dictionary<string, Uri>(StringComparer.Ordinal);
        var originalDid = did; var originalHandle = BlueskyAddress.Did(address.Actor) ? null : address.Actor;
        for (var depth = 0; depth < 6; depth++)
        {
            var recordUri = address.Record(did);
            if (!seen.Add(recordUri)) throw new InvalidDataException("The Bluesky quoted-post chain is recursive.");
            using var response = await JsonAsync(new(Api + "app.bsky.feed.getPostThread?depth=0&parentHeight=0&uri=" + Uri.EscapeDataString(recordUri)), session, ct).ConfigureAwait(false);
            var thread = Object(response.RootElement, "thread");
            if (Text(thread, "$type") != "app.bsky.feed.defs#threadViewPost") throw new NotSupportedException("The requested Bluesky post is unavailable to public access.");
            var post = Object(thread, "post"); var author = Object(post, "author"); var record = Object(post, "record");
            if (Text(post, "uri") != recordUri || Text(author, "did") != did || Text(record, "$type") != "app.bsky.feed.post" ||
                expectedCid is not null && Text(post, "cid") != expectedCid) throw new InvalidDataException("Bluesky returned another post, author or quoted record revision.");
            Public(post); Public(author);
            var view = Object(post, "embed"); var embed = Object(record, "embed");
            if (Text(view, "$type") == "app.bsky.embed.recordWithMedia#view" && Text(embed, "$type") == "app.bsky.embed.recordWithMedia")
            { view = Object(view, "media"); embed = Object(embed, "media"); }
            if (Text(view, "$type") == "app.bsky.embed.video#view" && Text(embed, "$type") == "app.bsky.embed.video")
            {
                var pds = await Server(did).ConfigureAwait(false);
                return Video(post, author, record, view, embed, did, address.Key, pds);
            }
            if (Text(view, "$type") != "app.bsky.embed.record#view" || Text(embed, "$type") != "app.bsky.embed.record")
                throw new NotSupportedException("This Bluesky post does not expose a supported native video.");
            var quote = Object(view, "record"); var reference = Object(embed, "record");
            if (Text(quote, "$type") != "app.bsky.embed.record#viewRecord") throw new NotSupportedException("The quoted Bluesky record is unavailable to public access.");
            Public(quote); Public(Object(quote, "author"));
            if (Text(quote, "uri") != Text(reference, "uri") || Text(quote, "cid") != Text(reference, "cid")) throw new InvalidDataException("Bluesky returned mismatched quoted-record identities.");
            // Confirm the requested handle even when its post only quotes another author.
            if (depth == 0 && originalHandle is not null) _ = await Server(originalDid).ConfigureAwait(false);
            expectedCid = Cid(Text(reference, "cid")); address = BlueskyAddress.FromRecord(Text(reference, "uri")); did = address.Actor;
        }
        throw new NotSupportedException("The Bluesky quoted-post chain exceeds the supported depth.");
        async Task<Uri> Server(string account)
        {
            if (servers.TryGetValue(account, out var server)) return server;
            server = await PdsAsync(account, account == originalDid ? originalHandle : null, session, ct).ConfigureAwait(false);
            servers.Add(account, server); return server;
        }
    }
    private static PageMetadata Video(JsonElement post, JsonElement author, JsonElement record, JsonElement view, JsonElement embed, string did, string key, Uri pds)
    {
        var blob = Object(embed, "video"); var cid = Blob(blob, "video/mp4", 300_000_000);
        if (Cid(Text(view, "cid")) != cid) throw new InvalidDataException("Bluesky's displayed video does not match the post's original blob.");
        var playlist = VideoResource(Text(view, "playlist"), did, cid, "playlist.m3u8");
        var thumbnail = Text(view, "thumbnail") is { Length: > 0 } image ? VideoResource(image, did, cid, "thumbnail.jpg") : null;
        var captions = Array(embed, "captions");
        if (captions.Length > 20) throw new InvalidDataException("Bluesky returned too many captions.");
        var subtitles = new List<SubtitleTrack>(); var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var caption in captions)
        {
            var language = Text(caption, "lang");
            if (language is null || !Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant) || !languages.Add(language))
                throw new InvalidDataException("Bluesky returned invalid or repeated caption languages.");
            subtitles.Add(new() { Url = BlobUrl(pds, did, Blob(Object(caption, "file"), "text/vtt", 20_000)), Language = language });
        }
        var description = Text(record, "text") ?? Text(embed, "alt") ?? "";
        var title = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (title?.Length > 200) title = title[..200];
        return new() { Id = key, Extractor = "bluesky", ArchiveIdentity = new("bluesky:post", did + "/" + key), Title = string.IsNullOrWhiteSpace(title) ? key : title,
            Description = description, Uploader = Text(author, "displayName") is { Length: > 0 } name ? name : did,
            SourcePageUrl = new BlueskyAddress(did, key).Web, ThumbnailUrl = thumbnail?.AbsoluteUri, Subtitles = subtitles, AnonymousPlayback = true,
            Media = [new() { Id = "hls", Url = playlist, Kind = MediaKind.Hls, Extension = "m3u8", HasVideo = true, IsCompleteMedia = true, Source = "bluesky", VerificationDemuxer = "mpegts" },
                new() { Id = "original", Url = BlobUrl(pds, did, cid), Kind = MediaKind.Progressive, Extension = "mp4", MimeType = "video/mp4", HasVideo = true,
                    IsCompleteMedia = true, FileSize = Long(blob, "size"), VerificationFileSize = Long(blob, "size"), Source = "bluesky", Label = "Original upload", VerificationDemuxer = "mov" }] };
    }
    private static async Task<Uri> PdsAsync(string did, string? handle, MediaHttp http, CancellationToken ct)
    {
        var endpoint = did.StartsWith("did:plc:", StringComparison.Ordinal) ? new Uri("https://plc.directory/" + did) : new Uri("https://" + did[8..] + "/.well-known/did.json");
        using var response = await JsonAsync(endpoint, http, ct).ConfigureAwait(false); var root = response.RootElement;
        if (Text(root, "id") != did) throw new InvalidDataException("The Bluesky DID document belongs to another account.");
        if (handle is not null)
        {
            var declared = Array(root, "alsoKnownAs").Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)
                .FirstOrDefault(value => value.StartsWith("at://", StringComparison.Ordinal) && BlueskyAddress.Handle(value[5..]));
            if (!string.Equals(declared, "at://" + handle, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The requested Bluesky handle is not confirmed by its DID document.");
        }
        var service = Array(root, "service").FirstOrDefault(item => Text(item, "id") is { } id && (id == "#atproto_pds" || id == did + "#atproto_pds") && Text(item, "type") == "AtprotoPersonalDataServer");
        if (!Uri.TryCreate(Text(service, "serviceEndpoint"), UriKind.Absolute, out var pds) || pds.Scheme != "https" || pds.UserInfo.Length != 0 || pds.Query.Length != 0 ||
            pds.Fragment.Length != 0 || pds.AbsolutePath != "/" || !pds.IsDefaultPort || !BlueskyAddress.Handle(pds.Host)) throw new NotSupportedException("Bluesky did not expose a supported public blob server.");
        return pds;
    }
    private static Uri BlobUrl(Uri pds, string did, string cid) => new(pds, "/xrpc/com.atproto.sync.getBlob?did=" + Uri.EscapeDataString(did) + "&cid=" + Uri.EscapeDataString(cid));
    private static string Blob(JsonElement blob, string mime, long maximum)
    {
        if (Text(blob, "$type") != "blob" || Text(blob, "mimeType") != mime || Long(blob, "size") is not > 0 || Long(blob, "size") > maximum)
            throw new InvalidDataException("Bluesky returned an invalid or unsupported original blob descriptor.");
        return Cid(Text(Object(blob, "ref"), "$link"));
    }
    private static string Cid(string? value) => value is not null && Regex.IsMatch(value, @"\Ab[a-z2-7]{20,120}\z", RegexOptions.CultureInvariant) ? value : throw new InvalidDataException("Bluesky returned an unsupported content identifier.");
    private static Uri VideoResource(string? raw, string did, string cid, string file)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "video.bsky.app" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new NotSupportedException("Bluesky returned an unverified video origin.");
        if (Uri.UnescapeDataString(url.AbsolutePath) != "/watch/" + did + "/" + cid + "/" + file) throw new InvalidDataException("Bluesky's media resource belongs to another account or video.");
        return url;
    }
    private static void Public(JsonElement value)
    {
        var viewer = Object(value, "viewer");
        if (True(viewer, "blockedBy") || Text(viewer, "blocking") is { Length: > 0 } ||
            Array(value, "labels").Any(label => !True(label, "neg") && Text(label, "val") == "!no-unauthenticated"))
            throw new NotSupportedException("This Bluesky record requires additional account access.");
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != endpoint) throw new InvalidDataException("Bluesky redirected a metadata response to another resource.");
        try { return JsonDocument.Parse(response.Text, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { throw new InvalidDataException("Bluesky returned malformed public metadata."); }
    }
}
