using System.Globalization;
using System.Text;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task<IReadOnlyList<decimal>> JoinHlsPeriodsAsync(string ffmpeg, IReadOnlyList<(string Path, double Duration)> periods, string output, bool fragmented, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(output)!;
        var list = Path.Combine(directory, "periods.ffconcat");
        IReadOnlyList<ProbedStream>? first = null;
        var origins = new List<decimal>();
        var text = new StringBuilder("ffconcat version 1.0\n");
        foreach (var period in periods)
        {
            var streams = await ProbeStreamsAsync(ffmpeg, period.Path, ct, "inspect HLS timeline periods").ConfigureAwait(false);
            if (streams.Any(stream => fragmented ? !stream.Container.Split(',').Contains("mp4", StringComparer.Ordinal) : stream.Container != "mpegts"))
                throw new NotSupportedException("The HLS period does not use the expected MPEG-TS or fragmented MP4 container.");
            if (streams.Any(stream => stream.Kind is not ("audio" or "video") && !(stream.Kind == "data" && stream.Codec == "timed_id3") || stream.IsCover))
                throw new NotSupportedException("This HLS timeline contains unsupported stream types.");
            if (first is not null && !first.Select(Identity).SequenceEqual(streams.Select(Identity)))
                throw new NotSupportedException("The HLS timeline changes codec, dimensions, channel layout or time base; conversion support is required.");
            if (fragmented && (streams.Any(stream => stream.Kind is not ("video" or "audio") || string.IsNullOrEmpty(stream.ExtraDataHash)) ||
                first is not null && !first.Select(stream => stream.ExtraDataHash).SequenceEqual(streams.Select(stream => stream.ExtraDataHash))))
                throw new NotSupportedException("The fragmented HLS timeline changes decoder configuration or contains unsupported streams.");
            first ??= streams;
            var media = streams.Where(stream => stream.Kind is "audio" or "video").ToArray();
            if (media.Length == 0 || media.Any(stream => stream.StartTime is null)) throw new InvalidDataException("The HLS period has no usable media clock.");
            origins.Add(media.Min(stream => stream.StartTime!.Value));
            var name = Path.GetFileName(period.Path);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\Aperiod-[0-9]+\.(?:ts|mp4)\z") || Path.GetDirectoryName(period.Path) != directory)
                throw new InvalidDataException("The HLS period is outside its staging directory.");
            text.Append("file '").Append(name).Append("'\nduration ").Append(period.Duration.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
        }
        await File.WriteAllTextAsync(list, text.ToString(), new UTF8Encoding(false), ct).ConfigureAwait(false);
        var arguments=new List<string>{"-y","-f","concat","-safe","1","-auto_convert","0","-i",list,"-map","0","-c","copy","-f",fragmented?"mp4":"mpegts"};
        if(first!.Any(stream=>stream.Dolby is not null)&&fragmented)arguments.AddRange(["-strict","unofficial"]);arguments.Add(output);
        await RunAsync(ffmpeg,arguments,ct).ConfigureAwait(false);
        var result = await ProbeStreamsAsync(ffmpeg, output, ct, "verify joined HLS media").ConfigureAwait(false);
        if (first is null || !first.Select(StreamIdentity).SequenceEqual(result.Select(StreamIdentity)) || fragmented && !first.Select(stream => stream.ExtraDataHash).SequenceEqual(result.Select(stream => stream.ExtraDataHash)))
            throw new InvalidDataException("The joined HLS output did not retain the selected media streams and decoder configuration.");
        if(!first.Select(stream=>stream.Dolby).SequenceEqual(result.Select(stream=>stream.Dolby)))throw new InvalidDataException("The joined HLS output lost Dolby Vision signaling.");
        return origins;
        static object Identity(ProbedStream stream) => (StreamIdentity(stream), stream.TimeBase);
        static object StreamIdentity(ProbedStream stream) => (stream.Kind, stream.Codec, stream.Width, stream.Height, stream.Channels, stream.ChannelLayout, stream.SampleRate);
    }
    internal static Task MergeHlsAsync(string ffmpeg, string video, string audio, string output, decimal videoOffset, CancellationToken ct)
    {
        var arguments = new List<string> { "-y" };
        if (videoOffset > 0) arguments.AddRange(["-itsoffset", videoOffset.ToString(CultureInfo.InvariantCulture)]);
        arguments.AddRange(["-i", video]);
        if (videoOffset < 0) arguments.AddRange(["-itsoffset", (-videoOffset).ToString(CultureInfo.InvariantCulture)]);
        arguments.AddRange(["-i", audio, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", output]);
        return RunAsync(ffmpeg, arguments, ct);
    }
}
