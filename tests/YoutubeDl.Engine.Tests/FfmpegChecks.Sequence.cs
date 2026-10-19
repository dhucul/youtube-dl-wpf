using System.Globalization;
using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;

internal static partial class FfmpegChecks
{
    private static async Task BilibiliSequence(string ffmpeg, string mode)
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        var extension = mode == "flv" ? "flv" : "mp4";
        var sourcePaths = new List<string>(); var sourceBytes = new List<byte[]>(); var milliseconds = new List<int>();
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        for (var index = 0; index < 2; index++)
        {
            var source = Path.Combine(files.Directory, "source-" + index + "." + extension);
            var size = index == 1 && mode == "incompatible" ? "160x96" : "128x96";
            var args = new List<string> { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=" + (index == 0 ? "red" : "blue") + ":s=" + size + ":r=24:d=1",
                "-f", "lavfi", "-i", "sine=frequency=" + (index == 0 ? "500" : "900") + ":sample_rate=48000:duration=1", "-c:v", "libx264", "-g", "24", "-pix_fmt", "yuv420p", "-c:a", "aac" };
            if (extension == "mp4") args.AddRange(["-movflags", "frag_keyframe+empty_moov+default_base_moof"]);
            args.Add(source); await Run(ffmpeg, args);
            using var info = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "json", source]));
            milliseconds.Add((int)Math.Round(decimal.Parse(info.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture) * 1000));
            sourcePaths.Add(source); sourceBytes.Add(await File.ReadAllBytesAsync(source));
        }
        if (mode == "corrupt")
        {
            var marker = sourceBytes[1].AsSpan().IndexOf("mdat"u8);
            if (marker < 0) throw new InvalidOperationException("No generated media payload.");
            sourceBytes[1][marker + 4] = 0x7f; sourceBytes[1][marker + 5] = 0xff; sourceBytes[1][marker + 6] = 0xff; sourceBytes[1][marker + 7] = 0xff;
        }
        var playback = BilibiliSequenceChecks.Playback(extension);
        for (var index = 0; index < 2; index++)
        {
            playback["data"]!["durl"]![index]!["size"] = sourceBytes[index].Length + (mode == "size" && index == 1 ? 1 : 0);
            playback["data"]!["durl"]![index]!["length"] = milliseconds[index];
        }
        playback["data"]!["timelength"] = milliseconds.Sum();
        var calls = new List<Uri>(); var completed = new List<string>(); var attempts = new int[2];
        var transfers = new SequenceTransferState();
        var engine = BilibiliChecks.Engine(calls, playback: _ => playback, mediaResponse: request =>
        {
            var index = request.RequestUri!.AbsolutePath.Contains("part-1") ? 0 : 1; var attempt = Interlocked.Increment(ref attempts[index]);
            if (mode == "missing" && index == 1 || mode == "retry" && index == 0 && attempt == 1)
                return new(mode == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable) { Content = new StringContent("unavailable") };
            if (mode == "cancel" && index == 1) stop.Cancel();
            if (mode == "parallel")
            {
                var content = new StreamContent(new SequenceStream(sourceBytes[index], index, transfers)); content.Headers.ContentLength = sourceBytes[index].Length;
                return new(HttpStatusCode.OK) { Content = content };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(sourceBytes[index]) };
        });
        var archive = Path.Combine(files.Directory, "archive.txt");
        var request = BilibiliChecks.Request(files.Directory) with { FfmpegPath = ffmpeg, KeepOriginal = mode is "mp4" or "flv", WriteInfoJson = true,
            DownloadArchiveFile = archive, ConcurrentFragments = 2, FragmentRetries = mode == "retry" ? 1 : 0,
            VideoOnly = mode == "video", AudioOnly = mode == "audio", AudioFormat = "best" };
        if (mode is "corrupt" or "missing" or "size" or "incompatible" or "cancel")
        {
            var prior = Path.Combine(files.Directory, "bilibili.mp4"); await File.WriteAllTextAsync(prior, "previous complete video");
            try
            {
                await engine.DownloadAsync(request, null, null, stop.Token, completed.Add);
                throw new InvalidOperationException("An invalid ordered media sequence was published.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException || ex is OperationCanceledException && stop.IsCancellationRequested || ex is InvalidOperationException && ex.Message.StartsWith("ffmpeg failed:")) { }
            if (File.Exists(archive) || completed.Count != 0 || await File.ReadAllTextAsync(prior) != "previous complete video" || Directory.GetFiles(files.Directory, "bilibili.*").Length != 1)
                throw new InvalidOperationException("A failed ordered download changed prior files, archive or callbacks.");
            return;
        }
        var progress = new SequenceProgress();
        var result = await engine.DownloadAsync(request, progress, null, default, completed.Add);
        if (!result.Succeeded || result.OutputPath is null || completed.Count != result.OutputPaths.Count ||
            result.OutputPaths.Count != (request.KeepOriginal ? 3 : 1) || attempts[0] != (mode == "retry" ? 2 : 1) || attempts[1] != 1)
            throw new InvalidOperationException("Ordered download missed a chunk, retry, retained original or completion callback.");
        if (progress.Last?.DownloadedBytes != sourceBytes.Sum(bytes => (long)bytes.Length) || progress.Last.TotalBytes != progress.Last.DownloadedBytes)
            throw new InvalidOperationException("Ordered download progress did not cover the complete source set.");
        if (mode == "parallel" && (transfers.Peak != 2 || transfers.Active != 0 || !transfers.Completed.SequenceEqual(new[] { 1, 0 })))
            throw new InvalidOperationException("Ordered files did not overlap, finish out of order, and drain before publication.");
        if (request.KeepOriginal && sourceBytes.Any(bytes => !result.OutputPaths.Any(path => File.ReadAllBytes(path).SequenceEqual(bytes))))
            throw new InvalidOperationException("An ordered source file was not retained exactly.");
        var expected = new Dictionary<string, long>();
        foreach (var source in sourcePaths)
            foreach (var pair in await FrameCounts(source)) expected[pair.Key] = expected.GetValueOrDefault(pair.Key) + pair.Value;
        var actual = await FrameCounts(result.OutputPath);
        if (mode == "audio") expected.Remove("video");
        if (mode == "video") expected.Remove("audio");
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var count) || count != pair.Value))
            throw new InvalidOperationException("Joining or post-processing lost source frames: " + JsonSerializer.Serialize(new { expected, actual }));
        if (mode != "audio")
        {
            var before = new List<string>(); foreach (var source in sourcePaths) before.AddRange(await VideoHashes(source));
            if (!before.SequenceEqual(await VideoHashes(result.OutputPath))) throw new InvalidOperationException("Joining changed source frame order or decoded pixels.");
        }
        await Run(ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", result.OutputPath, "-map", "0", "-fps_mode:v", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-"]);
        if ((await File.ReadAllTextAsync(archive)).Trim() != "bilibili " + BilibiliChecks.Bvid + "_1445374") throw new InvalidOperationException("The sequence archive identity changed.");
        async Task<Dictionary<string, long>> FrameCounts(string path)
        {
            using var json = JsonDocument.Parse(await Run(ffprobe, ["-v", "error", "-count_frames", "-show_entries", "stream=codec_type,nb_read_frames", "-of", "json", path]));
            return json.RootElement.GetProperty("streams").EnumerateArray().ToDictionary(stream => stream.GetProperty("codec_type").GetString()!, stream => long.Parse(stream.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture));
        }
        async Task<string[]> VideoHashes(string path)
        {
            var text = await Run(ffmpeg, ["-nostdin", "-v", "error", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "framemd5", "-"]);
            return text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim()).ToArray();
        }
    }
    private sealed class SequenceProgress : IProgress<DownloadProgress>
    { public DownloadProgress? Last { get; private set; } public void Report(DownloadProgress value) => Last = value; }
    private sealed class SequenceTransferState
    {
        internal int Active; internal int Peak; internal List<int> Completed { get; } = [];
        internal void Start() { lock (this) { Active++; Peak = Math.Max(Peak, Active); } }
        internal void Finish(int index, bool complete) { lock (this) { Active--; if (complete) Completed.Add(index); } }
    }
    private sealed class SequenceStream(byte[] bytes, int index, SequenceTransferState state) : Stream
    {
        private readonly MemoryStream _source = new(bytes); private bool _started; private bool _finished;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => bytes.Length; public override long Position { get => _source.Position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_started) { _started = true; state.Start(); await Task.Delay(index == 0 ? 150 : 10, ct); }
            var count = await _source.ReadAsync(buffer, ct);
            if (count == 0 && !_finished) { _finished = true; state.Finish(index, true); }
            return count;
        }
        protected override void Dispose(bool disposing) { if (_started && !_finished) { _finished = true; state.Finish(index, false); } _source.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
