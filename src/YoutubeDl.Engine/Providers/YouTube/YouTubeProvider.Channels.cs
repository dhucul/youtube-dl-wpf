using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.YouTube;

public sealed partial class YouTubeProvider
{
    private static async Task<PageMetadata> ExtractChannelAsync(YouTubeChannelAddress address, NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var account=http.SessionState("youtube:account",()=>new YouTubeAccountSession(http,request.Authentication.YouTubeAccountIndex));
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        var first = await ReadPage(address).ConfigureAwait(false);
        var identity = first.Page.Id;
        var entries = new List<NativePlaylistEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (address.Tab is not null) await ReadFeed(first.Page, first.Version).ConfigureAwait(false);
        else
        {
            if (first.Page.Tabs.Count == 0) throw new NotSupportedException("YouTube did not expose supported upload tabs for this channel.");
            foreach (var kind in new[] { "videos", "shorts", "streams" })
            {
                if (!first.Page.Tabs.TryGetValue(kind, out var url)) continue;
                if (first.Page.SelectedTab == kind) await ReadFeed(first.Page, first.Version).ConfigureAwait(false);
                else
                {
                    if (!YouTubeChannelAddress.TryParse(url, out var tab)) throw new InvalidDataException("The channel tab address is invalid.");
                    var next = await ReadPage(tab!).ConfigureAwait(false);
                    if (next.Page.Id != identity) throw new InvalidDataException("The channel identity changed between tabs.");
                    await ReadFeed(next.Page, next.Version).ConfigureAwait(false);
                }
            }
        }
        var playlistId = identity + (address.Tab is null ? "" : ":" + address.Tab);
        return new() { Id = playlistId, PlaylistId = playlistId, IsPlaylist = true, Extractor = "youtube", Title = first.Page.Title + (address.Tab is null ? "" : " - " + address.Tab),
            Uploader = first.Page.Title, Description = first.Page.Description, ThumbnailUrl = first.Page.Thumbnail, SourcePageUrl = address.Url, Entries = entries };

        async Task<(YouTubeChannelPage Page, string Version)> ReadPage(YouTubeChannelAddress target)
        {
            var url=account.Select(target.Url);var response = await account.ReadPageAsync(url,ct).ConfigureAwait(false);
            if (!YouTubeChannelAddress.TryParse(YouTubeAccountSession.WithoutSelection(response.Url), out var final) || final!.Tab != target.Tab)
                throw new InvalidDataException("YouTube redirected the channel to a different page type or tab.");
            account.Observe(response.Text,response.Url,ct);
            var page = YouTubeChannelParser.ParsePage(YouTubePageJson.ReadInitialData(response.Text), target, request.MaximumPlaylistEntries, ct);
            var match = Regex.Match(response.Text, @"""INNERTUBE_CLIENT_VERSION""\s*:\s*""(?<value>[^""]+)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return (page, match.Groups["value"].Value);
        }

        async Task ReadFeed(YouTubeChannelPage page, string version)
        {
            var batch = page.Batch ?? throw new InvalidDataException("The requested channel tab has no supported feed.");
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            var tabSeen = new HashSet<string>(StringComparer.Ordinal);
            for (var count = 0; ; count++)
            {
                ct.ThrowIfCancellationRequested();
                var progress = 0;
                foreach (var entry in batch.Entries)
                {
                    if (entry.Id is { } id)
                    {
                        if (!tabSeen.Add(id)) continue;
                        progress++;
                        if (!seen.Add(id)) continue;
                    }
                    else progress++;
                    if (entries.Count >= request.MaximumPlaylistEntries) throw new InvalidDataException("The channel exceeds the configured item limit; it was not truncated silently.");
                    entries.Add(entry with { Position = entries.Count + 1 });
                }
                if (batch.Continuation is not { } continuation) break;
                if (progress == 0 || !tokens.Add(continuation) || count >= 1000)
                    throw new InvalidDataException("The channel feed repeated a continuation or stopped making progress.");
                if (string.IsNullOrWhiteSpace(version) || version.Length > 128) throw new InvalidDataException("The channel has no supported client version for pagination.");
                var body = new { context = new { client = new { clientName = "WEB", clientVersion = version } }, continuation };
                var json = await account.PostAsync("browse",body,"1",version,8*1024*1024,ct).ConfigureAwait(false);
                batch = YouTubeChannelParser.ParseContinuation(json, batch.Target!, request.MaximumPlaylistEntries, ct);
            }
        }
    }
}
