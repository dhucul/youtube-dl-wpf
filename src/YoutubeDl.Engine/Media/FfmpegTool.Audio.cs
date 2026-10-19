using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    internal static async Task RequireAudioTrackAsync(string ffmpeg, string input, CancellationToken ct)
    {
        _ = SelectAudio(await ProbeStreamsAsync(ffmpeg, input, ct, "check the selected audio track", localMediaOnly: true).ConfigureAwait(false));
    }

    internal static async Task<AudioOutputPlan> PlanAudioAsync(string ffmpeg, string input, string format, string quality, CancellationToken ct,bool localMediaOnly=false)
    {
        AudioOutputPlan.Validate(format, quality);
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "choose an audio output format",localMediaOnly:localMediaOnly).ConfigureAwait(false);
        var audio = SelectAudio(streams);
        if (string.IsNullOrWhiteSpace(audio.Codec)) throw new InvalidDataException("The audio codec could not be identified.");
        var requested = AudioOutputPlan.Normalize(format);
        var automatic = requested is "best" or "copy";
        var extension = automatic ? AudioOutputPlan.AutomaticExtension(audio.Codec,audio.Container,requested) : AudioOutputPlan.ExtensionFor(requested)!;
        var encoder = automatic ? "copy" : requested switch
        {
            "mp3" => "libmp3lame", "aac" or "m4a" => "aac", "opus" => "libopus", "vorbis" or "ogg" => "libvorbis",
            "flac" => "flac", "alac" => "alac", "wav" => WaveCodec(audio), _ => throw new ArgumentException("Unsupported audio format.")
        };
        var expectedCodec = encoder switch { "copy" => audio.Codec, "libmp3lame" => "mp3", "libopus" => "opus", "libvorbis" => "vorbis", _ => encoder };
        return new(audio.Index, extension, encoder, expectedCodec, audio.Channels, audio.SampleRate,
            AudioOutputPlan.EncoderQuality(encoder, quality, audio.Channels));
    }

    internal static async Task ExtractAudioAsync(string ffmpeg, string input, string output, AudioOutputPlan plan, CancellationToken ct)
    {
        var args = new List<string> { "-y", "-i", input, "-map", "0:" + plan.StreamIndex, "-vn", "-sn", "-dn", "-map_metadata", "0", "-c:a", plan.Encoder };
        // Vorbis uses variable decoded block sizes; source packet clocks are not a new encoder's sample clock.
        if (plan.Encoder == "libvorbis") args.AddRange(["-af", "asetpts=N/SR/TB"]);
        args.AddRange(plan.QualityArguments);
        if (plan.Encoder == "copy") await PreserveMp3BoundaryAsync(ffmpeg, input, output, args, ct).ConfigureAwait(false);
        args.Add(output);
        await RunConfiguredAsync(ffmpeg, args, ct, ["ExtractAudio"]).ConfigureAwait(false);
        var streams = await ProbeStreamsAsync(ffmpeg, output, ct, "verify extracted audio").ConfigureAwait(false);
        if (streams.Count != 1 || streams[0].Kind != "audio" || streams[0].Codec != plan.ExpectedCodec ||
            plan.Encoder == "copy" && (streams[0].Channels != plan.Channels || streams[0].SampleRate != plan.SampleRate))
            throw new InvalidDataException("The extracted file did not retain the requested audio format and track.");
        var demuxer = plan.Extension switch { "m4a" => "mov", "ogg" or "opus" => "ogg", "mka" => "matroska", var extension => extension };
        await VerifyDownloadedMediaAsync(ffmpeg,output,new MediaCandidate { Url = new Uri(Path.GetFullPath(output)), Kind = MediaKind.Progressive,
            HasAudio = true, HasVideo = false, VerificationDemuxer = demuxer },ct).ConfigureAwait(false);
    }
    private static ProbedStream SelectAudio(IReadOnlyList<ProbedStream> streams) => streams.Where(stream => stream.Kind == "audio").OrderByDescending(stream => stream.Default)
        .ThenByDescending(stream => stream.Channels).ThenBy(stream => stream.Index).FirstOrDefault()
        ?? throw new InvalidOperationException("The downloaded file has no audio track to extract.");
    private static string WaveCodec(ProbedStream stream)
    {
        if (stream.SampleFormat.StartsWith("dbl", StringComparison.Ordinal)) return "pcm_f64le";
        if (stream.SampleFormat.StartsWith("flt", StringComparison.Ordinal)) return "pcm_f32le";
        if (stream.BitsPerSample > 24) return "pcm_s32le";
        if (stream.BitsPerSample > 16) return "pcm_s24le";
        return stream.SampleFormat.StartsWith("s32", StringComparison.Ordinal) ? "pcm_s32le" : stream.SampleFormat == "u8" ? "pcm_u8" : "pcm_s16le";
    }
}
