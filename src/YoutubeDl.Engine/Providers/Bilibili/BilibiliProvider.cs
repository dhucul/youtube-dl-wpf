using System.Globalization;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.Bilibili;

public sealed partial class BilibiliProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("bilibili", "Bilibili public videos and multipart uploads", 200);
    public bool CanHandle(Uri url)
    {
        try { _ = BilibiliAddress.Parse(url); return true; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = BilibiliAddress.Parse(request.Url);
        var headers = new Dictionary<string, string> { ["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? "https://www.bilibili.com/" : request.Referer,
            ["User-Agent"] = string.IsNullOrWhiteSpace(request.UserAgent) ? "YoutubeDlNative/" + NativeEngine.Version : request.UserAgent };
        using var scoped = http.WithHeaders(headers);
        var selector = address.Bvid is { } bv ? "bvid=" + bv : "aid=" + address.Aid!.Value.ToString(CultureInfo.InvariantCulture);
        using var metadata = await ReadApi(scoped, "/x/web-interface/view?" + selector, ct).ConfigureAwait(false);
        var video = metadata.RootElement.GetProperty("data");
        var bvid = Text(video, "bvid"); var aid = Long(video, "aid");
        if (!BilibiliAddress.IsBvid(bvid) || aid is not > 0 || address.Bvid is not null && address.Bvid != bvid || address.Aid is not null && address.Aid != aid)
            throw new InvalidDataException("Bilibili returned another video identity.");
        if (!video.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Number || !state.TryGetInt32(out var stateNumber) || stateNumber != 0)
            throw new NotSupportedException("Bilibili has not made this upload available as a public video.");
        CheckRestrictions(video);
        var rights = Object(video, "rights");
        if (rights.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Bilibili omitted video access information.");
        if (new[] { "pay", "ugc_pay", "ugc_pay_preview", "arc_pay", "is_stein_gate" }.Any(name => Active(rights, name)))
            throw new NotSupportedException("Paid, preview or interactive Bilibili uploads require additional native support.");
        var pages = Array(video, "pages");
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        if (pages.Length == 0 || pages.Length > request.MaximumPlaylistEntries || Integer(video, "videos") != pages.Length)
            throw new InvalidDataException("Bilibili did not return a complete bounded part inventory.");
        var title = Text(video, "title") ?? bvid!;
        var thumbnail = Text(video, "pic") is { Length: > 0 } pic ? Resource(pic, image: true).AbsoluteUri : null;
        var entries = new List<NativePlaylistEntry>(); var identities = new HashSet<long>();
        foreach (var part in pages)
        {
            ct.ThrowIfCancellationRequested();
            var cid = Long(part, "cid"); var position = Integer(part, "page");
            if (cid is not > 0 || !identities.Add(cid.Value) || position != entries.Count + 1 || Text(part, "from") != "vupload" || !string.IsNullOrEmpty(Text(part, "weblink")))
                throw new InvalidDataException("Bilibili returned an inconsistent or externally hosted part inventory.");
            entries.Add(new(position.Value, bvid + "_" + cid.Value.ToString(CultureInfo.InvariantCulture),
                pages.Length == 1 ? title : title + " - P" + position.Value + " " + (Text(part, "part") ?? ""),
                new Uri("https://www.bilibili.com/video/" + bvid + "/?p=" + position.Value.ToString(CultureInfo.InvariantCulture)),
                Number(part, "duration"), thumbnail, true));
        }
        if (Long(video, "cid") != Long(pages[0], "cid")) throw new InvalidDataException("Bilibili returned an inconsistent first-part identity.");
        var common = new PageMetadata { Id = bvid, Extractor = Info.Id, Title = title, Description = Text(video, "desc"),
            Uploader = Text(Object(video, "owner"), "name"), ThumbnailUrl = thumbnail,
            SourcePageUrl = new Uri("https://www.bilibili.com/video/" + bvid + "/"), PlaybackHeaders = headers };
        if (address.Part is null && address.Cid is null && request.DownloadPlaylist && entries.Count > 1)
            return common with { IsPlaylist = true, PlaylistId = bvid, Entries = entries, DurationSeconds = Number(video, "duration") };
        var selected = address.Cid is { } requestedCid ? System.Array.FindIndex(pages, part => Long(part, "cid") == requestedCid) : (address.Part ?? 1) - 1;
        if (selected < 0 || selected >= pages.Length || address.Part is { } requestedPart && selected != requestedPart - 1)
            throw new InvalidDataException("The requested Bilibili part does not match this upload.");
        var entry = entries[selected]; var selectedCid = Long(pages[selected], "cid")!.Value;
        headers["Referer"] = string.IsNullOrWhiteSpace(request.Referer) ? entry.Url!.AbsoluteUri : request.Referer;
        using var playbackHttp = http.WithHeaders(headers);
        using var playback = await ReadApi(playbackHttp, "/x/player/playurl?bvid=" + bvid + "&cid=" + selectedCid.ToString(CultureInfo.InvariantCulture) + "&qn=80&fnval=16&fnver=0", ct).ConfigureAwait(false);
        var data = playback.RootElement.GetProperty("data");
        CheckRestrictions(data);
        if (Text(data, "bvid") is { } playbackBv && playbackBv != bvid || Long(data, "cid") is { } playbackCid && playbackCid != selectedCid)
            throw new InvalidDataException("Bilibili returned playback for another part.");
        var milliseconds = Number(data, "timelength");
        if (milliseconds is not > 0 || entry.DurationSeconds is > 0 and var duration && Math.Abs(milliseconds.Value / 1000 - duration) > Math.Max(1.1, duration * .005))
            throw new NotSupportedException("Bilibili did not expose a complete playback duration for this part.");
        var warnings = new List<string>();
        var media = ReadMedia(data, selectedCid, warnings);
        return common with { Id = entry.Id, Title = entry.Title, DurationSeconds = milliseconds / 1000, SourcePageUrl = entry.Url,
            Media = media, Warnings = warnings, PlaybackHeaders = headers };
    }
    private static async Task<JsonDocument> ReadApi(MediaHttp http, string path, CancellationToken ct)
    {
        var endpoint = new Uri("https://api.bilibili.com" + path);
        var document = await http.GetDocumentAsync(endpoint, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (document.Url != endpoint) throw new InvalidDataException("Bilibili redirected metadata away from the requested video endpoint.");
        var json = JsonDocument.Parse(document.Text);
        if (json.RootElement.ValueKind == JsonValueKind.Object && Integer(json.RootElement, "code") == 0 && Object(json.RootElement, "data").ValueKind == JsonValueKind.Object) return json;
        json.Dispose();
        throw new NotSupportedException("Bilibili denied or could not resolve this public video request.");
    }
    private static bool Active(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind switch
    {
        JsonValueKind.False or JsonValueKind.Null => false,
        JsonValueKind.Number => !field.TryGetDecimal(out var number) || number != 0,
        JsonValueKind.String => field.GetString() is not (null or "" or "0" or "false"),
        JsonValueKind.Array => field.GetArrayLength() > 0,
        JsonValueKind.Object => field.EnumerateObject().Any(), _ => true
    };
    private static void CheckRestrictions(JsonElement value)
    {
        if (new[] { "is_preview", "is_upower_exclusive", "is_upower_play", "is_upower_preview", "is_chargeable_season", "drm", "drm_tech_type", "is_drm", "is_live" }.Any(name => Active(value, name)))
            throw new NotSupportedException("Restricted, preview, live or protected Bilibili playback is not supported by this native provider.");
    }
    private static Uri Resource(string raw, bool image = false)
    {
        if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("Bilibili returned an invalid resource address.");
        var host = url.Host;
        var allowed = image ? host.EndsWith(".hdslb.com", StringComparison.Ordinal) :
            host.EndsWith(".bilivideo.com", StringComparison.Ordinal) || host.EndsWith(".bilivideo.cn", StringComparison.Ordinal) ||
            host.EndsWith(".bilivideo.net", StringComparison.Ordinal) || host.StartsWith("upos-", StringComparison.Ordinal) && host.EndsWith(".akamaized.net", StringComparison.Ordinal);
        if (!allowed) throw new NotSupportedException("Bilibili returned a resource origin that requires additional native validation.");
        return url;
    }
}
