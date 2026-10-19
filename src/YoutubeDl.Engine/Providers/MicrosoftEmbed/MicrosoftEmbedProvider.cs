using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.MicrosoftEmbed;

public sealed class MicrosoftEmbedProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("microsoftembed", "Microsoft public Universal Media Player", 210);
    public bool CanHandle(Uri url) => MicrosoftEmbedAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request)
    {
        var address = MicrosoftEmbedAddress.Parse(request.Url);
        var options = Options(address); var power = PowerId(address, options);
        return power is { } id ? new("microsoftembed", id) : null;
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var address = MicrosoftEmbedAddress.Parse(request.Url); var options = Options(address); var configured = (JsonObject)options.DeepClone();
        if (options["ageGate"] is JsonObject configuredAge && configuredAge["minimumAge"] is not null && (NumberNode(configuredAge["minimumAge"]) is not { } requiredAge || requiredAge > 0))
            throw new NotSupportedException("This Microsoft player requires additional native age/account workflow support.");
        using var anonymous = http.CreateAnonymousSession(); var adobe = options["adobeCms"] as JsonObject;
        var powerId = PowerId(address, options);
        if (powerId is { } power)
        {
            if (adobe is not null || options["sources"] is JsonArray { Count: > 0 }) throw new NotSupportedException("Mixed Microsoft CMS and directly supplied playback settings need additional support.");
            using var mapping = await JsonAsync(new("https://www.microsoft.com/videoplayer/umpOptionsMappings/" + power + ".json"), anonymous, ct).ConfigureAwait(false);
            options = JsonNode.Parse(mapping.RootElement.GetRawText()) as JsonObject ?? throw new InvalidDataException("Microsoft returned invalid player mapping settings.");
            adobe = options["adobeCms"] as JsonObject;
            if (adobe is null) throw new NotSupportedException("This Microsoft video mapping does not expose a supported Cascade identity.");
        }
        string? cascadeId = null;
        if (adobe is not null)
        {
            if (options["sources"] is JsonArray { Count: > 0 }) throw new NotSupportedException("Mixed Microsoft CMS and directly supplied sources need additional support.");
            var group = Value(adobe, "operatingGroup"); var video = Value(adobe, "videoId");
            if (group is null || video is null || !MicrosoftEmbedAddress.Identifier(group) || !MicrosoftEmbedAddress.Identifier(video)) throw new InvalidDataException("Microsoft returned incomplete or invalid Cascade identities.");
            var endpoint = new Uri("https://video.cascade.microsoft.com/api/og/" + Uri.EscapeDataString(group) + "/videos/" + Uri.EscapeDataString(video) + "/playback-info");
            using var response = await JsonAsync(endpoint, anonymous, ct).ConfigureAwait(false); var root = response.RootElement;
            if (Text(root, "videoId") is { } declaredId && declaredId != video || Text(root, "operatingGroup") is { } declaredGroup && declaredGroup != group)
                throw new InvalidDataException("Microsoft Cascade returned another video or operating group.");
            if (root.TryGetProperty("minimumAge", out var minimumAge) && minimumAge.ValueKind != JsonValueKind.Null && (Number(root, "minimumAge") is not { } apiRequiredAge || apiRequiredAge > 0))
                throw new NotSupportedException("This Microsoft video requires additional native age/account workflow support.");
            var api = JsonNode.Parse(root.GetRawText()) as JsonObject ?? throw new InvalidDataException("Microsoft returned malformed public playback metadata.");
            options = new JsonObject { ["sources"] = api["sources"]?.DeepClone(), ["ccFiles"] = api["closedCaptions"]?.DeepClone(), ["downloadableFiles"] = api["downloadableFiles"]?.DeepClone(),
                ["title"] = api["videoTitle"]?.DeepClone(), ["poster"] = api["posterUrl"]?.DeepClone(), ["duration"] = api["duration"]?.DeepClone() };
            foreach (var key in new[] { "title", "poster" }) if (configured[key] is not null) options[key] = configured[key]!.DeepClone();
            if (configured["duration"] is not null && options["duration"] is null) options["duration"] = configured["duration"]!.DeepClone();
            if (configured["ccFiles"] is JsonArray suppliedCaptions)
            {
                var captions = options["ccFiles"] as JsonArray ?? new(); if (options["ccFiles"] is null) options["ccFiles"] = captions;
                var languages = suppliedCaptions.OfType<JsonObject>().Select(track => Value(track, "locale")).Where(language => language is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var index = captions.Count - 1; index >= 0; index--) if (captions[index] is JsonObject track && languages.Contains(Value(track, "locale"))) captions.RemoveAt(index);
                foreach (var track in suppliedCaptions) captions.Add(track?.DeepClone());
            }
            cascadeId = group + "/" + video;
        }
        if (options["ageGate"] is JsonObject age && NumberNode(age["minimumAge"]) is > 0 || options.ContainsKey("drm") || options.ContainsKey("licenseServers"))
            throw new NotSupportedException("These Microsoft player access/protection settings need additional native support.");
        var sources = options["sources"] as JsonArray ?? throw new NotSupportedException("The Microsoft player does not expose supported public sources.");
        if (sources.Count > 64) throw new InvalidDataException("Microsoft returned too many player sources.");
        var media = new List<MediaCandidate>(); var urls = new HashSet<string>(StringComparer.Ordinal); var warnings = new List<string>(); var formats = new HashSet<string>(StringComparer.Ordinal);
        var duration = NumberNode(options["duration"]); if (duration is <= 0 or > 604800 || options["duration"] is not null && duration is null) throw new InvalidDataException("The Microsoft player has an invalid video duration.");
        foreach (var source in sources)
        {
            if (media.Count >= 64 || source is not JsonObject item) throw new InvalidDataException("Microsoft returned an excessive or malformed media inventory.");
            var type = Value(item, "type"); var raw = Value(item, "src") ?? Value(item, "url"); var kind = type switch { "MP4" or "video/mp4" => MediaKind.Progressive,
                "HLS" or "application/x-mpegURL" or "application/vnd.apple.mpegurl" => MediaKind.Hls, "DASH" or "application/dash+xml" => MediaKind.Dash, _ => MediaKind.Unknown };
            if (kind == MediaKind.Unknown) { warnings.Add("A supplied Microsoft player format requires additional native support."); continue; }
            var url = Resource(raw); if (!urls.Add(url.AbsoluteUri)) continue;
            var quality = Value(item, "quality"); var ordinal = quality switch { "HD" => 4, "HQ" => 3, "SD" => 2, "LO" => 1, _ => 0 };
            if (quality is not null && ordinal == 0) throw new NotSupportedException("This Microsoft quality label requires additional native support.");
            var id = kind == MediaKind.Progressive ? quality ?? "mp4" : kind == MediaKind.Hls ? "hls" : "dash";
            if (!formats.Add(id))
            {
                if (quality is not null) throw new InvalidDataException("Microsoft returned conflicting named format identities.");
                id += "-" + (media.Count + 1); if (!formats.Add(id)) throw new InvalidDataException("Microsoft returned conflicting format identities.");
            }
            media.Add(new() { Id = id, Label = quality, ProviderQualityOrder = ordinal, Url = url, Kind = kind, Extension = kind == MediaKind.Hls ? "ts" : "mp4", HasVideo = true, HasAudio = null,
                IsCompleteMedia = true, Source = Info.Id, VerificationDemuxer = kind == MediaKind.Hls ? "mpegts" : "mov", VerificationDurationSeconds = duration });
        }
        var subtitles = new List<SubtitleTrack>(); var tracks = options["ccFiles"] as JsonArray;
        if (tracks is not null)
        {
            if (tracks.Count > 128) throw new InvalidDataException("Microsoft returned too many caption resources.");
            var candidates = new List<SubtitleTrack>();
            foreach (var node in tracks)
            {
                if (node is not JsonObject track || Value(track, "locale") is not { } language || !Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidDataException("Microsoft returned invalid caption language metadata.");
                var extension = Value(track, "ccType") switch { "VTT" or "text/vtt" => "vtt", "TTML" or "application/ttml+xml" => "ttml", _ => null };
                if (extension is null) throw new NotSupportedException("This Microsoft caption format needs additional support.");
                candidates.Add(new() { Url = Resource(Value(track, "url")), Language = language.ToLowerInvariant(), Extension = extension });
            }
            foreach (var group in candidates.GroupBy(track => track.Language, StringComparer.OrdinalIgnoreCase))
            {
                var preferred = group.Where(track => track.Extension == "vtt").ToArray(); var available = preferred.Length > 0 ? preferred : group.ToArray();
                if (available.Select(track => track.Url).Distinct().Count() != 1) throw new InvalidDataException("Microsoft returned ambiguous preferred caption resources.");
                subtitles.Add(available[0]);
            }
        }
        var identity = powerId ?? cascadeId ?? "supplied-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', media.Select(item => item.Url.AbsoluteUri).Order(StringComparer.Ordinal))))).ToLowerInvariant();
        return new() { Id = identity, Extractor = Info.Id, ArchiveIdentity = new(Info.Id, identity), Title = Value(options, "title") ?? Value(options, "unlocalizedTitle") ?? identity,
            DurationSeconds = duration, Media = media.OrderByDescending(item => item.ProviderQualityOrder).ToArray(), Subtitles = subtitles, ThumbnailUrl = Value(options, "poster") is { Length: > 0 } poster ? Resource(poster).AbsoluteUri : null,
            Uploader = "Microsoft", AnonymousPlayback = true, SourcePageUrl = address.Page, Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray() };
    }
    private static JsonObject Options(MicrosoftEmbedAddress address)
    {
        JsonObject options = new();
        if (address.Query.TryGetValue("options", out var json))
        {
            using var parsed = ParseJson(json[0]); options = JsonNode.Parse(parsed.RootElement.GetRawText()) as JsonObject ?? throw new InvalidDataException("Microsoft player options must be a JSON object.");
        }
        if (options.ContainsKey("_nativePowerId")) throw new InvalidDataException("Microsoft options contain a reserved native field.");
        foreach (var key in new[] { "sources", "ccFiles", "downloadableFiles" }) if (options[key] is not null && options[key] is not JsonArray)
            throw new InvalidDataException("Microsoft player inventories must be JSON arrays.");
        foreach (var key in new[] { "adobeCms", "plugins", "ageGate" }) if (options[key] is not null && options[key] is not JsonObject)
            throw new InvalidDataException("Microsoft player settings have invalid object shapes.");
        foreach (var key in new[] { "title", "unlocalizedTitle", "poster" }) if (address.Query.TryGetValue(key, out var text)) options[key] = text[0];
        foreach (var key in new[] { "adobeCmsOperatingGroup", "adobeCmsVideoId" }) if (address.Query.TryGetValue(key, out var value))
        { var adobe = options["adobeCms"] as JsonObject ?? new(); if (options["adobeCms"] is null) options["adobeCms"] = adobe; adobe[key == "adobeCmsVideoId" ? "videoId" : "operatingGroup"] = value[0]; }
        foreach (var key in new[] { "mp4", "hls", "dash", "mss" }) if (address.Query.TryGetValue(key, out var values))
        {
            var sources = options["sources"] as JsonArray ?? new(); if (options["sources"] is null) options["sources"] = sources;
            foreach (var value in values)
            {
                var pair = key == "mp4" ? value.Split(',', 2) : ["", value];
                if (key == "mp4" && (pair.Length != 2 || pair[0] is not ("HD" or "HQ" or "SD" or "LO"))) throw new ArgumentException("A Microsoft MP4 option needs its declared quality label and URL.");
                var source = new JsonObject { ["type"] = key.ToUpperInvariant(), ["src"] = pair[1] }; if (key == "mp4") source["quality"] = pair[0]; sources.Add(source);
            }
        }
        foreach (var key in new[] { "vtt", "ttml" }) if (address.Query.TryGetValue(key, out var values))
        {
            var tracks = options["ccFiles"] as JsonArray ?? new(); if (options["ccFiles"] is null) options["ccFiles"] = tracks;
            foreach (var value in values) { var pair = value.Split(',', 2); if (pair.Length != 2) throw new ArgumentException("A Microsoft caption option needs its locale and URL."); tracks.Add(new JsonObject { ["locale"] = pair[0], ["ccType"] = key.ToUpperInvariant(), ["url"] = pair[1] }); }
        }
        if (options["plugins"] is JsonObject plugins && plugins.Count > 0)
        {
            if (plugins.Count != 1 || plugins["powerCms"] is not JsonObject power || Value(power, "videoId") is not { } id || !MicrosoftEmbedAddress.Identifier(id))
                throw new NotSupportedException("Additional Microsoft player plugins need native integration.");
            options["_nativePowerId"] = id;
        }
        if (options.ContainsKey("drm") || options.ContainsKey("licenseServers")) throw new NotSupportedException("Protected Microsoft player settings need additional native support.");
        return options;
    }
    private static string? PowerId(MicrosoftEmbedAddress address, JsonObject options)
    {
        var plugin = Value(options, "_nativePowerId");
        if (address.PowerId is not null && plugin is not null && plugin != address.PowerId) throw new InvalidDataException("The Microsoft player has conflicting plugin and path identities.");
        return address.PowerId ?? plugin;
    }
    private static string? Value(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static double? NumberNode(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        var valid = value.TryGetValue<double>(out var number) || value.TryGetValue<string>(out var text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
        return valid && double.IsFinite(number) && number >= 0 ? number : null;
    }
    private static Uri Resource(string? raw) => Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 ? uri :
        throw new InvalidDataException("Microsoft returned an unsupported public media resource.");
    private static JsonDocument ParseJson(string text)
    {
        JsonDocument json; try { json = JsonDocument.Parse(text, new() { MaxDepth = 48 }); } catch (JsonException) { throw new InvalidDataException("Microsoft returned malformed player metadata."); }
        try
        {
            var stack = new Stack<JsonElement>(); stack.Push(json.RootElement); var count = 0;
            while (stack.TryPop(out var node))
            {
                if (++count > 100000) throw new InvalidDataException("Microsoft returned excessive player metadata.");
                if (node.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in node.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Microsoft returned repeated metadata fields."); stack.Push(property.Value); } }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) stack.Push(child);
            }
            return json;
        }
        catch { json.Dispose(); throw; }
    }
    private static async Task<JsonDocument> JsonAsync(Uri endpoint, MediaHttp http, CancellationToken ct)
    { var response = await http.GetDocumentAsync(endpoint, 4 * 1024 * 1024, ct).ConfigureAwait(false); if (response.Url != endpoint) throw new InvalidDataException("Microsoft redirected its player metadata identity."); return ParseJson(response.Text); }
}
