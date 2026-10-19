using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Spreaker;

public sealed class SpreakerProvider : IMediaProvider
{
    private const string Api = "https://api.spreaker.com/v2/";
    public MediaProviderInfo Info { get; } = new("spreaker", "Spreaker public recorded episodes and complete shows", 200);
    public bool CanHandle(Uri uri) => SpreakerAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    { var address = SpreakerAddress.Parse(request.Url); return address.IsShow ? null : new("Spreaker", address.Identity); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = SpreakerAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        using var document = await JsonAsync(new(Api + (address.IsShow ? "shows/" : "episodes/") + Uri.EscapeDataString(address.Identity)), anonymous, ct).ConfigureAwait(false);
        var root = Object(document.RootElement, "response");
        if (!address.IsShow) return Episode(Object(root, "episode"), address.Identity);
        var show = Object(root, "show"); var id = Id(show, "show_id"); var author = Id(show, "author_id");
        if (SpreakerAddress.Numeric(address.Identity) ? id != address.Identity : Text(show, "slug") != address.Identity && Text(show, "permalink") != address.Identity)
            throw new InvalidDataException("Spreaker returned another requested show identity.");
        Access(show); var showLink = Link(Text(show, "site_url"), id, true);
        var linkIdentity = SpreakerAddress.Parse(showLink).Identity;
        if (!SpreakerAddress.Numeric(linkIdentity) && linkIdentity != Text(show, "slug") && linkIdentity != Text(show, "permalink")) throw new InvalidDataException("Spreaker's show link conflicts with its alias.");
        var entries = new List<NativePlaylistEntry>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var endpoint = new Uri(Api + "shows/" + id + "/episodes?filter=listenable&sorting=newest&limit=100");
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            ct.ThrowIfCancellationRequested(); if (!seenPages.Add(endpoint.AbsoluteUri)) throw new InvalidDataException("Spreaker repeated a show continuation.");
            using var page = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var response = Object(page.RootElement, "response");
            if (!response.TryGetProperty("items", out var supplied) || supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Spreaker omitted its episode inventory.");
            var items = supplied.EnumerateArray().ToArray(); if (items.Length > 100) throw new InvalidDataException("Spreaker returned an oversized episode page.");
            foreach (var item in items)
            {
                var episode = Id(item, "episode_id"); if (Id(item, "show_id") != id || Id(item, "author_id") != author || !ids.Add(episode)) throw new InvalidDataException("Spreaker changed show ownership or repeated an episode.");
                var link = Link(Text(item, "site_url"), episode, false); var available = Text(item, "type") == "RECORDED" && Public(item) && !ExplicitFalse(item, "download_enabled");
                if (entries.Count >= Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Spreaker show exceeds the configured inventory limit.");
                entries.Add(new(entries.Count + 1, episode, Text(item, "title") ?? episode, available ? link : null, DurationSeconds(item), Poster(item), available, ExpectedArchiveIdentity: new("Spreaker", episode)));
            }
            if (!response.TryGetProperty("next_url", out var next)) throw new InvalidDataException("Spreaker omitted its terminal pagination state.");
            if (next.ValueKind == JsonValueKind.Null) break;
            if (next.ValueKind != JsonValueKind.String || items.Length == 0) throw new InvalidDataException("Spreaker returned an incomplete episode page.");
            endpoint = Continuation(next.GetString(), id, Id(items[^1], "episode_id"));
        }
        return new() { Id = id, PlaylistId = id, Extractor = "SpreakerShow", IsPlaylist = true, Title = Text(show, "title") ?? id, Description = Text(show, "description"),
            Uploader = Text(show, "author_name") ?? Text(Object(show, "author"), "fullname"), ThumbnailUrl = Poster(show), SourcePageUrl = showLink, Entries = entries };
    }
    private static PageMetadata Episode(JsonElement episode, string expected)
    {
        var id = Id(episode, "episode_id"); if (id != expected) throw new InvalidDataException("Spreaker returned another requested episode.");
        Access(episode); if (Text(episode, "type") != "RECORDED") throw new NotSupportedException("Spreaker live draft and externally hosted episodes need additional support.");
        if (!episode.TryGetProperty("download_enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True) throw new NotSupportedException("This Spreaker episode does not expose an enabled public download.");
        if (Text(episode, "encoding_status") is { } status && status != "READY") throw new NotSupportedException("Spreaker has not completed this episode's encoding.");
        var showId = Id(episode, "show_id"); var authorId = Id(episode, "author_id"); var show = Object(episode, "show"); var author = Object(episode, "author");
        if (Id(show, "show_id") != showId || Id(show, "author_id") != authorId || Id(author, "user_id") != authorId) throw new InvalidDataException("Spreaker episode show/author identities disagree.");
        Access(show); var duration = DurationSeconds(episode); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Spreaker omitted a supported full episode duration.");
        var source = PublicUrl(Text(episode, "download_url"));
        if (source.Host != "api.spreaker.com" || source.AbsolutePath != "/v2/episodes/" + id + "/download.mp3") throw new NotSupportedException("This Spreaker download layout needs additional support.");
        return new() { Id = id, Extractor = "Spreaker", ArchiveIdentity = new("Spreaker", id), Title = Text(episode, "title") ?? id, Description = Text(episode, "description"),
            Uploader = Text(author, "fullname"), DurationSeconds = duration, ThumbnailUrl = Poster(episode), SourcePageUrl = Link(Text(episode, "site_url"), id, false),
            Media = [new() { Url = source, MimeType = "audio/mpeg", Kind = MediaKind.Progressive, Source = "spreaker", Id = "download", Extension = "mp3", IsCompleteMedia = true, AudioCodec = "mp3", HasAudio = true, HasVideo = false,
                VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static string Id(JsonElement element, string name)
    { var value = Long(element, name)?.ToString(CultureInfo.InvariantCulture); return value is not null && SpreakerAddress.Numeric(value) ? value : throw new InvalidDataException("Spreaker omitted a valid stable identity."); }
    private static double? DurationSeconds(JsonElement element) => Number(element, "duration") is { } milliseconds ? milliseconds / 1000 : null;
    private static bool ExplicitFalse(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.False;
    private static bool Public(JsonElement element)
    {
        foreach (var name in new[] { "visibility", "hidden" }) if (element.TryGetProperty(name, out var value) && (name == "hidden" ? value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) : value.ValueKind != JsonValueKind.String))
            throw new InvalidDataException("Spreaker returned a malformed access state.");
        return (Text(element, "visibility") is null or "PUBLIC") && !True(element, "hidden") && Text(element, "site_limited_key") is not { Length: > 0 };
    }
    private static void Access(JsonElement element)
    {
        if (!Public(element) || Text(element, "site_limited_key") is { Length: > 0 }) throw new NotSupportedException("Spreaker private and limited access episodes need additional support.");
    }
    private static Uri PublicUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Spreaker returned an invalid public URL.");
    private static Uri Link(string? raw, string expected, bool show)
    {
        var uri = PublicUrl(raw); var address = SpreakerAddress.Parse(uri);
        if (address.IsShow != show || (SpreakerAddress.Numeric(address.Identity) ? address.Identity != expected : !show)) throw new InvalidDataException("Spreaker's public link conflicts with its identity.");
        return uri;
    }
    private static string? Poster(JsonElement item)
    {
        var raw = Text(item, "image_original_url") ?? Text(item, "image_url"); if (raw is null) return null; var uri = PublicUrl(raw);
        if (uri.Host is not ("d3wo5wojvuv7l.cloudfront.net" or "d1bm3dmew779uf.cloudfront.net" or "d1botjg6upurv.cloudfront.net" or "d16p0vxkuq1hni.cloudfront.net" or "images.spreaker.com")) throw new NotSupportedException("Spreaker returned an unrecognized artwork origin.");
        return uri.AbsoluteUri;
    }
    private static Uri Continuation(string? raw, string show, string last)
    {
        var uri = PublicUrl(raw); if (uri.Host != "api.spreaker.com" || uri.AbsolutePath != "/v2/shows/" + show + "/episodes") throw new InvalidDataException("Spreaker continued another show or endpoint.");
        Dictionary<string, string> query; try { query = SpreakerAddress.Query(uri); } catch (ArgumentException) { throw new InvalidDataException("Spreaker repeated continuation parameters."); }
        if (query.Count != 4 || query.GetValueOrDefault("filter") != "listenable" || query.GetValueOrDefault("sorting") != "newest" || query.GetValueOrDefault("limit") != "100" || query.GetValueOrDefault("last_id") != last)
            throw new InvalidDataException("Spreaker changed the public episode continuation scope.");
        return uri;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Spreaker redirected a metadata identity.");
        JsonDocument document; try { document = JsonDocument.Parse(response.Text, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Spreaker returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Spreaker returned excessive metadata.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Spreaker returned duplicate metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
