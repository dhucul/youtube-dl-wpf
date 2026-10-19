using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using static YoutubeDl.Engine.Providers.ProviderJson;

namespace YoutubeDl.Engine.Providers.YouTube;

/// <summary>Validates matching public player metadata and an observed browser playback request.
/// A supplied proof remains subject to the server's authorization and complete-track checks.</summary>
public static class YouTubeObservedPlayback
{
    public static PageMetadata Parse(string playerJson, string expectedVideoId, Uri endpoint, ReadOnlyMemory<byte> requestBody, string? userAgent = null, CancellationToken ct = default, YouTubeObservedPlaybackEvidence? previous = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || !endpoint.IsDefaultPort || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase) || endpoint.AbsolutePath != "/videoplayback")
            throw new InvalidDataException("The observed playback endpoint is not a supported GoogleVideo resource.");
        if (userAgent is not null && (userAgent.Length > 4096 || userAgent.Any(char.IsControl))) throw new ArgumentException("The observed user agent is invalid.");
        if (Encoding.UTF8.GetByteCount(playerJson) > 8 * 1024 * 1024) throw new InvalidDataException("The observed player document exceeds its limit.");
        using var json = JsonDocument.Parse(playerJson, new JsonDocumentOptions { MaxDepth = 64 });
        var configurationText = Text(Object(Object(Object(json.RootElement, "playerConfig"), "mediaCommonConfig"), "mediaUstreamerRequestConfig"), "videoPlaybackUstreamerConfig");
        if (configurationText is null || configurationText.Length > 65536) throw new InvalidDataException("The observed player lacks a bounded playback configuration.");
        byte[] configuration;
        try { var value = configurationText.Replace('-', '+').Replace('_', '/'); configuration = Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '=')); }
        catch (FormatException) { throw new InvalidDataException("The observed playback configuration is invalid."); }
        var root = ProtobufReader.Read(requestBody, maximumBytes: 256 * 1024, maximumFields: 512);
        if (configuration.Length == 0 || !One(root, 5, 2).Bytes.Span.SequenceEqual(configuration))
            throw new InvalidDataException("The observed request belongs to another player configuration.");
        var context = ProtobufReader.Read(One(root, 19, 2).Bytes, maximumBytes: 65536, maximumFields: 128);
        var info = One(context, 1, 2).Bytes;
        var client = ProtobufReader.Read(info, maximumBytes: 16384, maximumFields: 128);
        var name = One(client, 16, 0).Integer;
        if (name != 1) throw new NotSupportedException("Only observed Web-client playback is supported by this reader.");
        var version = new UTF8Encoding(false, true).GetString(One(client, 17, 2).Bytes.Span);
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl)) throw new InvalidDataException("The observed client version is invalid.");
        var proof = One(context, 2, 2).Bytes;
        if (proof.Length is 0 or > 16384) throw new InvalidDataException("The observed request did not include a bounded playback proof.");
        var states = root.Where(field => field.Number == 1).ToArray();
        if (states.Length > 1 || states.Any(field => field.WireType != 2)) throw new InvalidDataException("The observed client playback state is ambiguous.");
        var abr = states.Length == 0 ? ReadOnlyMemory<byte>.Empty : states[0].Bytes;
        _ = ProtobufReader.Read(abr, maximumBytes: 16384, maximumFields: 256);
        var cookies = context.Where(field => field.Number == 3).ToArray();
        if (cookies.Length > 1 || cookies.Any(field => field.WireType != 2 || field.Bytes.Length > 16384)) throw new InvalidDataException("The observed playback cookie is invalid.");
        var initialContexts = new Dictionary<uint, byte[]>();
        foreach (var field in context.Where(field => field.Number == 5))
        {
            if (field.WireType != 2 || initialContexts.Count >= 64) throw new InvalidDataException("The observed playback context exceeds its limit.");
            var values = ProtobufReader.Read(field.Bytes, maximumBytes: 32768, maximumFields: 16);
            var type = One(values, 1, 0).Integer; var value = One(values, 2, 2).Bytes;
            if (type > uint.MaxValue || value.Length > 16384 || !initialContexts.TryAdd((uint)type, value.ToArray())) throw new InvalidDataException("The observed playback contexts are ambiguous.");
        }
        var session = new SabrPlaybackSession(expectedVideoId, endpoint, (uint)name, version, configuration, userAgent)
        { ProofToken = proof.ToArray(), ClientInfo = info.ToArray(), ClientAbrState = abr.ToArray(), InitialPlaybackCookie = cookies.Length == 0 ? [] : cookies[0].Bytes.ToArray(), InitialContexts = initialContexts };
        // The normal parser checks playability, video identity, and protected representations.
        var page = YouTubePlayerParser.ParseResponse(playerJson, expectedVideoId, ct: ct, sabrSession: session);
        if (page.IsLive) throw new NotSupportedException("Observed live playback requires additional recording support.");
        var offered = page.Media.Where(media => media.Sabr is not null).Select(media => media.Sabr!.Format).ToHashSet();
        var referenced = References(root, ct);
        if (referenced.Count == 0)
        {
            if (previous is null || previous.VideoId != expectedVideoId || previous.Formats.Count == 0 || previous.Formats.Any(format => !offered.Contains(format)))
                throw new InvalidDataException("The observed control request lacks matching prior format evidence.");
        }
        else if (referenced.Count > 128 || referenced.Any(format => !offered.Contains(format)))
            throw new InvalidDataException("The observed request references media outside the requested video's offered formats.");
        page = YouTubePlaybackCapabilities.ApplyAudio(page, abr);
        return page with { AnonymousPlayback = true, PlaybackHeaders = userAgent is null ? page.PlaybackHeaders : new Dictionary<string, string> { ["User-Agent"] = userAgent } };
    }
    public static YouTubeObservedPlaybackEvidence Observe(string playerJson, string expectedVideoId, Uri endpoint, ReadOnlyMemory<byte> requestBody, CancellationToken ct = default)
    {
        _ = Parse(playerJson, expectedVideoId, endpoint, requestBody, ct: ct);
        return new(expectedVideoId, References(ProtobufReader.Read(requestBody, maximumBytes: 256 * 1024, maximumFields: 512), ct).Distinct().ToArray());
    }
    public static async Task<YouTubeObservedPlaybackEvidence> ObserveResponseAsync(string expectedVideoId, ReadOnlyMemory<byte> response, CancellationToken ct = default)
    {
        if (response.Length > 8 * 1024 * 1024) throw new InvalidDataException("The observed response exceeds its limit.");
        using var input = new MemoryStream(response.ToArray()); var formats = new HashSet<SabrFormatId>();
        await foreach (var part in UmpReader.ReadAsync(input, maximumParts: 2048, ct: ct).ConfigureAwait(false))
        {
            if (part.Type == 58)
            {
                var status = One(ProtobufReader.Read(part.Payload, maximumBytes: 65536), 1, 0).Integer;
                if (status >= 3) throw new NotSupportedException("The observed browser response denied playback authorization.");
            }
            if (part.Type is not (20 or 42)) continue;
            var fields = ProtobufReader.Read(part.Payload, maximumBytes: 65536);
            var id = new UTF8Encoding(false, true).GetString(One(fields, part.Type == 20 ? 2 : 1, 2).Bytes.Span);
            if (id != expectedVideoId) throw new InvalidDataException("The observed response belongs to another video.");
            formats.Add(SabrControlState.ReadFormat(One(fields, part.Type == 20 ? 13 : 2, 2).Bytes));
            if (formats.Count > 128) throw new InvalidDataException("The observed response declares too many formats.");
        }
        if (formats.Count == 0) throw new InvalidDataException("The observed response lacks video-bound format declarations.");
        return new(expectedVideoId, formats.ToArray());
    }
    private static List<SabrFormatId> References(IReadOnlyList<ProtobufField> root, CancellationToken ct)
    {
        var referenced = new List<SabrFormatId>();
        foreach (var field in root)
        {
            ct.ThrowIfCancellationRequested();
            if (field.Number is 2 or 16 or 17)
            {
                if (field.WireType != 2) throw new InvalidDataException("An observed format reference is invalid.");
                referenced.Add(SabrControlState.ReadFormat(field.Bytes));
            }
            else if (field.Number == 3)
            {
                if (field.WireType != 2) throw new InvalidDataException("An observed buffered range is invalid.");
                referenced.Add(SabrControlState.ReadFormat(One(ProtobufReader.Read(field.Bytes, maximumBytes: 16384), 1, 2).Bytes));
            }
        }
        return referenced;
    }
    private static ProtobufField One(IReadOnlyList<ProtobufField> fields, int number, int wire)
    {
        var found = fields.Where(field => field.Number == number).ToArray();
        if (found.Length != 1 || found[0].WireType != wire) throw new InvalidDataException("The observed playback request has missing or repeated identity fields.");
        return found[0];
    }
}

public sealed class YouTubeObservedPlaybackEvidence
{
    public string VideoId { get; }
    public IReadOnlyList<SabrFormatId> Formats { get; }
    internal YouTubeObservedPlaybackEvidence(string videoId, SabrFormatId[] formats)
    { VideoId = videoId; Formats = System.Array.AsReadOnly(formats); }
}
