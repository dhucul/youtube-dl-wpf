using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;

internal static class MergeFormatChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native merge format chooses the first compatible container without recoding", () => Sync(Containers)),
        ("Native merge format is ignored for unpaired and video-only media", () => Sync(Unpaired)),
        ("Native merge and final remux preferences remain independent", () => Sync(Remux)),
        ("Native free formats preserve resolution frame rate and explicit IDs", () => Sync(Quality)),
        ("Native free audio formats retain the default language track pool", () => Sync(Audio)),
        ("Native free formats apply to best worst ranked and filtered selectors", () => Sync(Selectors)),
        ("Native merge and free-format arguments honor ordered overrides", () => Sync(Arguments)),
        ("Invalid native merge preferences fail before discovery", Invalid)
    ];
    internal static NativeDownloadRequest Request => new() { Url = new("https://formats.example/video"), OutputDirectory = Path.GetTempPath() };
    internal static MediaCandidate Candidate(string id, string extension, bool audio = false, int height = 720, double fps = 30, double bandwidth = 1000,
        bool defaultAudio = false, bool fallback = false, string? codec = null) => new()
    {
        Id = id, Url = new("https://formats.example/" + id), Kind = MediaKind.Progressive, Extension = extension,
        HasVideo = !audio, HasAudio = audio, Height = audio ? null : height, FramesPerSecond = audio ? null : fps,
        Bandwidth = bandwidth, IsDefaultAudio = defaultAudio, IsFallback = fallback,
        VideoCodec = audio ? null : codec ?? (extension == "webm" ? "vp09.00.41.08" : "avc1.4d401f"),
        AudioCodec = audio ? codec ?? (extension is "webm" or "opus" ? "opus" : "mp4a.40.2") : null
    };
    private static PageMetadata Page => new() { Media = [Candidate("mp4", "mp4", bandwidth: 4000), Candidate("webm", "webm", bandwidth: 2000), Candidate("aac", "m4a", true, bandwidth: 192), Candidate("opus", "webm", true, bandwidth: 128)] };
    private static void Containers()
    {
        Assert(MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'mp4'+'aac'", MergeOutputFormat = "webm/mkv" }).Extension == "mkv", "Incompatible first choice was selected.");
        Assert(MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'webm'+'opus'", MergeOutputFormat = "webm/mkv" }).Extension == "webm", "Compatible first choice was skipped.");
        Throws<NotSupportedException>(() => MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'mp4'+'aac'", MergeOutputFormat = "webm" }));
        Assert(MergeOutputFormats.Select("ogg/mkv", "mp4", "theora", "vorbis") == "ogg", "Saved Ogg merge option was not honored.");
        Assert(MergeOutputFormats.Select("ogg/mkv", "mp4", "h264", "aac") == "mkv", "Ogg accepted incompatible codecs.");
    }
    private static void Unpaired()
    {
        Assert(MediaDownloadPlan.Create(Page, Request with { FormatSelector = "mp4", MergeOutputFormat = "webm" }).Extension == "mp4", "Merge preference remuxed a single selected stream.");
        Assert(MediaDownloadPlan.Create(Page, Request with { VideoOnly = true, MergeOutputFormat = "mkv" }).Extension == "mp4", "Video-only selection was relabeled.");
    }
    private static void Remux()
    {
        var plan = MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'mp4'+'aac'", MergeOutputFormat = "mkv", Container = "mp4" });
        Assert(plan.Extension == "mkv", "Final remux replaced the separate merge preference.");
        Assert(MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'mp4'+'aac'", Container = "mkv" }).Extension == "mkv", "Legacy explicit container behavior changed.");
        Assert(MediaDownloadPlan.Create(Page, Request with { FormatSelector = "'mp4'+'aac'", MergeOutputFormat = "mkv", RecodeVideo = "webm" }).Extension == "mkv", "Final recoding replaced the merge preference.");
    }
    private static void Quality()
    {
        Assert(MediaDownloadPlan.Create(Page, Request).Primary.Id == "mp4", "Disabled preference changed the default.");
        Assert(MediaDownloadPlan.Create(Page, Request with { PreferFreeFormats = true }).Primary.Id == "webm", "Same-quality free video was not preferred.");
        foreach (var higher in new[] { Candidate("higher", "mp4", height: 1080), Candidate("higher", "mp4", fps: 60) })
            Assert(MediaDownloadPlan.Create(Page with { Media = Page.Media.Append(higher).ToArray() }, Request with { PreferFreeFormats = true }).Primary.Id == "higher", "Free preference discarded higher resolution or frame rate.");
        Assert(MediaDownloadPlan.Create(Page, Request with { PreferFreeFormats = true, FormatId = "mp4" }).Primary.Id == "mp4", "An explicit ID was overridden.");
        var fallback = Page with { Media = [Candidate("regular", "mp4"), Candidate("fallback", "webm", height: 1080, fallback: true), Candidate("audio", "m4a", true)] };
        Assert(MediaDownloadPlan.Create(fallback, Request with { PreferFreeFormats = true }).Primary.Id == "regular", "Fallback preference was lost.");
    }
    private static void Audio()
    {
        var plan = MediaDownloadPlan.Create(Page, Request with { PreferFreeFormats = true });
        Assert(plan.Audio?.Id == "opus" && plan.Extension == "webm", "Free audio was not selected or was mislabeled.");
        var page = Page with { Media = [Candidate("video", "mp4"), Candidate("default", "m4a", true, defaultAudio: true), Candidate("alternate", "webm", true)] };
        Assert(MediaDownloadPlan.Create(page, Request with { PreferFreeFormats = true }).Audio?.Id == "default", "Free preference overrode the default audio language.");
        Assert(MediaDownloadPlan.Create(Page, Request with { AudioOnly = true, PreferFreeFormats = true }).Primary.Id == "opus", "Audio-only preference was ignored.");
    }
    private static void Selectors()
    {
        var request = Request with { PreferFreeFormats = true };
        Assert(NativeFormatSelector.Select(Page, "bv+ba", request).Select(item => item.Id).SequenceEqual(["webm", "opus"]), "Best aliases ignored free formats.");
        Assert(NativeFormatSelector.Select(Page, "wv+wa", request).Select(item => item.Id).SequenceEqual(["mp4", "aac"]), "Worst aliases did not reverse the preference.");
        Assert(NativeFormatSelector.Select(Page, "bv.2", request).Single().Id == "mp4", "Ranked choice did not use the preference.");
        Assert(NativeFormatSelector.Select(Page, "bv[ext=mp4]", request).Single().Id == "mp4", "Preference escaped an explicit filter.");
        Assert(NativeFormatSelector.Select(Page, "'mp4'", request).Single().Id == "mp4", "Preference changed a literal ID.");
    }
    private static void Arguments()
    {
        var request = NativeExtraArguments.Apply(Request, ["--merge-output-format", "webm/mkv", "--prefer-free-formats", "--no-prefer-free-formats", "--merge-output-format=mp4/mkv", "--recode-video", "webm"]);
        Assert(request.MergeOutputFormat == "mp4/mkv" && !request.PreferFreeFormats && request.RecodeVideo == "webm", "Ordered job overrides were lost.");
    }
    private static async Task Invalid()
    {
        var calls = 0; var engine = new NativeEngine(httpFactory: _ => { calls++; throw new Exception("Discovery was reached."); });
        foreach (var value in new[] { "../mp4", "mp4//mkv", "exe", "MP4", string.Join('/', Enumerable.Repeat("mp4", 9)) })
        {
            try { await engine.DownloadAsync(Request with { MergeOutputFormat = value }, null, null, default); throw new Exception("Invalid preference was accepted."); }
            catch (ArgumentException) { }
        }
        Assert(calls == 0, "Invalid preferences reached the network.");
    }
    private static Task Sync(Action test) { test(); return Task.CompletedTask; }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
