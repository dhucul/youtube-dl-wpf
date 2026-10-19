using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Audius;

public sealed partial class AudiusProvider : IMediaProvider
{
    private const string Api = "https://api.audius.co/v1/";
    public MediaProviderInfo Info { get; } = new("audius", "Audius public tracks artists playlists and albums", 200);
    public bool CanHandle(Uri uri) => AudiusAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = AudiusAddress.Parse(request.Url); return address.Kind == "track" && address.Id is { } id ? new("audius", id) : null; }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = AudiusAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.Kind == "artist") return await ArtistAsync(address, request, anonymous, ct).ConfigureAwait(false);
        var endpoint = address.Id is { } id ? new Uri(Api + (address.Kind == "track" ? "tracks/" : "playlists/") + id) : new Uri(Api + "resolve?url=" + Uri.EscapeDataString(address.Web!.AbsoluteUri));
        using var document = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var data = Data(document.RootElement);
        if (data.ValueKind == JsonValueKind.Array) { if (data.GetArrayLength() != 1) throw new InvalidDataException("Audius returned an ambiguous requested resource."); data = data[0]; }
        var resourceId = Id(data);
        if (address.Id is { } expected && resourceId != expected || address.Web is not null && Permalink(data) != address.Web || address.Handle is { } handle && !string.Equals(Text(Object(data, "user"), "handle"), handle, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Audius returned another requested resource or artist.");
        return address.Kind == "track" ? Track(data) : await PlaylistAsync(data, request, anonymous, ct).ConfigureAwait(false);
    }
    private static PageMetadata Track(JsonElement track)
    {
        var id = Id(track); var user = Object(track, "user"); var owner = Id(user); var handle = Text(user, "handle");
        if (Text(track, "user_id") != owner || handle is null || !AudiusAddress.HandleValue(handle) || !Permalink(track).AbsolutePath.StartsWith("/" + handle + "/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Audius track and artist identities disagree.");
        ValidateUser(user); if (!Available(track)) throw new NotSupportedException("This Audius track is unavailable scheduled deleted gated or needs additional account/API-key support.");
        var duration = Number(track, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Audius omitted a supported complete track duration.");
        var source = Stream(track);
        return new() { Id = id, Extractor = "audius", ArchiveIdentity = new("audius", id), Title = Text(track, "title") ?? id, Description = Text(track, "description"), Uploader = Text(user, "name") ?? handle,
            DurationSeconds = duration, ThumbnailUrl = Artwork(track, "artwork"), SourcePageUrl = Permalink(track), AnonymousPlayback = true,
            Media = [new() { Id = "stream", Url = source, Kind = MediaKind.Progressive, Source = "audius", Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false, AudioCodec = "mp3", IsCompleteMedia = true,
                VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static bool Available(JsonElement track) => Flag(track, "is_available") && !Flag(track, "is_delete") && !Flag(track, "is_scheduled_release") && Flag(track, "is_streamable") && !Flag(track, "is_stream_gated") && Flag(Object(track, "access"), "stream") && Array(track, "allowed_api_keys").Length == 0;
    private static void ValidateUser(JsonElement user) { _ = Id(user); if (!Flag(user, "is_available") || Flag(user, "is_deactivated")) throw new NotSupportedException("This Audius artist is unavailable or deactivated."); }
    private static bool Flag(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.ValueKind == JsonValueKind.True : throw new InvalidDataException("Audius omitted an explicit supported availability state.");
    private static string Id(JsonElement value) { var id = Text(value, "id"); return id is not null && AudiusAddress.Identifier(id) ? id : throw new InvalidDataException("Audius omitted a valid stable encoded identifier."); }
    private static JsonElement Data(JsonElement root) => root.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? data : throw new InvalidDataException("Audius omitted its public resource data.");
    private static Uri Permalink(JsonElement resource) { var raw = Text(resource, "permalink"); if (raw is null || !raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("Audius omitted a bound public permalink."); var uri = new Uri("https://audius.co" + raw); _ = AudiusAddress.Parse(uri); return uri; }
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Audius returned an unsupported public HTTPS resource.");
    private static string? Artwork(JsonElement resource, string field)
    { var art = Object(resource, field); var raw = Text(art, "1000x1000") ?? Text(art, "480x480") ?? Text(art, "150x150"); return raw is null ? null : PublicUrl(raw).AbsoluteUri; }
    private static Uri Stream(JsonElement track)
    {
        var id = Id(track); var cid = Text(track, "track_cid"); if (cid is null || cid.Length is < 16 or > 256 || !cid.All(value => char.IsAsciiLetterOrDigit(value) || value is '_' or '-')) throw new InvalidDataException("Audius omitted a supported track content identity.");
        var raw = Text(Object(track, "stream"), "url"); if (raw is null) return new(Api + "tracks/" + id + "/stream");
        var source = PublicUrl(raw); if (source.AbsolutePath != "/tracks/cidstream/" + cid) throw new InvalidDataException("Audius returned another or preview track content URL.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in source.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Split('=', 2); if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) throw new InvalidDataException("Audius repeated signed stream query fields."); }
        if (query.Count != 1 || !query.TryGetValue("signature", out var signed)) throw new NotSupportedException("This Audius stream authorization layout needs additional native support.");
        using var signature = Parse(signed); var payload = Text(signature.RootElement, "data") ?? throw new InvalidDataException("Audius omitted signed public stream data."); using var proof = Parse(payload);
        if (Text(proof.RootElement, "cid") != cid || Long(proof.RootElement, "trackId") != Long(track, "track_id") || Long(track, "track_id") is not > 0 || Long(proof.RootElement, "userId") != 0 || Long(proof.RootElement, "timestamp") is not > 0 || Text(signature.RootElement, "signature") is not { Length: > 0 }) throw new InvalidDataException("Audius's stream descriptor does not bind the requested anonymous track.");
        return source;
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    {
        var response = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false); var document = Parse(response.Text);
        try
        {
            if (response.Url != endpoint)
            {
                var data = Data(document.RootElement); if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() == 1) data = data[0];
                var id = Id(data);
                var address = AudiusAddress.Parse(Permalink(data)); var parts = Permalink(data).AbsolutePath.Trim('/').Split('/');
                var canonical = new Uri(Api + (address.Kind == "track" ? "tracks" : "playlists") + "/by_permalink/" + parts[0] + "/" + parts[^1]);
                if (endpoint.AbsolutePath != "/v1/resolve" || response.Url.Scheme != "https" || response.Url.Host != "api.audius.co" || !response.Url.IsDefaultPort || response.Url.UserInfo.Length != 0 || response.Url.Query.Length != 0 || response.Url.Fragment.Length != 0 || response.Url.AbsolutePath != "/v1/tracks/" + id && response.Url.AbsolutePath != "/v1/playlists/" + id && response.Url != canonical)
                    throw new InvalidDataException("Audius redirected its public metadata identity.");
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
    private static JsonDocument Parse(string raw)
    {
        JsonDocument document; try { document = JsonDocument.Parse(raw, new() { MaxDepth = 64 }); } catch (JsonException) { throw new InvalidDataException("Audius returned malformed public metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(document.RootElement); var count = 0;
            while (stack.TryPop(out var item))
            {
                if (++count > 200000) throw new InvalidDataException("Audius returned excessive metadata fields.");
                if (item.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Audius returned repeated metadata fields."); stack.Push(field.Value); } }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) stack.Push(child);
            }
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
