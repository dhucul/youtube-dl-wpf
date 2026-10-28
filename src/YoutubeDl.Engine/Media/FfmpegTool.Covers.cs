using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace YoutubeDl.Engine.Media;

public static partial class FfmpegTool
{
    private enum CoverMode { Picture, Attachment, VorbisComment }
    private sealed class Cover : IDisposable
    {
        public required CoverMode Mode { get; init; }
        public required string ImagePath { get; set; }
        public required string MimeType { get; set; }
        public string? CommentPath { get; set; }
        public List<string> TemporaryFiles { get; } = [];
        public required int[] RemovedStreams { get; init; }
        public int RetainedStreams { get; init; }
        public int VideoStreams { get; init; }
        public int AudioStreams { get; init; }
        public int SubtitleStreams { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public void Dispose()
        {
            foreach (var file in TemporaryFiles)
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private sealed record ProbedStream(int Index, string Kind, string Codec, int Width, int Height, bool Attached, string? Filename, string? MimeType,
        bool Default, int Channels, int SampleRate, string SampleFormat, int BitsPerSample, string Container, string TimeBase, decimal? StartTime, string ChannelLayout, string ExtraDataHash,
        string CodecTag, string? Timecode, string FrameCount, decimal? DurationSeconds, double Rotation, uint? TrackId, string PixelFormat="",string ColorTransfer="",string ColorPrimaries="",string ColorSpace="",string SampleAspect="",string ColorRange="",bool SimpleDisplay=true,bool AdvancedHdr=false,bool StaticHdr=false,TransitionDolbyConfiguration? Dolby=null,int BFrames=0,string? DisplayMatrix=null,string FrameRate="")
    {
        public bool IsCover => Attached || Kind == "attachment" && MimeType is "image/jpeg" or "image/png" && Filename is { } filename &&
            new[] { "cover", "cover_land", "small_cover", "small_cover_land" }.Contains(Path.GetFileNameWithoutExtension(filename), StringComparer.OrdinalIgnoreCase);
    }

    public static void ValidateCoverContainer(string extension)
    {
        _ = Mode(extension);
    }
    private static CoverMode Mode(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "mp3" or "flac" or "mp4" or "m4a" or "m4b" or "m4v" => CoverMode.Picture,
        "mkv" or "mka" => CoverMode.Attachment,
        "ogg" or "oga" or "opus" => CoverMode.VorbisComment,
        _ => throw new NotSupportedException("Cover embedding is not supported for " + extension + ". Choose MP4/Matroska for video or MP3/M4A/FLAC/Ogg/Opus for audio.")
    };

    private static async Task<Cover> PrepareCoverAsync(string ffmpeg, string input, string image, string output, CancellationToken ct)
    {
        var mode = Mode(Path.GetExtension(output));
        var size = new FileInfo(image).Length;
        if (size is <= 0 or > 8 * 1024 * 1024) throw new InvalidDataException("The cover image exceeds the supported size limit.");
        var pictures = await ProbeStreamsAsync(ffmpeg, image, ct).ConfigureAwait(false);
        if (pictures.Count != 1 || pictures[0] is not { Kind: "video", Codec: "mjpeg" or "png" or "webp" or "gif" or "bmp" or "av1", Width: > 0, Height: > 0 } picture ||
            (long)picture.Width * picture.Height > 16 * 1024 * 1024)
            throw new InvalidDataException("The cover must be a supported image within the 16-megapixel limit.");
        var streams = await ProbeStreamsAsync(ffmpeg, input, ct).ConfigureAwait(false);
        if (mode == CoverMode.VorbisComment && streams.All(stream => stream.Kind != "audio"))
            throw new NotSupportedException("Ogg cover embedding requires an audio stream.");
        var removed = streams.Where(stream => stream.IsCover).Select(stream => stream.Index).ToArray();
        var cover = new Cover { Mode = mode, ImagePath = image, MimeType = picture.Codec == "mjpeg" ? "image/jpeg" : "image/png",
            RemovedStreams = removed, RetainedStreams = streams.Count - removed.Length,
            VideoStreams = streams.Count(stream => stream.Kind == "video" && !stream.IsCover), AudioStreams = streams.Count(stream => stream.Kind == "audio"),
            SubtitleStreams = streams.Count(stream => stream.Kind == "subtitle"), Width = picture.Width, Height = picture.Height };
        try
        {
            var decode = new List<string> { "-y", "-v", "error", "-max_alloc", "134217728", "-xerror", "-err_detect", "explode", "-i", image, "-map", "0:v:0", "-frames:v", "1" };
            if (picture.Codec is not ("mjpeg" or "png"))
            {
                cover.ImagePath = Path.Combine(Path.GetDirectoryName(output)!, ".cover-" + Guid.NewGuid().ToString("N") + ".png");
                cover.TemporaryFiles.Add(cover.ImagePath);
                decode.AddRange(["-c:v", "png", "-update", "1", cover.ImagePath]);
            }
            else decode.AddRange(["-f", "null", "-"]);
            using (var decodeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                decodeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                try { await RunAsync(ffmpeg, decode, decodeTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Cover image decoding timed out."); }
            }
            if (new FileInfo(cover.ImagePath).Length is <= 0 or > 8 * 1024 * 1024) throw new InvalidDataException("The converted cover exceeds the supported size limit.");
            if (mode == CoverMode.VorbisComment)
            {
                var data = await File.ReadAllBytesAsync(cover.ImagePath, ct).ConfigureAwait(false);
                var block = EncodePictureBlock(data, cover.MimeType);
                cover.CommentPath = Path.Combine(Path.GetDirectoryName(output)!, ".cover-" + Guid.NewGuid().ToString("N") + ".metadata");
                cover.TemporaryFiles.Add(cover.CommentPath);
                await File.WriteAllTextAsync(cover.CommentPath, "METADATA_BLOCK_PICTURE=" + Convert.ToBase64String(block), new UTF8Encoding(false), ct).ConfigureAwait(false);
            }
            return cover;
        }
        catch { cover.Dispose(); throw; }
    }

    private static async Task VerifyCoverAsync(string ffmpeg, string output, Cover cover, CancellationToken ct)
    {
        var streams = await ProbeStreamsAsync(ffmpeg, output, ct).ConfigureAwait(false);
        var pictures = streams.Where(stream => stream.IsCover).ToArray();
        if (pictures.Length != 1 || pictures[0].Width != cover.Width || pictures[0].Height != cover.Height ||
            pictures[0].Codec != (cover.MimeType == "image/jpeg" ? "mjpeg" : "png") ||
            streams.Count(stream => stream.Kind == "video" && !stream.IsCover) != cover.VideoStreams || streams.Count(stream => stream.Kind == "audio") != cover.AudioStreams)
            throw new InvalidDataException("The output did not retain the requested cover image and media tracks.");
    }

    // Xiph picture block: network-order integers and UTF-8 fields, without a FLAC metadata-block header.
    private static byte[] EncodePictureBlock(byte[] image, string mimeType)
    {
        using var output = new MemoryStream();
        Number(3); Text(mimeType); Text("Cover (front)");
        // Xiph permits all informational geometry fields to be zero; the image supplies authoritative dimensions.
        Number(0); Number(0); Number(0); Number(0); Number((uint)image.Length); output.Write(image);
        return output.ToArray();
        void Number(uint value) { Span<byte> field = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(field, value); output.Write(field); }
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); Number((uint)bytes.Length); output.Write(bytes); }
    }

    private static async Task<IReadOnlyList<ProbedStream>> ProbeStreamsAsync(string ffmpeg, string input, CancellationToken ct, string purpose = "inspect embedded cover images", string? inputDemuxer = null, bool countFrames = false, bool localMediaOnly = false, Action<JsonElement>? inspectDocument = null)
    {
        var start = new ProcessStartInfo(RequireProbe(ffmpeg, purpose))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        if (localMediaOnly)
            foreach (var argument in new[] { "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov,matroska,webm,mpegts,ogg,flv,avi,mp3,aac,flac,wav,asf,mpeg,mpegvideo" }) start.ArgumentList.Add(argument);
        if (inputDemuxer is not null)
        {
            foreach (var argument in new[] { "-protocol_whitelist", "file,pipe", "-f", inputDemuxer }) start.ArgumentList.Add(argument);
            if (inputDemuxer == "mov") foreach (var argument in new[] { "-enable_drefs", "0", "-use_absolute_path", "0" }) start.ArgumentList.Add(argument);
        }
        if (countFrames) start.ArgumentList.Add("-count_frames");
        if (inspectDocument is not null) start.ArgumentList.Add("-show_chapters");
        foreach (var argument in new[] { "-v", "error", "-max_alloc", "134217728", "-show_data_hash", "sha256", "-show_entries", "format=format_name:stream=avg_frame_rate,has_b_frames,index,id,codec_type,codec_name,codec_tag_string,nb_frames,nb_read_frames,duration,width,height,pix_fmt,color_transfer,color_primaries,color_space,color_range,sample_aspect_ratio,channels,channel_layout,sample_rate,sample_fmt,bits_per_raw_sample,bits_per_sample,time_base,start_time,extradata_hash:stream_disposition=attached_pic,default:stream_tags=filename,mimetype,timecode:stream_side_data=rotation,displaymatrix,side_data_type,dv_profile,dv_bl_signal_compatibility_id,rpu_present_flag,bl_present_flag,el_present_flag,dv_version_major,dv_version_minor,dv_md_compression:chapter=start_time,end_time:chapter_tags=title", "-of", "json", input })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Could not start ffprobe.");
        var output = ReadProbeAsync(process.StandardOutput); var error = ReadTailAsync(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); if (!countFrames) timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await ProcessLifetime.KillAndDrainAsync(process, null, output, error).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Media inspection timed out while trying to " + purpose + ".");
        }
        var json = await output.ConfigureAwait(false); var diagnostic = await error.ConfigureAwait(false);
        if (process.ExitCode != 0 || countFrames && !string.IsNullOrWhiteSpace(diagnostic)) throw new InvalidDataException("Could not inspect the media: " + Trim(diagnostic));
        using var document = JsonDocument.Parse(json);
        inspectDocument?.Invoke(document.RootElement);
        var items = document.RootElement.GetProperty("streams");
        var container = document.RootElement.TryGetProperty("format", out var format) ? Text(format, "format_name") ?? "" : "";
        if (items.GetArrayLength() is 0 or > 256) throw new InvalidDataException("The media has an unsupported stream count.");
        return items.EnumerateArray().Select(stream => new ProbedStream(stream.GetProperty("index").GetInt32(), Text(stream, "codec_type") ?? "", Text(stream, "codec_name") ?? "",
            Integer(stream, "width"), Integer(stream, "height"), stream.TryGetProperty("disposition", out var disposition) && Integer(disposition, "attached_pic") == 1,
            stream.TryGetProperty("tags", out var tags) ? Text(tags, "filename") : null, tags.ValueKind == JsonValueKind.Object ? Text(tags, "mimetype") : null,
            disposition.ValueKind == JsonValueKind.Object && Integer(disposition, "default") == 1, Integer(stream, "channels"), Integer(stream, "sample_rate"),
            Text(stream, "sample_fmt") ?? "", Math.Max(Integer(stream, "bits_per_raw_sample"), Integer(stream, "bits_per_sample")), container, Text(stream, "time_base") ?? "",
            decimal.TryParse(Text(stream, "start_time"), System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var startTime) ? startTime : null,
            Text(stream, "channel_layout") ?? "", Text(stream, "extradata_hash") ?? "", Text(stream, "codec_tag_string") ?? "",
            tags.ValueKind == JsonValueKind.Object ? Text(tags, "timecode") : null, Text(stream, countFrames ? "nb_read_frames" : "nb_frames") ?? "",
            decimal.TryParse(Text(stream, "duration"), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var duration) ? duration : null, Rotation(stream), TrackId(Text(stream, "id")),Text(stream,"pix_fmt")??"",Text(stream,"color_transfer")??"",Text(stream,"color_primaries")??"",Text(stream,"color_space")??"",Text(stream,"sample_aspect_ratio")??"",Text(stream,"color_range")??"",TransitionConversionPlan.SimpleMatrix(SideText(stream,"displaymatrix")),AdvancedHdr(stream),StaticHdr(stream),Dolby(stream),Integer(stream,"has_b_frames"),SideText(stream,"displaymatrix"),Text(stream,"avg_frame_rate")??"")).ToArray();

        static uint? TrackId(string? value) => value is not null && uint.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

        static double Rotation(JsonElement stream)
        {
            if (stream.TryGetProperty("side_data_list", out var list))
                foreach (var value in list.EnumerateArray()) if (value.TryGetProperty("rotation", out var rotation)) return rotation.GetDouble();
            return 0;
        }

        static string? SideText(JsonElement stream,string key)
        {
            if(stream.TryGetProperty("side_data_list",out var list))foreach(var item in list.EnumerateArray())if(item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String)return value.GetString();
            return null;
        }
        static TransitionDolbyConfiguration? Dolby(JsonElement stream)
        {
            if(stream.TryGetProperty("side_data_list",out var list))foreach(var item in list.EnumerateArray())if(Text(item,"side_data_type")=="DOVI configuration record")return new(Integer(item,"dv_profile"),Integer(item,"dv_bl_signal_compatibility_id"),Integer(item,"rpu_present_flag"),Integer(item,"bl_present_flag"),Integer(item,"el_present_flag"),Integer(item,"dv_version_major"),Integer(item,"dv_version_minor"),Text(item,"dv_md_compression")??"");
            return null;
        }
        static bool StaticHdr(JsonElement stream)=>stream.TryGetProperty("side_data_list",out var list)&&list.EnumerateArray().Any(item=>item.TryGetProperty("side_data_type",out var type)&&type.GetString() is { } text&&(text.Contains("Mastering display",StringComparison.OrdinalIgnoreCase)||text.Contains("Content light",StringComparison.OrdinalIgnoreCase)));
        static bool AdvancedHdr(JsonElement stream)=>stream.TryGetProperty("side_data_list",out var list)&&list.EnumerateArray().Any(item=>item.TryGetProperty("side_data_type",out var type)&&type.GetString() is { } text&&(text.Contains("DOVI",StringComparison.OrdinalIgnoreCase)||text.Contains("HDR Dynamic",StringComparison.OrdinalIgnoreCase)||text.Contains("HDR10+",StringComparison.OrdinalIgnoreCase)));
        static int Integer(JsonElement value, string key)
        {
            if (!value.TryGetProperty(key, out var property)) return 0;
            if (property.ValueKind == JsonValueKind.Number) return property.TryGetInt32(out var number) ? number : 0;
            return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }
        static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out var property) ? property.GetString() : null;
    }
    private static async Task<string> ReadProbeAsync(StreamReader reader)
    {
        const int maximum = 128 * 1024;
        var text = new StringBuilder(); var buffer = new char[4096]; var excessive = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) break;
            if (text.Length + count > maximum) excessive = true;
            else if (!excessive) text.Append(buffer, 0, count);
        }
        if (excessive) throw new InvalidDataException("Media inspection exceeded the supported metadata limit.");
        return text.ToString();
    }
}


