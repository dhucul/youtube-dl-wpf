using System.Xml;
using System.Xml.Linq;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine;

internal static class NativeDynamicDash
{
    internal static async Task<PageMetadata> FilterAsync(PageMetadata page, NativeDownloadRequest request, MediaHttp owner, CancellationToken ct)
    {
        if (request.AllowDynamicDash != false || !page.Media.Any(media => media.Kind == MediaKind.Dash)) return page;
        using var isolated = page.PublicServicePlayback ? owner.CreatePublicServiceSession(preserveForwardedFor: true) : page.PlaybackCookies is not null ? owner.CreateCookieSession(page.PlaybackCookies) : page.AnonymousPlayback ? owner.CreateAnonymousSession() : null;
        var source = isolated ?? owner;
        using var headers = page.PlaybackHeaders.Count == 0 ? null : source.WithHeaders(page.PlaybackHeaders); source = headers ?? source;
        using var queries = page.PlaybackQueries.Count == 0 ? null : source.WithQueries(page.PlaybackQueries); source = queries ?? source;
        using var authorized = page.PlaybackAuthorization is null ? null : source.WithAuthorization(page.PlaybackAuthorization); source = authorized ?? source;
        var formats = new List<MediaCandidate>(); var removed = 0;
        foreach (var media in page.Media)
        {
            ct.ThrowIfCancellationRequested();
            if (media.Kind != MediaKind.Dash) { formats.Add(media); continue; }
            var document = await source.GetDocumentAsync(media.Url, 8 * 1024 * 1024, ct).ConfigureAwait(false);
            using var text = new StringReader(document.Text); using var reader = XmlReader.Create(text, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != XName.Get("MPD", "urn:mpeg:dash:schema:mpd:2011") || (string?)root.Attribute("type") is { } type && type is not ("static" or "dynamic")) throw new InvalidDataException("The offered DASH document has an invalid presentation type.");
            if ((string?)root.Attribute("type") == "dynamic") removed++; else formats.Add(media);
        }
        return removed == 0 ? page : page with { Media = formats, Warnings = page.Warnings.Concat(new[] { "Dynamic DASH formats were excluded by the selected option." }).ToArray() };
    }
}
