using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers.YouTube;
using static SabrFixture;

internal static class SabrNativeChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("SABR tick-only timing accumulates precise ranges and completes at the rounded end", Timing),
        ("Malformed and contradictory SABR clock values are rejected", () => Sync(InvalidTiming)),
        ("SABR temporary playback can finish while explicit denial remains terminal", Startup),
        ("SABR stalled playback remains bounded with network retries enabled", Stalled),
        ("SABR interrupted responses retry only incomplete segments and retain exact completed bytes", Retry),
        ("SABR write failures never retry partially written output", WriteFailure),
        ("Native YouTube web discovery establishes mobile playback and publishes one complete file", () => Native(false)),
        ("Denied native YouTube playback preserves prior media, sidecars and archive", () => Native(true)),
        ("Invalid or restricted mobile contexts never expose ready YouTube SABR formats", InvalidContexts),
        ("SABR discovery keeps DRC identities and protocol filters distinct", () => Sync(Formats))
    ];

    private static SabrRequest Request(SabrControlState state, int _) => new()
    { ClientVersion = "fixture", PlaybackConfiguration = [1], VideoFormats = [Video], PlaybackCookie = state.PlaybackCookie, Contexts = state.OutgoingContexts };
    private static SabrTrackSink Sink(Stream output) => new(new Dictionary<SabrFormatId, Stream> { [Video] = output });

    private static async Task Timing()
    {
        using var bytes = new MemoryStream();
        Metadata(bytes, Video, 2, 2, 3);
        Segment(bytes, Video, 1, 0, [1]);
        Segment(bytes, Video, 2, 1, [2], new(0, 1, 3));
        Segment(bytes, Video, 3, 2, [3], new(1, 1, 3));
        bytes.Position = 0;
        using var output = new MemoryStream(); using var assembler = new SabrSegmentAssembler(VideoId);
        var control = new SabrControlState(VideoId); var sink = Sink(output);
        await SabrResponseProcessor.ReadAsync(bytes, control, assembler, sink, default);
        Assert(sink.IsComplete(control) && output.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Precise media did not complete.");
        Assert(sink.BufferedRanges.Single() == new SabrBufferedRange(Video, 0, 667, 1, 2, new(0, 2, 3)), "Tick fractions accumulated rounding error.");
        var request = ProtobufReader.Read((Request(control, 0) with { InitializedFormats = sink.InitializedFormats, BufferedRanges = sink.BufferedRanges }).Encode());
        var range = ProtobufReader.Read(request.Single(field => field.Number == 3).Bytes);
        Assert(range.Single(field => field.Number == 6).Bytes.Span.SequenceEqual(new byte[] { 8, 0, 16, 2, 24, 3 }), "The precise buffered clock wire values differ.");
        await Expect<InvalidDataException>(() => sink.AcceptAsync(new(Video, 3, false, 666, 334, new byte[] { 9 }, new(2001, 999, 3000)), default));
        Assert(output.Length == 3, "A changed clock wrote bytes.");
    }

    private static void InvalidTiming()
    {
        foreach (var time in new[] { new SabrTimeRange(0, 1, 0), new(0, 0, 30), new(long.MaxValue, 1, 30), new(0, long.MaxValue, 1) })
        {
            using var assembler = new SabrSegmentAssembler(VideoId);
            Throws<InvalidDataException>(() => assembler.Accept(new(20, Header(Video, 1, 1, 1, time))));
        }
        using (var assembler = new SabrSegmentAssembler(VideoId))
        {
            var conflicting = new ProtobufWriter(); conflicting.UInt64(11, 10);
            Throws<InvalidDataException>(() => assembler.Accept(new(20, Header(Video, 1, 1, 1, new(0, 1, 30)).Concat(conflicting.ToArray()).ToArray())));
        }
        var control = new SabrControlState(VideoId);
        control.Accept(End(1, 3));
        Throws<InvalidDataException>(() => control.Accept(End(2, 3)));
        Throws<InvalidDataException>(() => new SabrControlState(VideoId).Accept(End(1, 0)));
        var extra = new ProtobufWriter(); extra.UInt64(3, 1000);
        var valid = End(1, 3);
        Throws<InvalidDataException>(() => new SabrControlState(VideoId).Accept(valid with { Payload = valid.Payload.ToArray().Concat(extra.ToArray()).ToArray() }));
        Throws<ArgumentException>(() => (Request(control, 0) with { InitializedFormats = [Video], BufferedRanges = [new(Video, 0, 334, 1, 1, new(0, 2, 3))] }).Encode());

        static UmpPart End(ulong ticks, uint scale)
        {
            var writer = new ProtobufWriter(); writer.Message(2, format => { format.UInt64(1, 137); format.UInt64(2, 100); });
            writer.UInt64(4, 1); writer.UInt64(9, ticks); writer.UInt64(10, scale); return new(42, writer.ToArray());
        }
    }

    private static async Task Startup()
    {
        foreach (var status in new uint[] { 2, 3 })
        {
            using var body = new MemoryStream(); Status(body, status); Metadata(body, Video, 1, 1, 3);
            Segment(body, Video, 1, 0, [1]); Segment(body, Video, 2, 1, [2], new(0, 1, 3));
            using var output = new MemoryStream(); var count = 0;
            using var http = Http(_ => { count++; return Task.FromResult(Response(body.ToArray())); });
            var download = new SabrHttpSession(http).DownloadAsync(Endpoint, VideoId, Sink(output), Request, default, allowStartupPlayback: true, retries: 2);
            if (status == 2) { await download; Assert(output.Length == 2, "Temporary allowed playback did not complete."); }
            else { await Expect<NotSupportedException>(() => download); Assert(output.Length == 0, "Denied playback wrote data."); }
            Assert(count == 1, "A playback status triggered a new session or retry.");
        }
    }

    private static async Task Stalled()
    {
        var count = 0; using var body = new MemoryStream(); Status(body, 2);
        using var output = new MemoryStream();
        using var http = Http(_ => { count++; return Task.FromResult(Response(body.ToArray())); });
        await Expect<EndOfStreamException>(() => new SabrHttpSession(http).DownloadAsync(Endpoint, VideoId, Sink(output), Request, default, allowStartupPlayback: true, retries: 2));
        Assert(count == 3 && output.Length == 0, "Network retries extended stalled playback.");
    }

    private static async Task Retry()
    {
        using var output = new MemoryStream(); var count = 0;
        using var http = Http(async request =>
        {
            using var body = new MemoryStream(); Metadata(body, Video, 2, 2000);
            var fields = ProtobufReader.Read(await request.Content!.ReadAsByteArrayAsync());
            if (count++ == 0)
            {
                Segment(body, Video, 1, 0, [1]); Segment(body, Video, 2, 1, [2], new(0, 1000, 1000));
                Segment(body, Video, 3, 2, [9], new(1000, 1000, 1000), complete: false);
            }
            else
            {
                var range = ProtobufReader.Read(fields.Single(field => field.Number == 3).Bytes);
                Assert(range.Single(field => field.Number == 5).Integer == 1, "Incomplete data was acknowledged.");
                // A server may repeat a completed segment; it must not be written twice.
                Segment(body, Video, 2, 1, [2], new(0, 1000, 1000));
                Segment(body, Video, 3, 2, [3], new(1000, 1000, 1000));
            }
            return Response(body.ToArray());
        });
        await new SabrHttpSession(http).DownloadAsync(Endpoint, VideoId, Sink(output), Request, default, retries: 1);
        Assert(count == 2 && output.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Retry duplicated or retained unfinished bytes.");
    }

    private static async Task WriteFailure()
    {
        using var body = new MemoryStream(); Segment(body, Video, 1, 0, [1, 2]);
        using var output = new FailingOutput(); var count = 0;
        using var http = Http(_ => { count++; return Task.FromResult(Response(body.ToArray())); });
        await Expect<IOException>(() => new SabrHttpSession(http).DownloadAsync(Endpoint, VideoId, Sink(output), Request, default, retries: 2));
        Assert(count == 1 && output.Length == 1, "Output write failure retried a partially written segment.");
    }

    private static async Task Native(bool denied)
    {
        using var files = new Files(); var path = Path.Combine(files.Directory, "result.mp4"); var info = Path.Combine(files.Directory, "result.info.json");
        var archive = Path.Combine(files.Directory, "archive.txt");
        await File.WriteAllTextAsync(path, "prior media"); await File.WriteAllTextAsync(info, "prior info"); await File.WriteAllTextAsync(archive, "youtube priorvideo\n");
        var calls = 0; var completed = 0; var mobile = 0;
        var engine = new NativeEngine(httpFactory: _ => Http(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/watch") return new(HttpStatusCode.OK) { Content = new StringContent(Watch) };
            if (request.RequestUri.AbsolutePath == "/youtubei/v1/player")
            {
                mobile++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert(json.RootElement.GetProperty("videoId").GetString() == VideoId && json.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientName").GetString() == "IOS", "The mobile request lost identity.");
                Assert(request.Headers.UserAgent.ToString().Contains("com.google.ios.youtube"), "The mobile client header was not supplied.");
                return new(HttpStatusCode.OK) { Content = new StringContent(Player().ToJsonString()) };
            }
            Assert(request.RequestUri == Endpoint && request.Method == HttpMethod.Post, "Unexpected transport request.");
            Assert(request.Headers.UserAgent.ToString().Contains("com.google.ios.youtube"), "Playback lost the mobile user agent.");
            calls++; using var body = new MemoryStream(); Status(body, denied && calls > 1 ? 3u : 2u);
            if (calls == 1)
            {
                Metadata(body, Video, denied ? 2u : 1u, denied ? 2000u : 1000u);
                Segment(body, Video, 1, 0, [1, 2]); Segment(body, Video, 2, 1, [3, 4], new(0, 1000, 1000));
            }
            return Response(body.ToArray());
        }));
        var task = engine.DownloadAsync(new NativeDownloadRequest { Url = new("https://www.youtube.com/watch?v=" + VideoId), OutputDirectory = files.Directory,
            FileName = "result", FormatSelector = "137[protocol=sabr][height<=1080]", WriteInfoJson = true, DownloadArchiveFile = archive, Retries = 2 }, null, null, default, _ => completed++);
        if (denied)
        {
            await Expect<NotSupportedException>(() => task);
            Assert(calls == 2 && completed == 0 && File.ReadAllText(path) == "prior media" && File.ReadAllText(info) == "prior info" && File.ReadAllText(archive) == "youtube priorvideo\n", "Denial replaced a published file or changed the archive.");
        }
        else
        {
            var result = await task;
            Assert(result.Succeeded && completed == 1 && File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3, 4 }) && File.ReadAllText(archive).Contains("youtube " + VideoId), "Completed native playback was not published and recorded once.");
        }
        Assert(mobile == 1 && !Directory.EnumerateFiles(files.Directory, ".sabr-*", SearchOption.AllDirectories).Any(), "A playback session was recreated or left temporary media.");
    }

    private static async Task InvalidContexts()
    {
        Action<JsonNode>[] cases = [
            node => node["videoDetails"]!["videoId"] = "wrongvideo1",
            node => node["videoDetails"]!["isLive"] = true,
            node => node["playabilityStatus"]!["status"] = "LOGIN_REQUIRED",
            node => node["playerConfig"] = new JsonObject(),
            node => node["playerConfig"]!["mediaCommonConfig"]!["mediaUstreamerRequestConfig"]!["videoPlaybackUstreamerConfig"] = "invalid!",
            node => node["playerConfig"]!["mediaCommonConfig"]!["mediaUstreamerRequestConfig"]!["videoPlaybackUstreamerConfig"] = "    ",
            node => node["streamingData"]!["serverAbrStreamingUrl"] = "https://other.example/video",
            node => node["streamingData"]!["serverAbrStreamingUrl"] = Endpoint + "&n=unresolved",
            node => { foreach (var format in node["streamingData"]!["adaptiveFormats"]!.AsArray()) format!["drmFamilies"] = new JsonArray("WIDEVINE"); }
        ];
        foreach (var mutate in cases)
        {
            var node = Player(); mutate(node); var calls = 0;
            var engine = new NativeEngine(httpFactory: _ => Http(request =>
            {
                calls++; Assert(request.RequestUri!.Host == "www.youtube.com", "Invalid context reached media transport.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? Watch : node.ToJsonString()) });
            }));
            var info = await engine.InspectAsync(new NativeDownloadRequest { Url = new("https://youtu.be/" + VideoId), OutputDirectory = Path.GetTempPath() }, default);
            Assert(calls == 2 && info.Formats.All(format => format.Availability != FormatAvailability.Ready), "Invalid mobile context exposed ready media.");
        }
    }

    private static void Formats()
    {
        var node = Player(); var formats = node["streamingData"]!["adaptiveFormats"]!.AsArray();
        var drc = formats[1]!.DeepClone(); drc["isDrc"] = true; drc["xtags"] = "fixture-drc"; formats.Add(drc);
        var page = YouTubePlayerParser.ParseResponse(node.ToJsonString(), VideoId, sabrSession: new(VideoId, Endpoint, 5, "fixture", [1], "fixture"));
        Assert(page.Media.Count == 3 && page.Media.Select(item => item.Id).Distinct().Count() == 3 && page.Media.Single(item => item.Id?.EndsWith("-drc") == true).Sabr!.Format.ExtraTags == "fixture-drc", "DRC format identities collided or lost transport tags.");
    }
    private static MediaHttp Http(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(new Handler(respond), new MediaHttpOptions { AllowAutoRedirect = false });
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool condition, string message) => SabrFixture.Assert(condition, message);
    private sealed class FailingOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { WriteByte(buffer.Span[0]); return ValueTask.FromException(new IOException("Fixture disk failure")); }
    }
}
