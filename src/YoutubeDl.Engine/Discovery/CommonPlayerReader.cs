using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Providers;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Discovery;

/// <summary>Reads explicitly selected common player data without executing page scripts or collecting ad URLs.</summary>
internal static class CommonPlayerReader
{
    internal static PageMetadata? Read(string html, Uri page, PageMetadata metadata, CancellationToken ct)
    {
        var configs = new List<JsonElement>(); var active = Regex.IsMatch(html, @"\b(?:jwplayer\s*\([^)]{0,256}\)\s*\.setup\s*\(|videojs\s*\([^,)]{1,256},|flashvars[A-Za-z0-9_]*\s*=)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        foreach (Match call in Regex.Matches(html, @"\b(?:jwplayer\s*\([^)]{0,256}\)\s*\.setup\s*\(|videojs\s*\([^,)]{1,256},|(?:var\s+)?flashvars[A-Za-z0-9_]*\s*=|window\.initials\s*=)\s*(?<object>\{)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            ct.ThrowIfCancellationRequested(); var initials = call.Value.StartsWith("window.initials", StringComparison.Ordinal);
            if (!initials) active = true;
            var literal = JavaScriptDataLiteral.ObjectArgument(html, call.Groups["object"].Index, out var end, ct, allowIdentifierKeys: true);
            if (!initials && html.AsSpan(end).TrimStart() is var tail && !tail.IsEmpty && tail[0] is not (')' or ';' or '<')) throw new InvalidDataException("The selected player configuration contains a computed value.");
            using var json = JsonDocument.Parse(literal.ToJsonString()); var config = json.RootElement.Clone();
            if (initials) { config = Object(config, "xplayerSettings"); if (config.ValueKind != JsonValueKind.Object) continue; active = true; }
            if (configs.Count >= 8) throw new InvalidDataException("The page contains too many selected player configurations."); configs.Add(config);
        }
        var calls = Regex.Matches(html, @"\bhtml5player\.setVideo(?<quality>UrlHigh|UrlLow|HLS)\s*\(\s*(?<literal>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')\s*\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        active |= Regex.IsMatch(html, @"\bhtml5player\.setVideo(?:UrlHigh|UrlLow|HLS)\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (!active) return null;
        if (configs.Count > 1 || configs.Count > 0 && calls.Count > 0) throw new NotSupportedException("This page contains multiple players. Use a link to the selected video.");
        var media = new List<MediaCandidate>(); var seen = new HashSet<string>(StringComparer.Ordinal); var configRoot = configs.FirstOrDefault();
        if (configs.Count > 0)
        {
            if (PublicProviderApi.Drm(configRoot) || PublicProviderApi.Present(configRoot, "drmConfig")) return Result() with { DrmRefused = true, Warnings = ["The selected player does not offer clear downloadable media."] };
            if (True(configRoot, "requiresSubscription") || True(configRoot, "isPreview") || True(configRoot, "is_preview") || True(configRoot, "isTrailer")) return Result() with { Warnings = ["The selected player offers a restricted or preview source."] };
            var root = configRoot;
            if (root.TryGetProperty("playlist", out var playlist))
            {
                if (playlist.ValueKind == JsonValueKind.String && Uri.TryCreate(page, playlist.GetString(), out var hosted) && new Providers.JwPlatform.JwPlatformProvider().CanHandle(hosted)) return Result() with { EmbeddedPages = [hosted] };
                if (playlist.ValueKind != JsonValueKind.Array || playlist.GetArrayLength() != 1 || playlist[0].ValueKind != JsonValueKind.Object) throw new NotSupportedException("This player playlist needs its own collection integration."); root = playlist[0];
            }
            Add(Text(root, "file"), Text(root, "label"), 0);
            Sources(root, "sources"); Sources(root, "mediaDefinitions");
            var sourceGroups = Object(root, "sources");
            var standard = Object(sourceGroups, "standard"); Sources(standard, "mp4"); Sources(standard, "hls");
            Sources(standard, "h264", "video/mp4", encodedAddresses: true);
            Sources(Object(sourceGroups, "hls"), "h264", "application/vnd.apple.mpegurl", encodedAddresses: true);
        }
        foreach (Match call in calls)
        {
            var literal = JavaScriptDataLiteral.ObjectArgument("{\"url\":" + call.Groups["literal"].Value + "}", 0, out _, ct);
            var quality = call.Groups["quality"].Value; Add(literal["url"]?.GetValue<string>(), quality == "UrlHigh" ? "High" : quality == "UrlLow" ? "Low" : "HLS", quality == "UrlHigh" ? 2 : quality == "UrlLow" ? 1 : 3);
        }
        return Result() with { Media = media, Warnings = media.Count == 0 ? ["The selected player has no supported direct or adaptive video source."] : [] };

        PageMetadata Result() => new() { Id = Download.AdaptiveFragmentCheckpoint.Hash(page.AbsoluteUri), Extractor = "generic:player", Title = metadata.Title, Description = metadata.Description, ThumbnailUrl = metadata.ThumbnailUrl,
            DurationSeconds = Duration(), SourcePageUrl = page, AnonymousPlayback = true, PublicServicePlayback = true, PlaybackHeaders = new Dictionary<string, string> { ["Referer"] = page.AbsoluteUri } };
        double? Duration() => metadata.DurationSeconds ?? (Number(configRoot, "duration") is > 0 and var duration && double.IsFinite(duration) ? duration : null);
        void Sources(JsonElement root, string name, string? mime = null, bool encodedAddresses = false)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var values)) return;
            if (values.ValueKind == JsonValueKind.String) { Source(values.GetString(), null); return; }
            if (values.ValueKind == JsonValueKind.Object) { Source(Text(values, "src") ?? Text(values, "file") ?? Text(values, "url"), Text(values, "label") ?? Text(values, "quality")); return; }
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64) throw new InvalidDataException("The selected player has an invalid source inventory.");
            foreach (var row in values.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (row.ValueKind == JsonValueKind.String) Source(row.GetString(), null);
                else if (row.ValueKind == JsonValueKind.Object)
                {
                    if (PublicProviderApi.Drm(row)) throw new NotSupportedException("The selected source is protected.");
                    Source(Text(row, "src") ?? Text(row, "file") ?? Text(row, "url") ?? Text(row, "videoUrl"), Text(row, "label") ?? Text(row, "quality"));
                }
            }
            void Source(string? raw, string? label)
            {
                if (encodedAddresses && !string.IsNullOrWhiteSpace(raw)) raw = XplayerLinkDecoder.Decode(raw, page, ct);
                Add(raw, label, 0, mime);
            }
        }
        void Add(string? raw, string? label, int order, string? mime = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (!Uri.TryCreate(page, raw, out var url) || url.Scheme is not ("http" or "https") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 || url.AbsoluteUri.Length > 16384) throw new InvalidDataException("The selected player returned an invalid source URL.");
            var kind = PageMediaExtractor.Classify(url, mime); if (kind == MediaKind.Unknown || !seen.Add(url.AbsoluteUri)) return;
            if (media.Count >= 64) throw new InvalidDataException("The selected player returned too many media renditions.");
            var heightText = Regex.Match(label ?? "", @"\A(?<height>[1-9][0-9]{1,3})p?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var height = heightText.Success && int.TryParse(heightText.Groups["height"].Value, out var number) ? number : (int?)null;
            var extension = kind == MediaKind.Progressive ? PageMediaExtractor.ExtensionFrom(url, mime) : "mp4"; var audio = extension is "mp3" or "m4a" or "aac" or "ogg" or "opus" or "flac" or "wav";
            var hlsGroup = Object(Object(configRoot, "sources"), "hls");
            var adaptiveXplayer = Object(hlsGroup, "h264").ValueKind == JsonValueKind.Object;
            media.Add(new() { Id = "player-" + Download.AdaptiveFragmentCheckpoint.Hash(url.GetLeftPart(UriPartial.Path))[..16], Url = url, Kind = kind, Extension = extension, HasVideo = !audio, HasAudio = audio ? true : null, IsCompleteMedia = true,
                IsFallback = adaptiveXplayer && kind == MediaKind.Progressive, Height = audio ? null : height, Label = label, ProviderQualityOrder = order, Source = "common-player", VerificationDurationSeconds = Duration(), VerificationDemuxer = kind != MediaKind.Progressive || extension is "mp4" or "m4a" or "m4v" or "mov" ? "mov" : extension == "mp3" ? "mp3" : null });
        }
    }
}
