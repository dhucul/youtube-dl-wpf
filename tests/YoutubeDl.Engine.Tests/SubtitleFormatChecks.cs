using System.Diagnostics;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Media;

internal static class SubtitleFormatChecks
{
    internal const string Vtt = "WEBVTT\n\nfirst\n00:00:00.120 --> 00:00:00.870\nBonjour café & punctuation.\nSecond line.\n\nlast\n00:00:01.230 --> 00:00:02.340\nFinal 日本語.\n";
    internal const string Srt = "1\r\n00:00:00,120 --> 00:00:00,870\r\nBonjour café & punctuation.\r\nSecond line.\r\n\r\n2\r\n00:00:01,230 --> 00:00:02,340\r\nFinal 日本語.\r\n";
    internal const string WorkerVariable = "YOUTUBEDL_NATIVE_CAPTION_WORKER";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Subtitle format preferences choose offered alternatives after language and provenance selection", () => Sync(Selection)),
        ("Subtitle format preference fetches only the selected original document", PreferredSaving),
        ("Subtitle same-format conversion saves exact original bytes without ffmpeg", SameFormat),
        ("Subtitle conversion dependencies fail before media transfer", MissingConverter),
        ("Subtitle unsupported preferences and conversion targets fail explicitly", InvalidOptions),
        ("Subtitle all-language arguments retain manual and automatic selection and override order", () => Sync(Arguments)),
        ("Subtitle conversion output limits kill and drain the converter", () => ProcessBoundary("overflow")),
        ("Subtitle conversion cancellation kills and drains the converter", () => ProcessBoundary("cancel")),
        ("Subtitle failed or empty conversion preserves published captions", ConversionFailure)
    ];

    internal static SubtitleTrack Track(string language, string extension, bool automatic = false) => new()
    { Language = language, Extension = extension, Automatic = automatic, Url = new("https://media.example/" + (automatic ? "auto-" : "") + language + "." + extension) };

    private static void Selection()
    {
        var tracks = new[] { Track("en", "vtt"), Track("en", "srt"), Track("en", "ass", true), Track("fr", "ass"), Track("es", "vtt", true) };
        var request = CaptionChecks.Request(Path.GetTempPath()) with { WriteSubs = true, WriteAutoSubs = true, SubFormat = "ass/srt/best", SubLanguages = "en" };
        Assert(SubtitleSelection.Select(tracks, request).Single() == tracks[1], "Format selection replaced authored subtitles with automatic captions or ignored preference order.");
        Assert(SubtitleSelection.Select(tracks, request with { SubFormat = "best" }).Single() == tracks[0], "Best did not retain the provider's preferred supported original.");
        Assert(SubtitleSelection.Select(tracks, request with { WriteSubs = false }).Single() == tracks[2], "Automatic-only format selection chose authored text.");
        Assert(SubtitleSelection.Select(tracks, request with { SubLanguages = "all,-en", SubFormat = "ass/vtt" }).Select(track => track.Language).SequenceEqual(["fr", "es"]),
            "Language exclusions did not precede format preference evaluation.");
        Assert(SubtitleSelection.Select(tracks, request with { SubFormat = " SRT / best " }).Single() == tracks[1], "Case or whitespace changed an exact format preference.");
    }

    private static async Task PreferredSaving()
    {
        using var files = new Files(); var calls = new List<string>();
        var engine = CaptionChecks.CreateEngine("media"u8.ToArray(), calls, _ => new StringContent(Srt),
            [Track("en", "vtt"), Track("en", "srt"), Track("fr", "ass")]);
        var result = await engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SubFormat = "srt/vtt", SubLanguages = "en", SkipDownload = true }, null, null, default);
        Assert(result.Succeeded && result.OutputPaths.Count == 0 && calls.SequenceEqual(["en.srt"]) &&
            File.ReadAllText(Path.Combine(files.Directory, "result.en.srt")) == Srt && !File.Exists(Path.Combine(files.Directory, "result.en.vtt")),
            "Preference caused extra downloads, renamed source bytes, or lost standalone output.");
    }

    private static async Task SameFormat()
    {
        using var files = new Files(); var source = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Vtt)).ToArray();
        var result = await CaptionChecks.CreateEngine("media"u8.ToArray(), [], _ => new ByteArrayContent(source), [Track("en", "vtt")])
            .DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, ConvertSubs = "VTT", SkipDownload = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllBytes(Path.Combine(files.Directory, "result.en.vtt")).SequenceEqual(source),
            "A same-format request invoked conversion or rewrote the original document.");
    }

    private static async Task MissingConverter()
    {
        using var files = new Files(); var calls = new List<string>();
        var engine = CaptionChecks.CreateEngine("media"u8.ToArray(), calls, tracks: [Track("en", "vtt")]);
        await Expect<FileNotFoundException>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, ConvertSubs = "srt" }, null, null, default));
        Assert(calls.Count == 0 && !File.Exists(Path.Combine(files.Directory, "result.mp4")), "Missing subtitle conversion tools were discovered after media transfer.");
        var simulated = await engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, ConvertSubs = "srt", Simulate = true }, null, null, default);
        Assert(simulated.Simulated && calls.Count == 0, "Simulation required conversion tools or fetched caption data.");
    }

    private static async Task InvalidOptions()
    {
        using var files = new Files(); var calls = new List<string>();
        var engine = CaptionChecks.CreateEngine("media"u8.ToArray(), calls, tracks: [Track("en", "vtt")]);
        foreach (var preference in new[] { "srt//vtt", "../srt", "srt,ass", new string('a', 257) })
            await Expect<ArgumentException>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SubFormat = preference }, null, null, default));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SubFormat = "ass" }, null, null, default));
        await Expect<NotSupportedException>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, ConvertSubs = "exe" }, null, null, default));
        var unsupported = CaptionChecks.CreateEngine("media"u8.ToArray(), calls, tracks: [Track("en", "json3")]);
        await Expect<NotSupportedException>(() => unsupported.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SubFormat = "json3", ConvertSubs = "srt" }, null, null, default));
        Assert(calls.Count == 0, "Unsupported subtitle options silently fell back or reached media transfer.");
    }

    private static void Arguments()
    {
        var request = NativeExtraArguments.Apply(CaptionChecks.Request(Path.GetTempPath()), ["--all-subs", "--write-auto-subs", "--sub-format", "ass/srt/best", "--convert-subtitles", "srt"]);
        Assert(request.WriteSubs && request.WriteAutoSubs && request.SubLanguages == "all" && request.SubFormat == "ass/srt/best" && request.ConvertSubs == "srt", "Subtitle argument mapping lost an option.");
        request = NativeExtraArguments.Apply(request, ["--sub-langs", "fr", "--sub-format=vtt", "--convert-subs=vtt", "--no-write-auto-subs"]);
        Assert(request.SubLanguages == "fr" && request.SubFormat == "vtt" && request.ConvertSubs == "vtt" && !request.WriteAutoSubs,
            "Per-job subtitle overrides lost their ordering.");
    }

    private static async Task ProcessBoundary(string mode)
    {
        using var files = new Files(); using var stop = new CancellationTokenSource();
        var marker = Path.Combine(files.Directory, "worker.pid");
        var previous = Environment.GetEnvironmentVariable(WorkerVariable);
        Task<byte[]>? conversion = null;
        try
        {
            Environment.SetEnvironmentVariable(WorkerVariable, mode + "|" + marker);
            conversion = FfmpegTool.ConvertSubtitleAsync(WorkerExecutable, Encoding.UTF8.GetBytes(Vtt), "vtt", "srt", files.Directory, stop.Token);
            if (mode == "cancel")
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!File.Exists(marker))
                {
                    if (conversion.IsCompleted || DateTime.UtcNow >= deadline) throw new InvalidOperationException("Subtitle worker did not start.");
                    await Task.Delay(10);
                }
                stop.Cancel();
                await Expect<OperationCanceledException>(() => conversion);
            }
            else await Expect<InvalidDataException>(() => conversion);
            var pid = int.Parse(File.ReadAllText(marker));
            try { using var process = Process.GetProcessById(pid); Assert(process.HasExited, "Subtitle converter survived failure or cancellation."); }
            catch (ArgumentException) { }
            Assert(!Directory.EnumerateFiles(files.Directory, ".caption-*").Any(), "Subtitle conversion left temporary source documents.");
        }
        finally
        {
            stop.Cancel();
            if (conversion is not null) { try { await conversion; } catch { } }
            Environment.SetEnvironmentVariable(WorkerVariable, previous);
        }
    }

    private static async Task ConversionFailure()
    {
        using var files = new Files(); var previous = Environment.GetEnvironmentVariable(WorkerVariable);
        try
        {
            foreach (var mode in new[] { "fail", "empty", "invalid-utf8", "headers-only" })
            {
                var target = Path.Combine(files.Directory, "result.en.srt"); File.WriteAllText(target, "previous captions");
                Environment.SetEnvironmentVariable(WorkerVariable, mode + "|" + Path.Combine(files.Directory, "worker.pid"));
                var engine = CaptionChecks.CreateEngine("media"u8.ToArray(), [], _ => new StringContent(Vtt), [Track("en", "vtt")]);
                await Expect<Exception>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with
                { WriteSubs = true, SkipDownload = true, ConvertSubs = "srt", FfmpegPath = WorkerExecutable }, null, null, default));
                Assert(File.ReadAllText(target) == "previous captions" && !Directory.EnumerateFiles(files.Directory, ".caption-*", SearchOption.AllDirectories).Any(),
                    "Failed conversion changed published captions or leaked temporary source documents.");
            }
        }
        finally { Environment.SetEnvironmentVariable(WorkerVariable, previous); }
    }

    private static string WorkerExecutable => Path.Combine(AppContext.BaseDirectory, "YoutubeDl.Engine.Tests.exe");
    internal static async Task<int> Worker(string configuration)
    {
        var parts = configuration.Split('|', 2);
        await File.WriteAllTextAsync(parts[1] + ".tmp", Environment.ProcessId.ToString());
        File.Move(parts[1] + ".tmp", parts[1], true);
        using var output = Console.OpenStandardOutput();
        switch (parts[0])
        {
            case "overflow": await output.WriteAsync(new byte[FfmpegTool.MaximumSubtitleBytes + 8192]); await output.FlushAsync(); break;
            case "cancel": break;
            case "invalid-utf8": await output.WriteAsync(new byte[] { 0xff }); return 0;
            case "headers-only": await output.WriteAsync("WEBVTT\n\n"u8.ToArray()); return 0;
            case "empty": return 0;
            default: await Console.Error.WriteLineAsync("Fixture conversion failure"); return 3;
        }
        await Task.Delay(TimeSpan.FromMinutes(1));
        return 0;
    }

    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "subtitle-format-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            var path = Path.GetFullPath(Directory);
            if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root.");
            System.IO.Directory.Delete(path, true);
        }
    }
}
