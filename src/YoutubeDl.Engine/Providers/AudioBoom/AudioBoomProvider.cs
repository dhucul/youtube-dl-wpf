using System.Globalization;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Feeds;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.AudioBoom;

public sealed class AudioBoomProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("audioboom", "Audioboom public posts embeds and count-verified channel feeds", 200);
    public bool CanHandle(Uri uri) => AudioBoomAddress.Matches(uri);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) { var address = AudioBoomAddress.Parse(request.Url); return address.IsChannel ? null : new("audioboom", address.Identity); }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = AudioBoomAddress.Parse(request.Url); using var anonymous = http.CreateAnonymousSession();
        var document = await anonymous.GetDocumentAsync(address.IsChannel ? (address with { Embed = false }).Page : address.Page, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        var returned = AudioBoomAddress.Parse(document.Url);
        if (returned.IsChannel != address.IsChannel || !address.Alias && returned.Identity != address.Identity) throw new InvalidDataException("Audioboom redirected to another post or channel.");
        var html = AudioBoomHtml.Clean(document.Text); var canonical = AudioBoomHtml.Canonical(html); var identity = AudioBoomAddress.Parse(canonical);
        if (identity.IsChannel != address.IsChannel || !returned.Alias && identity.Identity != returned.Identity || identity.Alias || identity.Embed) throw new InvalidDataException("Audioboom's canonical identity disagrees with the requested resource.");
        var metadata = PageMetadataReader.Read(html, canonical); var id = identity.Identity;
        if (address.IsChannel)
        {
            var count = AudioBoomHtml.Count(html); if (count > Math.Min(10000, request.MaximumPlaylistEntries)) throw new NotSupportedException("The complete Audioboom channel exceeds the configured inventory limit.");
            var expected = new Uri("https://audioboom.com/channels/" + id + ".rss");
            var feeds = AudioBoomHtml.Tags(html, "link").Where(tag => tag.GetValueOrDefault("rel") == "alternate" && tag.GetValueOrDefault("type") == "application/rss+xml").Select(tag => tag.GetValueOrDefault("href")).Distinct(StringComparer.Ordinal).ToArray();
            if (feeds.Length != 1 || feeds[0] != expected.AbsoluteUri) throw new InvalidDataException("Audioboom's public feed does not match the channel identity.");
            var feed = await anonymous.GetDocumentAsync(expected, 8 * 1024 * 1024, ct).ConfigureAwait(false); if (feed.Url != expected) throw new InvalidDataException("Audioboom redirected to another channel feed.");
            var parsed = MediaFeedParser.Parse(feed.Text, expected, Math.Min(10000, request.MaximumPlaylistEntries));
            if (parsed.Next is not null || parsed.Metadata.Entries.Count != count) throw new InvalidDataException("Audioboom's public feed does not contain the declared complete channel inventory.");
            var entries = new List<NativePlaylistEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in parsed.Metadata.Entries)
            {
                if (entry.Url is null || !AudioBoomAddress.Matches(entry.Url)) throw new InvalidDataException("Audioboom omitted an identified public feed episode link.");
                var child = AudioBoomAddress.Parse(entry.Url); if (child.IsChannel || child.Embed || !seen.Add(child.Identity)) throw new InvalidDataException("Audioboom repeated or changed a channel episode identity.");
                entries.Add(new(entry.Position, child.Identity, entry.Title, entry.Available ? AudioBoomAddress.Episode(child.Identity) : null, entry.DurationSeconds, entry.ThumbnailUrl, entry.Available, ExpectedArchiveIdentity: new("audioboom", child.Identity)));
            }
            return metadata with { Id = id, PlaylistId = id, Extractor = "audioboom:channel", IsPlaylist = true, Entries = entries, SourcePageUrl = canonical };
        }
        using var player = AudioBoomHtml.Player(html, returned.Embed); var root = player.RootElement;
        if (returned.Embed && (!True(root, "isSingle") || True(root, "isChannel") || True(root, "isPlaylist"))) throw new NotSupportedException("This Audioboom player is a collection rather than the requested single episode.");
        var clips = Array(root, "clips"); if (clips.Length > 1000) throw new InvalidDataException("Audioboom returned excessive episode records.");
        var matches = clips.Where(clip => Long(clip, "clipId")?.ToString(CultureInfo.InvariantCulture) == id).ToArray(); if (matches.Length != 1) throw new InvalidDataException("Audioboom did not expose exactly one requested episode record."); var selected = matches[0];
        if (!selected.TryGetProperty("embedDisabled", out var disabled) || disabled.ValueKind != System.Text.Json.JsonValueKind.False) throw new NotSupportedException("Audioboom omitted a supported public episode playback state.");
        var staticLink = PublicUrl(Text(selected, "staticLink")); if (staticLink.Host != "audioboom.com" || AudioBoomAddress.Parse(staticLink) is not { IsChannel: false, Embed: false } link || link.Identity != id) throw new InvalidDataException("Audioboom's player link disagrees with its episode ID.");
        var duration = Number(selected, "duration"); if (duration is not > 0 || duration > 604800) throw new InvalidDataException("Audioboom omitted a supported full episode duration.");
        var source = PublicUrl(Text(selected, "clipURLPriorToLoading")); if (source.Host != "audioboom.com" || source.AbsolutePath != "/posts/" + id + ".mp3") throw new NotSupportedException("This Audioboom playback layout needs additional support.");
        var poster = metadata.ThumbnailUrl; if (poster is not null && (PublicUrl(poster).Host != "audioboom.com" || !PublicUrl(poster).AbsolutePath.StartsWith("/i/", StringComparison.Ordinal))) throw new NotSupportedException("Audioboom returned an unsupported artwork origin.");
        return metadata with { Id = id, Extractor = "audioboom", ArchiveIdentity = new("audioboom", id), Title = Text(selected, "title") ?? metadata.Title, Uploader = Text(selected, "author"),
            Description = Text(selected, "formattedDescription") ?? metadata.Description, DurationSeconds = duration, SourcePageUrl = canonical, AnonymousPlayback = true,
            Media = [new() { Id = "source", Url = source, MimeType = "audio/mpeg", Kind = MediaKind.Progressive, Extension = "mp3", Source = "audioboom", AudioCodec = "mp3", HasAudio = true, HasVideo = false, IsCompleteMedia = true,
                VerificationDemuxer = "mp3", VerificationDurationSeconds = duration }] };
    }
    private static Uri PublicUrl(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri : throw new InvalidDataException("Audioboom returned an unsupported public URL.");
}
