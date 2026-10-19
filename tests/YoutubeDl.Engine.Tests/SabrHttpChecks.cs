using System.Net;
using System.Net.Http.Headers;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class SabrHttpChecks
{
    private static readonly SabrFormatId Format = new(137, 100);
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Native SABR HTTP requests assemble selected staged media", Transfer),
        ("SABR follow-up requests report received track state without suppressing fresh initialization", FollowUp),
        ("SABR protection requirements fail before staging initial media", Protection),
        ("SABR authorization errors and redirect boundaries fail explicitly", Boundaries),
        ("SABR empty-response loops terminate without declaring completion", Stalled)
    ];
    private static SabrRequest Request(SabrControlState state, int _) => new()
    {
        ClientVersion = "fixture", PlaybackConfiguration = [1], VideoFormats = [Format],
        PlaybackCookie = state.PlaybackCookie, Contexts = state.OutgoingContexts
    };
    private static SabrTrackSink Sink(Stream output) => new(new Dictionary<SabrFormatId, Stream> { [Format] = output });
    private static async Task Transfer()
    {
        using var output = new MemoryStream();
        var requests = 0;
        using var http = new MediaHttp(new Handler(async request =>
        {
            requests++;
            var fields = ProtobufReader.Read(await request.Content!.ReadAsByteArrayAsync());
            if (request.Method != HttpMethod.Post || fields.All(field => field.Number != 17)) throw new InvalidOperationException("The session did not send selected formats.");
            using var response = new MemoryStream();
            var initialization = new ProtobufWriter(); initialization.String(1, "fixture-video");
            initialization.Message(2, format => { format.UInt64(1, 137); format.UInt64(2, 100); }); initialization.UInt64(4, 1);
            Part(response, 42, initialization.ToArray());
            Segment(response, 1, 0, true, [1, 2]);
            Segment(response, 2, 1, false, [3, 4]);
            return Ump(response.ToArray());
        }), new MediaHttpOptions { AllowAutoRedirect = false });
        await new SabrHttpSession(http).DownloadAsync(new Uri("https://r1.googlevideo.com/videoplayback?fixture=authorized"), "fixture-video", Sink(output), Request, default);
        if (requests != 1 || !output.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new InvalidOperationException("The HTTP session did not finish the requested track.");
    }
    private static async Task FollowUp()
    {
        using var output = new MemoryStream();
        var requests = 0;
        using var http = new MediaHttp(new Handler(async request =>
        {
            var fields = ProtobufReader.Read(await request.Content!.ReadAsByteArrayAsync());
            using var response = new MemoryStream();
            if (requests++ == 0)
            {
                if (fields.Any(field => field.Number is 2 or 3)) throw new InvalidOperationException("Fresh state suppressed initialization.");
                var metadata = new ProtobufWriter(); metadata.String(1, "fixture-video");
                metadata.Message(2, format => { format.UInt64(1, 137); format.UInt64(2, 100); }); metadata.UInt64(4, 2);
                Part(response, 42, metadata.ToArray());
                Segment(response, 1, 0, true, [1, 2]); Segment(response, 2, 1, false, [3, 4]);
            }
            else
            {
                if (fields.Count(field => field.Number == 2) != 1) throw new InvalidOperationException("Received initialization was not acknowledged.");
                var range = ProtobufReader.Read(fields.Single(field => field.Number == 3).Bytes);
                if (range.Single(field => field.Number == 2).Integer != 0 || range.Single(field => field.Number == 3).Integer != 1000 ||
                    range.Single(field => field.Number == 4).Integer != 1 || range.Single(field => field.Number == 5).Integer != 1)
                    throw new InvalidOperationException("The follow-up request did not report the received media range.");
                Segment(response, 3, 2, false, [5, 6]);
            }
            return Ump(response.ToArray());
        }), new MediaHttpOptions { AllowAutoRedirect = false });
        await new SabrHttpSession(http).DownloadAsync(new Uri("https://r1.googlevideo.com/videoplayback"), "fixture-video", Sink(output),
            (state, number) => Request(state, number) with { InitializedFormats = [Format] }, default);
        if (requests != 2 || !output.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 })) throw new InvalidOperationException("The follow-up session did not finish exact staged bytes.");
    }

    private static async Task Protection()
    {
        using var output = new MemoryStream();
        using var response = new MemoryStream();
        var protection = new ProtobufWriter(); protection.UInt64(1, 2); Part(response, 58, protection.ToArray());
        Segment(response, 1, 0, true, [1, 2]);
        using var http = new MediaHttp(new Handler(_ => Task.FromResult(Ump(response.ToArray()))), new MediaHttpOptions { AllowAutoRedirect = false });
        await Expect<NotSupportedException>(() => new SabrHttpSession(http).DownloadAsync(new Uri("https://r1.googlevideo.com/videoplayback"), "fixture-video", Sink(output), Request, default));
        if (output.Length != 0) throw new InvalidOperationException("Unauthorized initial media was staged.");
    }

    private static async Task Boundaries()
    {
        using var output = new MemoryStream();
        using var denied = new MediaHttp(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))), new MediaHttpOptions { AllowAutoRedirect = false });
        await Expect<NotSupportedException>(() => new SabrHttpSession(denied).DownloadAsync(new Uri("https://r1.googlevideo.com/media"), "fixture-video", Sink(output), Request, default));
        using var redirect = new MediaHttp(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect); response.Headers.Location = new Uri("https://evil.example/media"); return Task.FromResult(response);
        }), new MediaHttpOptions { AllowAutoRedirect = false });
        await Expect<ArgumentException>(() => new SabrHttpSession(redirect).DownloadAsync(new Uri("https://r1.googlevideo.com/media"), "fixture-video", Sink(output), Request, default));
        using var automatic = new MediaHttp(new Handler(_ => throw new InvalidOperationException("Automatic redirects must be checked before HTTP.")));
        await Expect<ArgumentException>(() => new SabrHttpSession(automatic).DownloadAsync(new Uri("https://r1.googlevideo.com/media"), "fixture-video", Sink(output), Request, default));
        await Expect<ArgumentException>(() => new SabrHttpSession(denied).DownloadAsync(new Uri("https://r1.googlevideo.com/media"), "fixture-video", Sink(output),
            (state, number) => Request(state, number) with { VideoFormats = [new(138, 100)] }, default));
    }
    private static async Task Stalled()
    {
        using var output = new MemoryStream();
        var requests = 0;
        using var http = new MediaHttp(new Handler(_ => { requests++; return Task.FromResult(Ump([])); }), new MediaHttpOptions { AllowAutoRedirect = false });
        await Expect<EndOfStreamException>(() => new SabrHttpSession(http).DownloadAsync(new Uri("https://r1.googlevideo.com/media"), "fixture-video", Sink(output), Request, default));
        if (requests != 3 || output.Length != 0) throw new InvalidOperationException("A stalled session was retried indefinitely or declared complete.");
    }
    private static void Segment(Stream output, byte id, uint number, bool init, byte[] data)
    {
        var header = new ProtobufWriter(); header.UInt64(1, id); header.String(2, "fixture-video"); header.UInt64(3, 137); header.UInt64(4, 100);
        header.UInt64(8, init ? 1UL : 0); header.UInt64(9, number); header.UInt64(14, (ulong)data.Length);
        if (!init) { header.UInt64(11, (number - 1) * 1000); header.UInt64(12, 1000); }
        Part(output, 20, header.ToArray()); Part(output, 21, new byte[] { id }.Concat(data).ToArray()); Part(output, 22, [id]);
    }
    private static void Part(Stream output, byte type, byte[] data)
    {
        if (data.Length >= 128) throw new InvalidOperationException("The test fixture must have a one-byte UMP length.");
        output.WriteByte(type); output.WriteByte((byte)data.Length); output.Write(data);
    }
    private static HttpResponseMessage Ump(byte[] data)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.yt-ump"); return response;
    }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request);
    }
}
