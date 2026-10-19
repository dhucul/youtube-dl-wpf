using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task JoinMediaFilesAsync(string ffmpeg, IReadOnlyList<(string Path, double Duration)> parts, string output, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var list = Path.Combine(directory, "files.ffconcat");
        if (File.Exists(list) && (File.GetAttributes(list) & FileAttributes.ReparsePoint) != 0) throw new IOException("The local media inventory cannot be a link.");
        var text = new StringBuilder("ffconcat version 1.0\n");
        IReadOnlyList<ProbedStream>? first = null;
        foreach (var part in parts)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(part.Path); var name = Path.GetFileName(path);
            if (Path.GetDirectoryName(path) != directory || !Regex.IsMatch(name, @"\Apart-[0-9]{6}\.(?:mp4|flv|mp3)\z", RegexOptions.CultureInvariant) ||
                !double.IsFinite(part.Duration) || part.Duration <= 0)
                throw new InvalidDataException("An ordered media file is outside its owned staging directory or has invalid timing.");
            var streams = await ProbeStreamsAsync(ffmpeg, path, ct, "verify ordered media compatibility", countFrames: Path.GetExtension(path) == ".mp3", localMediaOnly: true).ConfigureAwait(false);
            if (streams.Any(stream => stream.Kind is not ("video" or "audio") || stream.IsCover || stream.StartTime is null))
                throw new NotSupportedException("This ordered media sequence contains unsupported tracks or clocks.");
            if (first is not null && !first.Select(Identity).SequenceEqual(streams.Select(Identity)))
                throw new NotSupportedException("The ordered media files change codec, dimensions, audio layout, time base or decoder configuration.");
            first ??= streams;
            var duration = part.Duration;
            if (Path.GetExtension(path) == ".mp3")
            {
                if (streams.Count != 1 || streams[0].Kind != "audio" || streams[0].Codec != "mp3" || streams[0].SampleRate is not (8000 or 11025 or 12000 or 16000 or 22050 or 24000 or 32000 or 44100 or 48000) || !long.TryParse(streams[0].FrameCount, NumberStyles.None, CultureInfo.InvariantCulture, out var frames) || frames is < 1 or > 30000000) throw new NotSupportedException("Ordered MP3 files require one complete declared MP3 audio track and frame inventory.");
                // MP3 packets occupy whole encoded frames. The editorial interval can end
                // inside the final frame; using it as the next file's clock overlaps DTS.
                var samplesPerFrame = streams[0].SampleRate >= 32000 ? 1152 : 576;
                var encoded = frames * (double)samplesPerFrame / streams[0].SampleRate;
                if (Math.Abs(encoded - duration) > 2d * samplesPerFrame / streams[0].SampleRate) throw new InvalidDataException("An ordered MP3 part differs from its declared interval by more than two encoded frames.");
                duration = encoded;
            }
            text.Append("file '").Append(name).Append("'\nduration ").Append(duration.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        }
        if (first is null) throw new InvalidDataException("No complete media files were supplied for joining.");
        await File.WriteAllTextAsync(list, text.ToString(), new UTF8Encoding(false), ct).ConfigureAwait(false);
        try
        {
            await RunAsync(ffmpeg, ["-y", "-v", "error", "-nostats", "-protocol_whitelist", "file,pipe", "-f", "concat", "-safe", "1", "-auto_convert", "0", "-i", list,
                "-map", "0", "-c", "copy", output], ct, rejectDiagnostics: true).ConfigureAwait(false);
            var result = await ProbeStreamsAsync(ffmpeg, output, ct, "verify the joined media streams").ConfigureAwait(false);
            if (!first.Select(StreamIdentity).SequenceEqual(result.Select(StreamIdentity)))
                throw new InvalidDataException("Joining changed the media tracks or decoder configuration.");
        }
        finally { if (File.Exists(list)) File.Delete(list); }
        static object Identity(ProbedStream stream) => (StreamIdentity(stream), stream.TimeBase);
        static object StreamIdentity(ProbedStream stream) => (stream.Kind, stream.Codec, stream.Width, stream.Height, stream.Channels, stream.ChannelLayout, stream.SampleRate, stream.ExtraDataHash);
    }
}
