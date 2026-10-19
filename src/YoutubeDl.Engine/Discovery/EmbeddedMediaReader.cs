using System.Net;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Discovery;

public static class EmbeddedMediaReader
{
    public static IReadOnlyList<Uri> Read(string html, Uri page)
    {
        var found = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(html, @"<(?:a|iframe|embed|script|div|blockquote|wistia-player|wistia-playlist|video-js|video|img|stream)\b(?<attributes>[^>]{0,8192})>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Regex.Matches(tag.Groups["attributes"].Value, @"(?<name>[A-Za-z0-9_:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["double"].Success ? attribute.Groups["double"].Value : attribute.Groups["single"].Value));
            if (tag.Value.StartsWith("<a", StringComparison.OrdinalIgnoreCase) && attributes.GetValueOrDefault("data-pin-do") == "embedPin" &&
                attributes.TryGetValue("href", out var pinLink) && Uri.TryCreate(page, pinLink, out var pinUrl) && Providers.Pinterest.PinterestAddress.Matches(pinUrl))
            {
                try { Add(Providers.Pinterest.PinterestAddress.Parse(pinUrl).Page); }
                catch (NotSupportedException) { }
            }
            if (tag.Value.StartsWith("<a", StringComparison.OrdinalIgnoreCase) && attributes.GetValueOrDefault("data-pin-do") == "embedBoard" &&
                attributes.TryGetValue("href", out var boardLink) && Uri.TryCreate(page, boardLink, out var boardUrl))
            {
                try { if (Providers.Pinterest.PinterestBoardAddress.TryParse(boardUrl, out var board)) Add(board!.Page); }
                catch (NotSupportedException) { }
            }
            if (attributes.TryGetValue("src", out var source) && Uri.TryCreate(page, source, out var uri))
            {
                if (tag.Value.StartsWith("<iframe", StringComparison.OrdinalIgnoreCase) || tag.Value.StartsWith("<embed", StringComparison.OrdinalIgnoreCase)) Add(uri);
                else if (uri.Host is "cdn.jwplayer.com" or "content.jwplatform.com" or "fast.wistia.com" or "fast.wistia.net") Add(uri);
                else if (Providers.Kaltura.KalturaAddress.IsPlayer(uri))
                {
                    try { Providers.Kaltura.KalturaAddress.Parse(uri); Add(uri); }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
                }
                else if (uri.Host.EndsWith(".dailymotion.com", StringComparison.OrdinalIgnoreCase))
                {
                    if (attributes.TryGetValue("data-video", out var id) && Regex.IsMatch(id, @"\A[A-Za-z0-9]{1,40}\z")) Add(new Uri("https://www.dailymotion.com/video/" + id));
                    else if (attributes.TryGetValue("data-playlist", out var list) && Regex.IsMatch(list, @"\A[A-Za-z0-9]{1,40}\z")) Add(new Uri("https://www.dailymotion.com/playlist/" + list));
                }
            }
            if (tag.Value.StartsWith("<wistia-player", StringComparison.OrdinalIgnoreCase) && attributes.TryGetValue("media-id", out var mediaId) && IsWistiaId(mediaId)) Add(new Uri("https://fast.wistia.com/embed/iframe/" + mediaId));
            if (tag.Value.StartsWith("<wistia-playlist",StringComparison.OrdinalIgnoreCase)&&attributes.TryGetValue("channel-id",out var channelId)&&IsWistiaId(channelId))Add(new("https://fast.wistia.com/embed/channel/"+channelId+"?mode=playlist"));
            if (Providers.Vimeo.VimeoAddress.ReadEmbed(attributes) is { } vimeo) Add(vimeo);
            if (Providers.CloudflareStream.CloudflareStreamProvider.ReadEmbed(tag.Value, attributes) is { } cloudflare) Add(cloudflare);
            if (attributes.TryGetValue("class", out var classes))
            {
                if (classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("vidyard-player-embed", StringComparer.OrdinalIgnoreCase) &&
                    attributes.TryGetValue("data-uuid", out var vidyardId) && Providers.Vidyard.VidyardProvider.IsUuid(vidyardId))
                    Add(new Uri("https://play.vidyard.com/" + vidyardId + ".html" +
                        (attributes.TryGetValue("data-second", out var vidyardSecond) ? "?second=" + Uri.EscapeDataString(vidyardSecond) : "")));
                if (classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("tiktok-embed", StringComparer.OrdinalIgnoreCase) &&
                    attributes.TryGetValue("data-video-id", out var tiktokId) && Providers.TikTok.TikTokAddress.IsId(tiktokId))
                {
                    if (attributes.TryGetValue("cite", out var cite))
                    {
                        if (!Uri.TryCreate(page, cite, out var cited) || !Providers.TikTok.TikTokAddress.IsHost(cited) || Providers.TikTok.TikTokAddress.Parse(cited).Id != tiktokId)
                            throw new InvalidDataException("The TikTok embed has inconsistent video identities.");
                    }
                    Add(Providers.TikTok.TikTokAddress.Canonical(tiktokId));
                }
                var wistia = Regex.Match(classes, @"(?:\A|\s)wistia_async_(?<id>[a-z0-9]{10})(?:\s|\z)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (wistia.Success) Add(new Uri("https://fast.wistia.com/embed/"+(classes.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains("wistia_playlist",StringComparer.OrdinalIgnoreCase)?"playlists/":"iframe/") + wistia.Groups["id"].Value));
            }
            if (attributes.TryGetValue("data-account", out var account) && Regex.IsMatch(account, @"\A[0-9]{1,20}\z") &&
                attributes.TryGetValue("data-player", out var player) && Regex.IsMatch(player, @"\A[A-Za-z0-9_-]{1,80}\z"))
            {
                var embed = attributes.GetValueOrDefault("data-embed", "default");
                if (!Regex.IsMatch(embed, @"\A[A-Za-z0-9_-]{1,30}\z")) continue;
                var video = attributes.GetValueOrDefault("data-video-id"); var playlist = attributes.GetValueOrDefault("data-playlist-id");
                if (!string.IsNullOrEmpty(video) || !string.IsNullOrEmpty(playlist))
                    Add(new Uri("https://players.brightcove.net/" + account + "/" + player + "_" + embed + "/index.html?" +
                        (!string.IsNullOrEmpty(video) ? "videoId=" + Uri.EscapeDataString(video) : "playlistId=" + Uri.EscapeDataString(playlist!))));
            }
        }
        // Flat legacy widget declarations have literal entry/partner fields in the same call.
        // Nested or computed JavaScript configurations require a future script integration.
        foreach (Match call in Regex.Matches(html, @"\bkWidget\.embed\s*\(\s*\{(?<config>[^{}]{1,8192})\}\s*\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            if (ReadKalturaWidget(call.Groups["config"].Value) is { } widget) Add(widget);
        }
        foreach (var rumble in Providers.Rumble.RumblePage.EmbeddedUrls(html)) Add(rumble);
        foreach(Match call in Regex.Matches(html,@"\bWistia\.playlist\s*\(\s*['""](?<id>[a-z0-9]{10})['""]\s*[,)]",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))Add(new("https://fast.wistia.com/embed/playlists/"+call.Groups["id"].Value));
        foreach(Match setup in Regex.Matches(html,@"\bjwplayer\s*\([^)]{0,256}\)\s*\.setup\s*\(\s*\{(?<config>[^{}]{1,8192})\}\s*\)",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))
        {
            var match=Regex.Match(setup.Groups["config"].Value,@"(?:\A|,)\s*(?:playlist|""playlist""|'playlist')\s*:\s*(?:""(?<url>[^""\\]+)""|'(?<url>[^'\\]+)')\s*(?:,|\z)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(match.Success&&Uri.TryCreate(page,WebUtility.HtmlDecode(match.Groups["url"].Value),out var endpoint)&&new Providers.JwPlatform.JwPlatformProvider().CanHandle(endpoint))Add(endpoint);
        }
        return found.Values.ToArray();
        void Add(Uri uri)
        {
            if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) return;
            var normalized = new UriBuilder(uri) { Fragment = "" }.Uri;
            if (normalized == page) return;
            if (found.Count >= 64 && !found.ContainsKey(normalized.AbsoluteUri)) throw new InvalidDataException("The page contains too many embedded media addresses.");
            found.TryAdd(normalized.AbsoluteUri, normalized);
        }
    }
    private static bool IsWistiaId(string id) => Regex.IsMatch(id, @"\A[a-z0-9]{10}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static Uri? ReadKalturaWidget(string config)
    {
        config = config.Trim();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var property = new Regex(@"\G\s*(?:(?<key>[A-Za-z_$][A-Za-z0-9_$]*)|""(?<key>[^""\\]*)""|'(?<key>[^'\\]*)')\s*:\s*(?:""(?<value>[^""\\]*)""|'(?<value>[^'\\]*)'|(?<value>[0-9]+|true|false|null))\s*(?:,|\z)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        for (var position = 0; position < config.Length;)
        {
            var match = property.Match(config, position);
            if (!match.Success || match.Index != position || !fields.TryAdd(match.Groups["key"].Value, match.Groups["value"].Value)) return null;
            position += match.Length;
        }
        if (fields.ContainsKey("ks") || !fields.TryGetValue("wid", out var widget) || !Regex.IsMatch(widget, @"\A_[0-9]{1,10}\z") ||
            !fields.TryGetValue("entry_id", out var entry) || !Providers.Kaltura.KalturaAddress.IsEntryId(entry)) return null;
        return new Uri("https://cdnapisec.kaltura.com/p/" + widget[1..] + "/embedPlaykitJs?entry_id=" + entry);
    }
}
