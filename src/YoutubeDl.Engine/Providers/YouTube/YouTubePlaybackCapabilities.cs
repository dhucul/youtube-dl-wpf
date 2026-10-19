using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols;

namespace YoutubeDl.Engine.Providers.YouTube;

internal static class YouTubePlaybackCapabilities
{
    internal static PageMetadata ApplyAudio(PageMetadata page, ReadOnlyMemory<byte> abr)
    {
        var offered = ProtobufReader.Read(abr, maximumBytes: 16384, maximumFields: 256).Where(field => field.Number == 38 && field.WireType == 2).ToArray();
        if (offered.Length == 0) return page;
        if (offered.Length != 1) throw new InvalidDataException("The browser supplied ambiguous media capabilities.");
        var limits = new List<(ulong Codec, ulong Channels)>();
        foreach (var capability in ProtobufReader.Read(offered[0].Bytes, maximumBytes: 16384, maximumFields: 128).Where(field => field.Number == 2 && field.WireType == 2))
        {
            var fields = ProtobufReader.Read(capability.Bytes, maximumBytes: 4096, maximumFields: 64);
            var codecs = fields.Where(field => field.Number == 1 && field.WireType == 0).ToArray(); var channels = fields.Where(field => field.Number == 2 && field.WireType == 0).ToArray();
            if (codecs.Length == 1 && channels.Length == 1 && channels[0].Integer > 0) limits.Add((codecs[0].Integer, channels[0].Integer));
        }
        if (limits.Count == 0) return page;
        var unavailable = page.Media.Where(media => media.IsAudioOnly && media.Sabr is not null && !limits.Any(limit =>
            limit.Codec == Codec(media.AudioCodec) && (media.Sabr.AudioTrack?.Channels is not > 0 || limit.Channels >= (ulong)media.Sabr.AudioTrack.Channels.Value)))
            .Select(media => media.Id).ToHashSet(StringComparer.Ordinal);
        if (unavailable.Count == 0) return page;
        const string reason = "This YouTube playback session does not support the audio codec or channel layout; choose an available audio track.";
        return page with { Media = page.Media.Where(media => !unavailable.Contains(media.Id)).ToArray(),
            ReportedFormats = page.ReportedFormats.Select(format => unavailable.Contains(format.Id) ? Unavailable(format, reason) : format).ToArray() };
    }
    private static ulong Codec(string? value) => value?.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) == true ? 1UL : string.Equals(value, "opus", StringComparison.OrdinalIgnoreCase) ? 3UL : 0UL;
    private static NativeFormat Unavailable(NativeFormat value, string reason) => new()
    {
        Id = value.Id, MimeType = value.MimeType, Width = value.Width, Height = value.Height, Bandwidth = value.Bandwidth, Extension = value.Extension, Note = value.Note,
        AudioOnly = value.AudioOnly, HasAudio = value.HasAudio, HasVideo = value.HasVideo, AudioCodec = value.AudioCodec, VideoCodec = value.VideoCodec,
        FramesPerSecond = value.FramesPerSecond, FileSize = value.FileSize, AudioLanguage = value.AudioLanguage, IsDefaultAudio = value.IsDefaultAudio,
        Protocol = value.Protocol, Availability = FormatAvailability.AdaptiveTransportRequired, UnavailableReason = reason
    };
}
