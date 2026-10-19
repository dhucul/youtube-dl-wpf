using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Media;

internal static class TtmlChecks
{
    internal const string Sample = "<tt xmlns='http://www.w3.org/ns/ttml' xmlns:tts='http://www.w3.org/ns/ttml#styling'><body><div><p begin='0.120s' end='0.870s'>Bonjour café &amp; punctuation.<br/>Second line.</p><p begin='1.230s' end='2.340s'><span tts:fontStyle='italic'>Final 日本語.</span></p></div></body></tt>";
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("TTML native SRT and VTT preserve Unicode, entities, line breaks and timed emphasis", () => Sync(Basic)),
        ("TTML nested intervals honor parent clipping and the shorter end or duration", () => Sync(Nesting)),
        ("TTML sequential siblings use prior active ends and derive container duration", () => Sync(Sequence)),
        ("TTML timed spans split text changes without extending cue visibility", () => Sync(Spans)),
        ("TTML style references and hidden regions preserve visible text only", () => Sync(Styles)),
        ("TTML whitespace modes preserve deliberate spaces and blank lines", () => Sync(Whitespace)),
        ("TTML media clocks honor fractional frame rates, subframes and tick defaults", () => Sync(Clocks)),
        ("TTML legacy DFXP namespaces retain their timing and text", () => Sync(Legacy)),
        ("TTML invalid XML, unsupported timing and recursive styles fail explicitly", () => Sync(Invalid)),
        ("TTML bounded XML and cue expansion honor limits and cancellation", () => Sync(Bounds)),
        ("TTML standalone conversion requires no ffmpeg or media transfer", Standalone),
        ("TTML original saving stays byte exact while failed conversion preserves existing captions", Originals),
        ("TTML conversion failure preserves published media and download archives", Publication)
    ];
    private static string Xml(string body, string parameters = "", string head = "") => "<tt xmlns='http://www.w3.org/ns/ttml' xmlns:ttp='http://www.w3.org/ns/ttml#parameter' xmlns:tts='http://www.w3.org/ns/ttml#styling' " + parameters + ">" + head + "<body>" + body + "</body></tt>";
    private static TtmlDocument Read(string text) => TtmlReader.Read(text, default);
    private static void Basic()
    {
        var document = Read(Sample);
        Assert(document.Cues.Count == 2 && document.Cues[0] == new TtmlCue(.120m, .870m, "Bonjour café &amp; punctuation.\nSecond line.") &&
            document.Cues[1] == new TtmlCue(1.230m, 2.340m, "<i>Final 日本語.</i>"), "TTML cue text, markup or timing changed.");
        Assert(document.Render("srt").StartsWith("1\n00:00:00,120 --> 00:00:00,870\n") && document.Render("vtt").StartsWith("WEBVTT\n\n00:00:00.120 --> 00:00:00.870\n"), "Native caption serialization used the wrong format.");
        var literal = Read(Xml("<p begin='0s' dur='1s'>&lt;b&gt;literal&lt;/b&gt; &amp; data</p>"));
        Assert(literal.Cues.Single().Text == "&lt;b&gt;literal&lt;/b&gt; &amp; data", "Literal XML text became subtitle markup.");
    }
    private static void Nesting()
    {
        var text = Xml("<div begin='12s' dur='6s'><p begin='1s' end='10s'>Clipped</p><p begin='7s' dur='2s'>Outside</p></div><p begin='20s' end='30s' dur='2s'>Shorter</p>");
        Assert(Read(text).Cues.SequenceEqual([new TtmlCue(13, 18, "Clipped"), new TtmlCue(20, 22, "Shorter")]), "Ancestor bounds or end/duration precedence were ignored.");
        Assert(Read(Xml("<p begin='0s' dur='3s'>First paragraph</p><p begin='0s' dur='1s'>Second paragraph</p>")).Cues.Select(cue => cue.Text).SequenceEqual(["First paragraph", "Second paragraph"]), "Simultaneous paragraphs changed document order.");
    }
    private static void Sequence()
    {
        var text = Xml("<div begin='1s' timeContainer='seq'><p dur='2s'>First</p><p begin='1s' dur='3s'>Second</p><div><p dur='1s'>Third</p></div><p begin='1s' end='2s'>Fourth</p></div>");
        Assert(Read(text).Cues.SequenceEqual([new TtmlCue(1, 3, "First"), new TtmlCue(4, 7, "Second"), new TtmlCue(7, 8, "Third"), new TtmlCue(9, 10, "Fourth")]), "Sequential timing treated a begin delay as duration or lost the prior sibling's active end.");
    }
    private static void Spans()
    {
        var text = Xml("<p begin='1s' end='5s'>Base <span begin='1s' end='2s'>temporary </span><span begin='2s' dur='1s'>later </span>tail</p>");
        Assert(Read(text).Cues.SequenceEqual([new TtmlCue(1, 2, "Base tail"), new TtmlCue(2, 3, "Base temporary tail"), new TtmlCue(3, 4, "Base later tail"), new TtmlCue(4, 5, "Base tail")]), "Timed spans leaked outside their active interval.");
        var implicitParagraph = Read(Xml("<p><span begin='1s' dur='1s'>Finite child</span></p>"));
        Assert(implicitParagraph.Cues.Single() == new TtmlCue(1, 2, "Finite child"), "Finite child timing could not bound a paragraph.");
    }
    private static void Styles()
    {
        const string head = "<head><styling><style xml:id='base' tts:fontWeight='bold'/><style xml:id='emphasis' style='base' tts:fontStyle='italic'/></styling><layout><region xml:id='hidden' tts:display='none'/></layout></head>";
        var text = Xml("<p begin='0s' dur='1s' style='emphasis' tts:fontWeight='normal'>Visible</p><p begin='1s' dur='1s' region='hidden' tts:display='auto'>Must stay hidden</p><p begin='2s' dur='1s' tts:visibility='hidden'>Hidden<span tts:visibility='visible'>Restored</span></p>", head: head);
        Assert(Read(text).Cues.SequenceEqual([new TtmlCue(0, 1, "<i>Visible</i>"), new TtmlCue(2, 3, "Restored")]), "Style inheritance or hidden-region propagation exposed incorrect text.");
        var opacity = Read(Xml("<p begin='0s' dur='1s'><span tts:opacity='0'>Hidden</span>Visible</p>"));
        Assert(opacity.Cues.Single().Text == "Visible", "Zero-opacity text appeared in the converted caption.");
        var alpha = Read(Xml("<p begin='0s' dur='1s' tts:color='rgba(0, 0, 0, 0)'>Hidden<span tts:color='inherit'>Still hidden</span><span tts:color='#ffffff'>Restored</span></p>"));
        Assert(alpha.Cues.Single().Text == "Restored", "Transparent text or its visible child was rendered incorrectly.");
    }
    private static void Whitespace()
    {
        var text = Xml("<p begin='0s' dur='1s'>  A \n <span>B</span>   C <br/> D  </p><p begin='1s' dur='1s' xml:space='preserve'> A  B\nC </p><p begin='2s' dur='1s'>One<br/><br/>Two</p>");
        Assert(Read(text).Cues.Select(cue => cue.Text).SequenceEqual(["A B C\nD", " A  B\nC ", "One\n\u00a0\nTwo"]), "Default and preserved XML whitespace were conflated or blank lines ended a cue.");
    }
    private static void Clocks()
    {
        Assert(Read(Xml("<p begin='30f' dur='30f'>Frames</p>", "ttp:frameRate='30' ttp:frameRateMultiplier='1000 1001'")).Cues.Single() == new TtmlCue(1.001m, 2.002m, "Frames"), "Fractional frame rate lost its multiplier.");
        Assert(Read(Xml("<p begin='00:00:00:12.1' dur='25t'>Subframes</p>", "ttp:frameRate='25' ttp:subFrameRate='2'")).Cues.Single() == new TtmlCue(.5m, 1m, "Subframes"), "Subframe or derived tick rate was incorrect.");
        Assert(Read(Xml("<p begin='1t' end='2t'>Default ticks</p>")).Cues.Single() == new TtmlCue(1, 2, "Default ticks"), "Unspecified frame rate incorrectly changed the default tick rate.");
        Assert(Read(Xml("<p begin='1500t' end='2500t'>Explicit ticks</p>", "ttp:tickRate='1000'")).Cues.Single().Start == 1.5m, "Explicit tick rate ignored.");
        Assert(Read(Xml("<p begin='00:01:01.123456' dur='500ms'>Clock</p>")).Cues.Single() == new TtmlCue(61.123m, 61.623m, "Clock"), "Media clock precision or rounding changed.");
    }
    private static void Legacy()
    {
        foreach (var ns in new[] { "http://www.w3.org/2006/10/ttaf1", "http://www.w3.org/2006/04/ttaf1", "" })
        {
            var text = "<tt xmlns='" + ns + "'><body><div><p begin='1s' end='2s'>Legacy</p></div></body></tt>";
            Assert(Read(text).Cues.Single() == new TtmlCue(1, 2, "Legacy"), "A supported DFXP namespace lost text/timing.");
        }
    }
    private static void Invalid()
    {
        foreach (var xml in new[] { "<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///unrequested-ttml-file'>]><tt><body><p begin='0s' dur='1s'>&x;</p></body></tt>", "<html/>", "<tt><body>", Xml("<p begin='2s' end='1s'>Backwards</p>"), Xml("<p begin='0s' dur='1s'>Rate</p>", "ttp:frameRate='0'") })
            Throws<InvalidDataException>(() => Read(xml));
        foreach (var xml in new[] { Xml("<p begin='0s'>Indefinite</p>"), Xml("<p begin='0s' dur='1s'>Clock</p>", "ttp:timeBase='clock'"), Xml("<p begin='wallclock(12:00:00)' dur='1s'>Wall clock</p>"), Xml("<p begin='0s' dur='1s'><set attributeName='color' to='red'/>Animated</p>"), Xml("<p begin='0.0001s' dur='0.0001s'>Too short</p>") })
            Throws<NotSupportedException>(() => Read(xml));
        Throws<InvalidDataException>(() => Read(Xml("<p begin='0s' dur='1s' style='a'>Cycle</p>", head: "<head><styling><style xml:id='a' style='b'/><style xml:id='b' style='a'/></styling></head>")));
        Throws<InvalidDataException>(() => Read(Xml("<p begin='0s' dur='1s' style='missing'>Unknown</p>")));
        Throws<NotSupportedException>(() => Read(Xml("<p begin='0s' dur='1s' style='animated'>Text</p>", head: "<head><styling><style xml:id='animated'><set attributeName='visibility' to='hidden'/></style></styling></head>")));
    }
    private static void Bounds()
    {
        Throws<InvalidDataException>(() => Read(Xml(new string('x', 2 * 1024 * 1024))));
        Throws<InvalidDataException>(() => Read(Xml(string.Concat(Enumerable.Repeat("<div>", 70)) + "<p begin='0s' dur='1s'>Deep</p>" + string.Concat(Enumerable.Repeat("</div>", 70)))));
        Throws<InvalidDataException>(() => Read(Xml(string.Concat(Enumerable.Range(0, 10001).Select(index => "<p begin='" + index + "s' dur='1s'>Cue</p>")))));
        Throws<InvalidDataException>(() => Read(Xml("<p begin='0s' dur='2000s'>" + new string('a', 5000) + string.Concat(Enumerable.Range(0, 1000).Select(index => "<span begin='" + index * 2 + "s' dur='1s'>X</span>")) + "</p>")));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Throws<OperationCanceledException>(() => TtmlReader.Read(Sample, stop.Token));
    }
    private static async Task Standalone()
    {
        using var files = new Files();
        foreach (var format in new[] { "srt", "vtt" })
        {
            var calls = new List<string>(); var callbacks = 0;
            var result = await CaptionChecks.CreateEngine("unused"u8.ToArray(), calls, _ => new StringContent(Sample), [SubtitleFormatChecks.Track("en", "ttml")])
                .DownloadAsync(CaptionChecks.Request(files.Directory) with { FileName = format, WriteSubs = true, SkipDownload = true, ConvertSubs = format }, null, null, default, _ => callbacks++);
            Assert(result.Succeeded && result.OutputPaths.Count == 0 && result.SidecarPaths.Count == 1 && callbacks == 0 && calls.SequenceEqual(["en.ttml"]) &&
                File.ReadAllText(Path.Combine(files.Directory, format + ".en." + format)) == Read(Sample).Render(format), "Native standalone TTML conversion fetched media or needed ffmpeg.");
        }
    }
    private static async Task Originals()
    {
        using var files = new Files(); var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Sample)).ToArray();
        var engine = CaptionChecks.CreateEngine("unused"u8.ToArray(), [], _ => new ByteArrayContent(bytes), [SubtitleFormatChecks.Track("en", "dfxp")]);
        var result = await engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SkipDownload = true }, null, null, default);
        Assert(result.Succeeded && File.ReadAllBytes(Path.Combine(files.Directory, "result.en.dfxp")).SequenceEqual(bytes), "Saving original DFXP changed its bytes.");
        var target = Path.Combine(files.Directory, "result.en.srt"); File.WriteAllText(target, "previous");
        var invalid = CaptionChecks.CreateEngine("unused"u8.ToArray(), [], _ => new StringContent(Xml("<p>Unbounded</p>")), [SubtitleFormatChecks.Track("en", "ttml")]);
        await Expect<NotSupportedException>(() => invalid.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, SkipDownload = true, ConvertSubs = "srt" }, null, null, default));
        Assert(File.ReadAllText(target) == "previous", "Failed TTML conversion replaced a published subtitle.");
    }
    private static async Task Publication()
    {
        using var files = new Files(); var media = Path.Combine(files.Directory, "result.mp4"); var archive = Path.Combine(files.Directory, "archive.txt");
        File.WriteAllText(media, "published media"); File.WriteAllText(archive, "fixture existing\n"); var callbacks = 0;
        var engine = CaptionChecks.CreateEngine("new media"u8.ToArray(), [], _ => new StringContent("<not-ttml/>"), [SubtitleFormatChecks.Track("en", "ttml")]);
        await Expect<InvalidDataException>(() => engine.DownloadAsync(CaptionChecks.Request(files.Directory) with { WriteSubs = true, ConvertSubs = "srt", DownloadArchiveFile = archive }, null, null, default, _ => callbacks++));
        Assert(File.ReadAllText(media) == "published media" && File.ReadAllText(archive) == "fixture existing\n" && callbacks == 0, "Failed TTML conversion changed media/history or issued completion.");
    }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "ttml-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
