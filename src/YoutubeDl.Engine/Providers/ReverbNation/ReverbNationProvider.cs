using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.ReverbNation;

public sealed partial class ReverbNationProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("reverbnation", "ReverbNation public songs and counted artist collections", 200);
    public bool CanHandle(Uri url) => ReverbNationAddress.Matches(url);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = ReverbNationAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        if (address.WidgetArtistId is not null) address = await WidgetAsync(address, anonymous, ct).ConfigureAwait(false);
        var data = await PageAsync(address, anonymous, ct).ConfigureAwait(false); var artist = Object(data, "ARTIST"); var owner = ValidateArtist(artist, address);
        if (address.SongId is null) return await ArtistAsync(data, address, owner, request, anonymous, ct).ConfigureAwait(false);
        var song = Object(data, "PRIMARY_SONG"); ValidateSong(song, address, owner); return Facts(song, artist, address);
    }
    private static async Task<JsonElement> PageAsync(ReverbNationAddress address, MediaHttp http, CancellationToken ct)
    {
        var page = await http.GetDocumentAsync(address.Web, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var final = ReverbNationAddress.Parse(page.Url); if (final.Artist != address.Artist || final.SongId != address.SongId) throw new InvalidDataException("ReverbNation redirected to another requested identity.");
        return ParseBootstrap(page.Text, address.SongId is null ? "modules/artist_profile" : "modules/song_page", "config", ct);
    }
    private static JsonElement ParseBootstrap(string html, string module, string variable, CancellationToken ct)
    {
        var clean = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        var scripts = Regex.Matches(clean, @"<script\b[^>]*>(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))
            .Where(match => module.Length == 0 ? match.Groups["body"].Value.Contains("var configuration", StringComparison.Ordinal) : match.Groups["body"].Value.Contains(module, StringComparison.Ordinal)).ToArray();
        if (scripts.Length != 1) throw new InvalidDataException("ReverbNation did not expose one bounded public metadata bootstrap.");
        var script = scripts[0].Groups["body"].Value; var markers = Regex.Matches(script, @"\bvar\s+" + Regex.Escape(variable) + @"\s*=\s*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (markers.Count != 1) throw new InvalidDataException("ReverbNation's public metadata assignment is ambiguous.");
        var value = JavaScriptDataLiteral.ObjectArgument(script, markers[0].Index + markers[0].Length, out var end, ct);
        var trailing = script[end..].TrimStart(); if (trailing.Length == 0 || trailing[0] != ';') throw new InvalidDataException("ReverbNation appended an executable metadata expression.");
        using var document = JsonDocument.Parse(value.ToJsonString(), new JsonDocumentOptions { MaxDepth = 64 }); return document.RootElement.Clone();
    }
    private static async Task<ReverbNationAddress> WidgetAsync(ReverbNationAddress address, MediaHttp http, CancellationToken ct)
    {
        var page = await http.GetDocumentAsync(address.Web, 2 * 1024 * 1024, ct).ConfigureAwait(false);
        if (ReverbNationAddress.Parse(page.Url).WidgetArtistId != address.WidgetArtistId) throw new InvalidDataException("ReverbNation redirected another artist player.");
        var data = ParseBootstrap(page.Text, "", "configuration", ct); var playlist = Array(data, "PLAYLIST").ToArray();
        if (playlist.Length != 1 || Id(playlist[0]) != address.WidgetSongId || Id(Object(playlist[0], "artist")) != address.WidgetArtistId) throw new InvalidDataException("ReverbNation's player changed its selected recording.");
        var owner = ReverbNationAddress.Parse(new Uri(new Uri("https://legacy.reverbnation.com"), Text(Object(playlist[0], "artist"), "homepage") ?? throw new InvalidDataException("ReverbNation's player omitted its artist homepage.")));
        if (owner.SongId is not null || owner.WidgetArtistId is not null) throw new InvalidDataException("ReverbNation's player did not identify a public artist.");
        return new(owner.Artist, address.WidgetSongId);
    }
    private static string ValidateArtist(JsonElement artist, ReverbNationAddress address)
    {
        var id = Id(artist); if (Text(artist, "homepage") != address.Artist || Text(artist, "type") is { } type && type != "artist") throw new InvalidDataException("ReverbNation returned another artist homepage."); return id;
    }
    private static void ValidateSong(JsonElement song, ReverbNationAddress address, string owner, bool requireDelivery = true)
    {
        var canonical = ReverbNationAddress.Parse(Url(Text(song, "homepage_url")));
        if (Id(song) != address.SongId || Id(Object(song, "artist")) != owner || canonical.Artist != address.Artist || canonical.SongId != address.SongId)
            throw new InvalidDataException("ReverbNation returned another requested song or owner.");
        if (!Flag(song, "public", true) || Flag(song, "streaming_restricted", true) || !StreamingAccess(song))
            throw new NotSupportedException("This ReverbNation recording does not expose public streaming access.");
        if (Number(song, "duration") is not > 0 or > 604800) throw new InvalidDataException("ReverbNation omitted a supported complete recording duration.");
        if (requireDelivery) _ = AudioUrl(Text(song, "url"), Id(song));
    }
    private static bool StreamingAccess(JsonElement song) => Text(song, "access") is "streaming_only" or "free_download" or "streaming_and_download" or "exclusive_download";
    private static PageMetadata Facts(JsonElement song, JsonElement artist, ReverbNationAddress address)
    {
        var id = Id(song); var owner = Id(artist); var duration = Number(song, "duration"); var media = AudioUrl(Text(song, "url"), id); var picture = Text(song, "image") ?? Text(song, "thumbnail"); var photo = Long(song, "photo_id");
        if (picture is not null) { var cover = Url(picture); if (cover.Host != "d3e6ckxkrs5ntg.cloudfront.net" || photo is not > 0 || !cover.AbsolutePath.StartsWith("/photos/images/" + photo + "/", StringComparison.Ordinal)) throw new InvalidDataException("ReverbNation's artwork does not match the published photo."); }
        return new() { Id = id, Extractor = "reverbnation", ArchiveIdentity = new("reverbnation", id), Title = Text(song, "name")?.Trim() ?? id, Uploader = Text(artist, "name"), SourcePageUrl = address.Web,
            DurationSeconds = duration, ThumbnailUrl = picture, AnonymousPlayback = true, ViewCount = Long(song, "plays") is >= 0 and var plays ? plays : null,
            MetadataFields = new Dictionary<string, object?> { ["uploader_id"] = owner, ["photo_id"] = photo, ["protected"] = Flag(song, "protected") },
            Media = [new() { Id = "public-mp3", Url = media, Kind = MediaKind.Progressive, Source = "reverbnation", Extension = "mp3", MimeType = "audio/mpeg", HasAudio = true, HasVideo = false,
                IsCompleteMedia = true, VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static Uri AudioUrl(string? raw, string id)
    {
        var url = Url(raw); if (url.Scheme != "https" || url.Host != "dnma3z6c8ojev.cloudfront.net" || !url.AbsolutePath.StartsWith("/songs/mp3_files/" + id + "/original/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ReverbNation's supplied audio does not match its recording identity."); return url;
    }
    private static string Id(JsonElement node, string name = "id") => Long(node, name) is > 0 and < 1000000000000000 ? Long(node, name)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new InvalidDataException("ReverbNation returned an invalid numeric identity.");
    private static bool Flag(JsonElement node, string name, bool required = false)
    {
        if (!node.TryGetProperty(name, out var value)) return required ? throw new InvalidDataException("ReverbNation omitted a public access state.") : false;
        return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False or JsonValueKind.Null => false, _ => throw new InvalidDataException("ReverbNation returned an invalid access state.") };
    }
    private static Uri Url(string? raw) => raw is not null && raw.Length <= 32768 && Uri.TryCreate(raw.StartsWith("//", StringComparison.Ordinal) ? "https:" + raw : raw, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 ? url : throw new InvalidDataException("ReverbNation returned an invalid resource URL.");
    private static async Task<JsonElement> ApiAsync(Uri url, MediaHttp http, CancellationToken ct)
    {
        var data = await http.GetDocumentAsync(url, 8 * 1024 * 1024, ct, new Dictionary<string, string> { ["Accept"] = "application/json" }).ConfigureAwait(false);
        if (data.Url != url) throw new InvalidDataException("ReverbNation redirected public song inventory metadata."); using var document = JsonDocument.Parse(data.Text, new JsonDocumentOptions { MaxDepth = 64 }); return document.RootElement.Clone();
    }
}
