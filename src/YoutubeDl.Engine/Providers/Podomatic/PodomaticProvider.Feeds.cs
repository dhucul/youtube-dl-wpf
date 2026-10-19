using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.Feeds;

namespace YoutubeDl.Engine.Providers.Podomatic;

public sealed partial class PodomaticProvider
{
    private static async Task<PageMetadata> ReadFeedAsync(PodomaticAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var source = new Uri("https://" + address.Name + ".podomatic.com/rss2.xml");
        var document = await http.GetDocumentAsync(source, 5 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url.Scheme != "https" || !document.Url.IsDefaultPort || document.Url.UserInfo.Length != 0 || document.Url.Fragment.Length != 0 || !MediaFeedParser.IsFeed(document.Text, document.ContentType))
            throw new InvalidDataException("The publisher's Podomatic feed did not resolve to a supported public HTTPS feed.");
        var page = await MediaFeedReader.ReadAsync(document, request, http, ct).ConfigureAwait(false);
        var entries = new List<NativePlaylistEntry>();
        foreach (var entry in page.Entries)
        {
            var media = entry.ResolvedMedia;
            if (media is not null)
            {
                var candidates = media.Media.Select(candidate => VerifiedFeedAudio(candidate, media.DurationSeconds)).ToArray();
                media = media with { Media = candidates, AnonymousPlayback = true };
            }
            entries.Add(entry with { ResolvedMedia = media });
        }
        var moved = document.Url.Host != source.Host;
        return page with { Entries = entries, Extractor = "podomatic:feed", AnonymousPlayback = true,
            Warnings = moved ? page.Warnings.Append("The publisher moved this feed to " + document.Url.Host + "; these entries use the current feed's media and identifiers.").ToArray() : page.Warnings };
    }
    private static MediaCandidate VerifiedFeedAudio(MediaCandidate candidate, double? duration)
    {
        if (candidate.Kind != MediaKind.Progressive || !candidate.IsAudioOnly || candidate.Extension != "mp3") return candidate;
        return new() { Url = candidate.Url, Id = candidate.Id, Kind = candidate.Kind, Source = candidate.Source, Extension = candidate.Extension, MimeType = candidate.MimeType,
            HasAudio = true, HasVideo = false, AudioCodec = "mp3", IsCompleteMedia = true, FileSize = candidate.FileSize, Bandwidth = candidate.Bandwidth, FramesPerSecond = candidate.FramesPerSecond,
            Width = candidate.Width, Height = candidate.Height, AudioLanguage = candidate.AudioLanguage, IsDefaultAudio = candidate.IsDefaultAudio, IsFallback = candidate.IsFallback, Label = candidate.Label,
            VerificationDemuxer = "mp3", VerificationMinimumDurationSeconds = duration is > 0 ? duration : null };
    }
}
