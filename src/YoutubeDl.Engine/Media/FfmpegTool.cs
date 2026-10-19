using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Globalization;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Media;

/// <summary>Runs ffmpeg for native media processing and drains child processes on cancellation.</summary>
public static partial class FfmpegTool
{
    public static string RequireProbe(string? ffmpeg, string purpose = "align HLS subtitle timestamps")
    {
        if (string.IsNullOrWhiteSpace(ffmpeg)) throw new FileNotFoundException("The ffmpeg tools are required to " + purpose + ".");
        var probe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ffmpeg))!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(probe)) throw new FileNotFoundException("ffprobe from the ffmpeg tools is required to " + purpose + ".", probe);
        return probe;
    }

    public static async Task<decimal> ReadStartTimeAsync(string ffmpeg, string input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(RequireProbe(ffmpeg)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "format=start_time", "-of", "json", input }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start ffprobe.");
        var output = ReadTailAsync(process.StandardOutput); var error = ReadTailAsync(process.StandardError);
        try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            throw;
        }
        var json = await output.ConfigureAwait(false); var diagnostic = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("Could not read media timing: " + Trim(diagnostic));
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("format", out var format) || !format.TryGetProperty("start_time", out var time) ||
            !decimal.TryParse(time.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            throw new NotSupportedException("The media does not expose a start timestamp for HLS subtitle synchronization.");
        return result;
    }
    public static Task RemuxAsync(string ffmpeg, string input, string output, CancellationToken ct) => RemuxAsync(ffmpeg, input, output, ct, "VideoRemuxer");
    private static async Task RemuxAsync(string ffmpeg, string input, string output, CancellationToken ct, string processingStage)
    {
        var timecode = await PrepareTimecodeAsync(ffmpeg, input, output, ct).ConfigureAwait(false);
        var chapters = await PrepareMovieChaptersAsync(ffmpeg, input, ct).ConfigureAwait(false);
        var legacyTracks = await PrepareLegacyTracksAsync(ffmpeg, input, ct).ConfigureAwait(false);
        var arguments = new List<string> { "-y" };
        PreserveTransportPresentation(arguments,input);
        arguments.AddRange(["-i", input, "-map", "0", "-c", "copy"]);
        MapTimecode(arguments, timecode); MapMovieChapters(arguments, chapters);
        MapLegacyTracks(arguments, legacyTracks);
        if (chapters is not null) SetMovieClock(arguments, output);
        await PreserveMp3BoundaryAsync(ffmpeg, input, output, arguments, ct).ConfigureAwait(false);
        var dolby=await PreserveDolbyMuxAsync(ffmpeg,input,output,arguments,ct).ConfigureAwait(false);
        arguments.Add(output);
        await RunConfiguredAsync(ffmpeg, arguments, ct, [processingStage]).ConfigureAwait(false);
        await VerifyDolbyMuxAsync(ffmpeg,output,dolby,ct).ConfigureAwait(false);
        await VerifyTimecodeAsync(ffmpeg, output, timecode, ct).ConfigureAwait(false);
        await VerifyMovieChaptersAsync(ffmpeg, output, chapters?.Chapters, ct).ConfigureAwait(false);
    }

    // Some transport packagers write decode clocks after reordered pictures' PTS.
    // Retain the declared presentation clock and let the parser derive valid DTS.
    private static void PreserveTransportPresentation(List<string> arguments,string input)
    {if(Path.GetExtension(input).ToLowerInvariant() is ".ts" or ".m2ts")arguments.AddRange(["-fflags","+igndts"]);}

    public static async Task ExtractAudioAsync(
        string ffmpeg, string input, string output, string codec, string quality, CancellationToken ct)
    {
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct, "select the audio track").ConfigureAwait(false);
        var audio = SelectAudio(streams);
        codec = codec.ToLowerInvariant();
        var expected = codec switch { "copy" => audio.Codec, "libmp3lame" => "mp3", "libopus" => "opus", "libvorbis" => "vorbis", _ => codec };
        var plan = new AudioOutputPlan(audio.Index, Path.GetExtension(output).TrimStart('.'), codec, expected, audio.Channels, audio.SampleRate,
            AudioOutputPlan.EncoderQuality(codec, quality, audio.Channels));
        await ExtractAudioAsync(ffmpeg, input, output, plan, ct).ConfigureAwait(false);
    }

    public static async Task ExtractVideoAsync(string ffmpeg, string input, string output, CancellationToken ct)
    {
        var args=new List<string>{"-y","-i",input,"-map","0:v:0","-an","-c:v","copy"};var dolby=await PreserveDolbyMuxAsync(ffmpeg,input,output,args,ct).ConfigureAwait(false);args.Add(output);await RunConfiguredAsync(ffmpeg,args,ct,["VideoConvertor"]).ConfigureAwait(false);await VerifyDolbyMuxAsync(ffmpeg,output,dolby,ct).ConfigureAwait(false);
    }

    public static async Task MergeAsync(
        string ffmpeg, string video, string audio, string output, CancellationToken ct, bool preserveTimestamps = false)
    {
        var arguments = new List<string> { "-y", "-v", "error", "-nostats" };
        if (preserveTimestamps) arguments.Add("-copyts");
        arguments.AddRange(["-i", video, "-i", audio, "-c", "copy", "-map", "0:v:0", "-map", "1:a:0"]);
        if (preserveTimestamps) arguments.AddRange(["-avoid_negative_ts", "disabled"]);
        var dolby=await PreserveDolbyMuxAsync(ffmpeg,video,output,arguments,ct).ConfigureAwait(false);
        arguments.Add(output);
        await RunConfiguredAsync(ffmpeg, arguments, ct, ["Merger"], rejectDiagnostics: true).ConfigureAwait(false);
        await VerifyDolbyMuxAsync(ffmpeg,output,dolby,ct).ConfigureAwait(false);
    }

    public static async Task EmbedAsync(
        string ffmpeg, string input, string output, IReadOnlyList<EmbeddedSubtitle> subtitles, string? thumbnail, IReadOnlyDictionary<string, string>? metadata, CancellationToken ct,
        IReadOnlyList<ChapterMarker>? chapters = null)
    {
        string? chapterPath = null;
        var captionCopies = new List<string>();
        try
        {
            if (Path.GetExtension(output).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v")
            {
                var prepared = new List<EmbeddedSubtitle>();
                foreach (var subtitle in subtitles)
                {
                    var extension = Path.GetExtension(subtitle.Path).ToLowerInvariant();
                    if (extension is ".vtt" or ".srt")
                    {
                        if (new FileInfo(subtitle.Path).Length > MaximumSubtitleBytes) throw new InvalidDataException("The embedding caption exceeds its size limit.");
                        var normalized = SubtitleOverlap.Normalize(await File.ReadAllBytesAsync(subtitle.Path, ct).ConfigureAwait(false), extension, ct);
                        if (normalized is not null)
                        {
                            var path = Path.Combine(Path.GetDirectoryName(output)!, ".caption-overlap-" + Guid.NewGuid().ToString("N") + ".vtt");
                            captionCopies.Add(path); await File.WriteAllBytesAsync(path, normalized, ct).ConfigureAwait(false);
                            prepared.Add(new(path, subtitle.Language)); continue;
                        }
                    }
                    prepared.Add(subtitle);
                }
                subtitles = prepared;
            }
            if (chapters is { Count: > 0 })
            {
                // QuickTime chapter samples have consecutive durations. Represent
                // unlabelled intervals explicitly instead of moving named chapters.
                if (Path.GetExtension(output).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v" or ".m4a" or ".3gp")
                    chapters = ChapterMetadataWriter.WithExplicitGaps(chapters);
                var document = ChapterMetadataWriter.Render(chapters);
                chapterPath = Path.Combine(Path.GetDirectoryName(output)!, ".chapters-" + Guid.NewGuid().ToString("N") + ".ffmetadata");
                await File.WriteAllTextAsync(chapterPath, document, new UTF8Encoding(false), ct).ConfigureAwait(false);
            }
            await EmbedCoreAsync(ffmpeg, input, output, subtitles, thumbnail, metadata, chapterPath, chapters, ct).ConfigureAwait(false);
        }
        finally
        {
            if (chapterPath is not null && File.Exists(chapterPath)) File.Delete(chapterPath);
            foreach (var path in captionCopies) if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task EmbedCoreAsync(string ffmpeg, string input, string output, IReadOnlyList<EmbeddedSubtitle> subtitles,
        string? thumbnail, IReadOnlyDictionary<string, string>? metadata, string? chapterPath, IReadOnlyList<ChapterMarker>? expectedChapters, CancellationToken ct)
    {
        var timecode = await PrepareTimecodeAsync(ffmpeg, input, output, ct).ConfigureAwait(false);
        var inheritedChapters = await PrepareMovieChaptersAsync(ffmpeg, input, ct).ConfigureAwait(false);
        var legacyTracks = await PrepareLegacyTracksAsync(ffmpeg, input, ct).ConfigureAwait(false);
        using var cover = string.IsNullOrWhiteSpace(thumbnail) ? null : await PrepareCoverAsync(ffmpeg, input, thumbnail, output, ct).ConfigureAwait(false);
        var args = new List<string> { "-y" };
        PreserveTransportPresentation(args,input);
        args.AddRange(["-i", input]);
        var next = 1;
        int? thumbnailIndex = null;
        var subtitleIndexes = new List<int>();
        if (cover?.Mode == CoverMode.Picture)
        {
            thumbnailIndex = next++;
            args.AddRange(["-i", cover.ImagePath]);
        }

        foreach (var subtitle in subtitles)
        {
            subtitleIndexes.Add(next++);
            args.AddRange(["-i", subtitle.Path]);
        }

        int? chapterIndex = null;
        if (chapterPath is not null)
        {
            chapterIndex = next;
            args.AddRange(["-f", "ffmetadata", "-i", chapterPath]);
        }
        args.AddRange(["-map", "0", "-c", "copy"]);
        MapTimecode(args, timecode);
        MapMovieChapters(args, inheritedChapters);
        MapLegacyTracks(args, legacyTracks);
        if (cover is not null) foreach (var stream in cover.RemovedStreams) args.AddRange(["-map", "-0:" + stream]);
        if (chapterIndex is not null)
        {
            args.AddRange(["-map_metadata", "0", "-map_chapters", chapterIndex.Value.ToString()]);
        }
        if (chapterIndex is not null || inheritedChapters is not null) SetMovieClock(args, output);
        if (subtitles.Count > 0) args.AddRange(["-map", "-0:s?"]);
        if (thumbnailIndex is not null)
        {
            var picture = cover!.VideoStreams.ToString(CultureInfo.InvariantCulture);
            args.AddRange(["-map", thumbnailIndex.Value + ":v:0", "-disposition:v:" + picture, "attached_pic",
                "-metadata:s:v:" + picture, "title=Album cover", "-metadata:s:v:" + picture, "comment=Cover (front)"]);
            if (Path.GetExtension(output).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-id3v2_version", "3"]);
        }
        for (var index = 0; index < subtitleIndexes.Count; index++)
            args.AddRange(["-map", subtitleIndexes[index] + ":s:0", "-metadata:s:s:" + index, "language=" + SubtitleLanguage(subtitles[index].Language)]);
        if (subtitles.Count > 0)
            args.AddRange(["-c:s", Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".mp4" or ".mov" or ".m4v" => "mov_text", ".mkv" => "srt", ".webm" => "webvtt",
                var extension => throw new NotSupportedException("Native caption embedding is pending for container: " + extension)
            }]);
        if (metadata is not null)
        {
            if (Path.GetExtension(output).Equals(".aac", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-write_id3v2", "1"]);
            foreach (var item in metadata)
                args.AddRange(["-metadata", item.Key + "=" + item.Value]);
        }

        if (cover?.Mode == CoverMode.Attachment)
        {
            var index = cover.RetainedStreams - legacyTracks.Length - (inheritedChapters?.Streams.Length ?? 0) - (subtitles.Count > 0 ? cover.SubtitleStreams : 0) + subtitles.Count;
            args.AddRange(["-attach", cover.ImagePath, "-metadata:s:" + index, "mimetype=" + cover.MimeType,
                "-metadata:s:" + index, "filename=cover" + Path.GetExtension(cover.ImagePath)]);
        }
        else if (cover?.Mode == CoverMode.VorbisComment)
            args.AddRange(["-/metadata:s:a", cover.CommentPath!, "-metadata:s:a", "COVERART=", "-metadata:s:a", "COVERARTMIME="]);

        await PreserveMp3BoundaryAsync(ffmpeg, input, output, args, ct).ConfigureAwait(false);
        var dolby=await PreserveDolbyMuxAsync(ffmpeg,input,output,args,ct).ConfigureAwait(false);
        args.Add(output);
        await RunConfiguredAsync(ffmpeg, args, ct, (metadata is not null || chapterPath is not null ? new[] { "Metadata" } : Array.Empty<string>()).Concat(subtitles.Count > 0 ? ["EmbedSubtitle"] : Array.Empty<string>()).Concat(cover is not null ? ["EmbedThumbnail"] : Array.Empty<string>()).ToArray()).ConfigureAwait(false);
        await VerifyDolbyMuxAsync(ffmpeg,output,dolby,ct).ConfigureAwait(false);
        await VerifyTimecodeAsync(ffmpeg, output, timecode, ct).ConfigureAwait(false);
        await VerifyMovieChaptersAsync(ffmpeg, output, expectedChapters is { Count: > 0 } ? expectedChapters : inheritedChapters?.Chapters, ct).ConfigureAwait(false);
        if (cover is not null) await VerifyCoverAsync(ffmpeg, output, cover, ct).ConfigureAwait(false);
    }

    private static string SubtitleLanguage(string language)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(language.Split('.')[0]).ThreeLetterISOLanguageName; }
        catch (System.Globalization.CultureNotFoundException) { return "und"; }
    }

    public static async Task<bool> IsAvailableAsync(string ffmpeg, CancellationToken ct)
    {
        try
        {
            var result = await RunAsync(ffmpeg, ["-version"], ct, ignoreFailure: true).ConfigureAwait(false);
            return result == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunAsync(
        string ffmpeg, IReadOnlyList<string> args, CancellationToken ct, bool ignoreFailure = false, bool rejectDiagnostics = false, Action<string>? inspectOutput = null)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
            throw new FileNotFoundException("ffmpeg was not found. Install it from About before merging or extracting audio.");

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-nostdin");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Could not start ffmpeg.");
        using var lifetime=ProcessLifetime.Attach(process);
        await YoutubeDl.Engine.Download.PublicationFaults.AtAsync("processor-started").ConfigureAwait(false);
        var errorTask = ReadTailAsync(process.StandardError);
        var outputTask = ReadTailAsync(process.StandardOutput);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.WhenAll(errorTask, outputTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            throw;
        }
        var error = await errorTask.ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        if (process.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException("ffmpeg failed: " + Trim(error));
        if (rejectDiagnostics && !string.IsNullOrWhiteSpace(error))
            throw new InvalidDataException("ffmpeg reported media processing errors: " + Trim(error));
        inspectOutput?.Invoke(output);
        return process.ExitCode;
    }

    private static async Task<string> ReadTailAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) return tail.ToString();
            tail.Append(buffer, 0, count);
            if (tail.Length > 8000) tail.Remove(0, tail.Length - 8000);
        }
    }

    private static string Trim(string value)
    {
        var text = value.Trim();
        return text.Length <= 500 ? text : text[^500..];
    }
}

