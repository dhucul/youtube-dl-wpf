using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Mastodon;

public sealed class MastodonProvider : IMediaProvider
{
    // Federated instances have no fixed domain list. Specific site providers retain priority.
    public MediaProviderInfo Info { get; } = new("mastodon", "Mastodon public status attachments", 190);
    public bool CanHandle(Uri url) => MastodonAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = MastodonAddress.Parse(request.Url);
        using var anonymous = http.CreateAnonymousSession();
        var response = await anonymous.GetDocumentAsync(address.Api, 4 * 1024 * 1024, ct).ConfigureAwait(false);
        if (response.Url != address.Api) throw new InvalidDataException("Mastodon redirected its status API to another resource.");
        using var document = ParseJson(response.Text);
        var post = document.RootElement;
        if (Text(post, "id") != address.Id) throw new InvalidDataException("Mastodon returned another status identity.");
        var rootIdentity = Identity(post);
        var account = Object(post, "account"); var acct = Text(account, "acct");
        if (address.Account is not null && !string.Equals(address.Account, acct, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mastodon returned another requested account.");
        if (rootIdentity.Origin == address.Origin && rootIdentity.Id != address.Id || rootIdentity.Origin != address.Origin && address.Account is not null && !address.Account.Contains('@'))
            throw new InvalidDataException("Mastodon's origin and local status identities disagree.");
        var seen = new HashSet<string>(StringComparer.Ordinal); var depth = 0;
        while (Object(post, "reblog") is { ValueKind: JsonValueKind.Object } boosted)
        {
            if (!seen.Add(rootIdentity.Uri.AbsoluteUri) || ++depth > 5) throw new InvalidDataException("The Mastodon boost chain is recursive or too deep.");
            if (Array(post, "media_attachments").Length != 0) throw new InvalidDataException("A Mastodon boost mixes original and wrapper attachments.");
            post = boosted; rootIdentity = Identity(post);
            if (seen.Contains(rootIdentity.Uri.AbsoluteUri)) throw new InvalidDataException("The Mastodon boost chain repeats a status identity.");
        }
        var description = Plain(Text(post, "content") ?? "");
        var spoiler = Plain(Text(post, "spoiler_text") ?? "");
        if (spoiler.Length > 0) description = spoiler + (description.Length > 0 ? "\n" + description : "");
        var title = description.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Mastodon " + rootIdentity.Id;
        if (title.Length > 200) title = title[..200];
        account = Object(post, "account");
        var uploader = Text(account, "display_name") is { Length: > 0 } name ? Plain(name) : Text(account, "acct");
        if (!post.TryGetProperty("media_attachments", out var suppliedAttachments) || suppliedAttachments.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Mastodon did not supply a complete attachment inventory.");
        var attachments = suppliedAttachments.EnumerateArray().ToArray();
        if (attachments.Length > 64 || attachments.Length > request.MaximumPlaylistEntries) throw new NotSupportedException("The Mastodon attachment inventory exceeds the configured limit.");
        var entries = new List<NativePlaylistEntry>(); var playable = new List<PageMetadata>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attachment in attachments)
        {
            var id = Text(attachment, "id");
            if (id is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) || !ids.Add(id))
                throw new InvalidDataException("Mastodon returned invalid or repeated attachment identities.");
            var type = Text(attachment, "type"); var position = entries.Count + 1;
            var itemId = rootIdentity.Id + "-" + id;
            if (type is not ("video" or "gifv" or "audio") || Text(attachment, "url") is not { Length: > 0 } raw)
            { entries.Add(new(position, itemId, title, null, null, null, false)); continue; }
            var resource = Resource(raw); var audio = type == "audio"; var extension = Path.GetExtension(resource.AbsolutePath).TrimStart('.').ToLowerInvariant();
            var demuxer = extension switch { "mp4" or "m4a" or "mov" => "mov", "mp3" => "mp3", "ogg" or "opus" => "ogg", "webm" => "matroska", "flac" => "flac", "wav" => "wav", _ => null };
            if (demuxer is null || !audio && extension is not ("mp4" or "mov" or "webm") || audio && extension == "mov")
                throw new NotSupportedException("This Mastodon attachment container requires additional support.");
            var meta = Object(attachment, "meta"); var original = Object(meta, "original");
            var duration = Number(original, "duration") ?? Number(meta, "duration");
            var width = Integer(original, "width") ?? Integer(meta, "width"); var height = Integer(original, "height") ?? Integer(meta, "height");
            if (duration is <= 0 or > 604800 || width is <= 0 or > 65536 || height is <= 0 or > 65536) throw new InvalidDataException("Mastodon returned unsupported media dimensions or duration.");
            var preview = Text(attachment, "preview_url") is { Length: > 0 } image && type != "audio" ? Resource(image).AbsoluteUri : null;
            var alt = Text(attachment, "description");
            var itemDescription = string.IsNullOrWhiteSpace(alt) ? description : description + (description.Length > 0 ? "\n\n" : "") + alt;
            var page = new PageMetadata { Id = itemId, Extractor = "mastodon", ArchiveIdentity = new("mastodon:media", rootIdentity.Uri.AbsoluteUri + "#" + id),
                Title = title, Description = itemDescription, Uploader = uploader, DurationSeconds = duration, ThumbnailUrl = preview, SourcePageUrl = rootIdentity.Web, AnonymousPlayback = true,
                Media = [new() { Id = "original", Url = resource, Kind = MediaKind.Progressive, Extension = extension,
                    HasVideo = !audio, HasAudio = audio ? true : type == "gifv" ? false : null, IsCompleteMedia = true, Width = audio ? null : width, Height = audio ? null : height,
                    FramesPerSecond = audio ? null : Rate(Text(original, "frame_rate")) ?? Number(meta, "fps"), Bandwidth = Number(original, "bitrate"), Source = "mastodon",
                    VerificationDemuxer = demuxer, VerificationDurationSeconds = duration }] };
            // This is one asset inside the post. Refresh its parent inventory by
            // attachment identity; reopening the post is not a distinct child.
            playable.Add(page); entries.Add(new(position, itemId, title, resource, duration, preview, true, page, page.ArchiveIdentity));
        }
        if (playable.Count == 1) return playable[0] with { Id = rootIdentity.Id, ArchiveIdentity = new("mastodon", rootIdentity.Uri.AbsoluteUri) };
        if (playable.Count == 0) return new() { Id = rootIdentity.Id, Extractor = "mastodon", Title = title, Description = description, Uploader = uploader,
            SourcePageUrl = rootIdentity.Web, ArchiveIdentity = new("mastodon", rootIdentity.Uri.AbsoluteUri), Warnings = ["This public Mastodon status exposes no ready video/audio attachments. Image posts, cards and quoted posts need additional support."] };
        return new() { Id = rootIdentity.Id, PlaylistId = rootIdentity.Id, Extractor = "mastodon:status", IsPlaylist = true, Title = title, Description = description,
            Uploader = uploader, SourcePageUrl = rootIdentity.Web, Entries = entries, ArchiveIdentity = new("mastodon", rootIdentity.Uri.AbsoluteUri) };
    }
    private static (Uri Uri, Uri Web, Uri Origin, string Id) Identity(JsonElement post)
    {
        if (Text(post, "visibility") is not ("public" or "unlisted")) throw new NotSupportedException("This Mastodon status requires additional account access.");
        var account = Object(post, "account");
        if (True(account, "suspended") || True(Object(account, "relationship"), "blocked_by")) throw new NotSupportedException("This Mastodon account is unavailable to public access.");
        if (Text(post, "id") is not { } id || !Regex.IsMatch(id, @"\A[1-9][0-9]{0,39}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Mastodon returned an invalid local status ID.");
        var uri = Resource(Text(post, "uri")); var canonical = StatusAddress(uri);
        if (!uri.AbsolutePath.StartsWith("/users/", StringComparison.Ordinal) || uri.Query.Length != 0 || canonical.Account != Text(account, "username"))
            throw new InvalidDataException("Mastodon's ActivityPub identity does not match the returned author.");
        if (uri.AbsolutePath.EndsWith("/activity", StringComparison.Ordinal) && Object(post, "reblog").ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A Mastodon boost activity has no original status.");
        var accountUrl = Resource(Text(account, "url")); var expectedAccount = new Uri(canonical.Origin, "/@" + canonical.Account);
        if (accountUrl != expectedAccount || accountUrl.Query.Length != 0) throw new InvalidDataException("Mastodon's author origin does not match the status origin.");
        var acct = Text(account, "acct");
        if (acct is null || !string.Equals(acct, canonical.Account, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(acct, canonical.Account + "@" + canonical.Origin.IdnHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mastodon's account handle disagrees with its origin.");
        var web = Text(post, "url") is { Length: > 0 } raw ? Resource(raw) : new Uri(canonical.Origin, "/@" + canonical.Account + "/" + canonical.Id);
        var address = StatusAddress(web);
        if (web.Query.Length != 0 || address.Origin != canonical.Origin || address.Id != canonical.Id || address.Account != canonical.Account)
            throw new InvalidDataException("Mastodon's web URL does not identify its original status.");
        return (uri, web, canonical.Origin, canonical.Id);
    }
    private static Uri Resource(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var url) && MastodonAddress.PublicHttps(url) ? url :
        throw new InvalidDataException("Mastodon returned an unsupported public resource address.");
    private static MastodonAddress StatusAddress(Uri url)
    { try { return MastodonAddress.Parse(url); } catch (ArgumentException) { throw new InvalidDataException("Mastodon returned an unsupported status identity layout."); } }
    private static JsonDocument ParseJson(string text)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Mastodon returned malformed public status metadata."); }
        try
        {
            var nodes = new Stack<JsonElement>(); nodes.Push(document.RootElement); var count = 0;
            while (nodes.TryPop(out var node))
            {
                if (++count > 100000) throw new InvalidDataException("Mastodon returned too many metadata fields.");
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in node.EnumerateObject())
                    { if (!names.Add(property.Name)) throw new InvalidDataException("Mastodon returned repeated metadata fields."); nodes.Push(property.Value); }
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) nodes.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static double? Rate(string? value)
    {
        if (value is null) return null; var parts = value.Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var denominator) && numerator > 0 && denominator > 0 && numerator / denominator <= 1000 ? numerator / denominator : null;
    }
    private static string Plain(string html)
    {
        var lineBreaks = Regex.Replace(html, @"</p\s*>|<br\s*/?>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var text = Regex.Replace(lineBreaks, "<[^>]*>", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return WebUtility.HtmlDecode(text).Trim();
    }
}
