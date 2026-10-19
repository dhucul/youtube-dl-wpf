using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

internal static class HlsTransferChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("HLS media descriptors preserve encryption, sequence, and byte ranges", () => Sync(Descriptors)),
        ("Native HLS AES-128 decrypts segments with implicit sequence IVs", AesImplicit),
        ("HLS key rotation and METHOD=NONE preserve clear output", KeyRotation),
        ("HLS byte-range requests assemble exact subresources", Ranges),
        ("HLS initialization maps precede fragmented media exactly once", Initialization),
        ("Invalid HLS ranges and keys preserve the previous result", Failures),
        ("Encrypted HLS maps require explicit initialization vectors", () => Sync(InvalidDescriptors)),
        ("Separate HLS audio requires ffmpeg before any media segment transfer", AudioPrerequisite),
        ("Unsupported HLS period initialization layouts fail before any resource transfer", PeriodPrerequisites)
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private static void Descriptors()
    {
        var playlist = Parse("#EXT-X-MEDIA-SEQUENCE:42\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\",IV=0x2a\n#EXT-X-BYTERANGE:4@0\nmedia.bin\n#EXT-X-BYTERANGE:3\nmedia.bin\n");
        Assert(playlist.MediaSegments[0].Sequence == 42 && playlist.MediaSegments[1].Sequence == 43, "Media sequence was lost.");
        Assert(playlist.MediaSegments[1].Range == new HlsByteRange(4, 3) && playlist.MediaSegments[0].Encryption!.IV![15] == 42, "Implicit range or padded IV differs.");
        Assert(playlist.UnsupportedFeatures.Count == 0, "Supported features were marked unsupported.");
    }

    private static async Task AesImplicit()
    {
        using var files = new Files();
        var key = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var iv = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), 42);
        var clear = Encoding.ASCII.GetBytes("native encrypted segment");
        var encrypted = Encrypt(clear, key, iv);
        using var http = new MediaHttp(new Handler(request => Response(request.RequestUri!.AbsolutePath.EndsWith("key") ? key : encrypted)));
        var playlist = Parse("#EXT-X-MEDIA-SEQUENCE:42\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\nsegment.ts\n");
        await new HlsDownloader(http).DownloadAsync(playlist, files.Output, null, default, retries: 0);
        Assert(File.ReadAllBytes(files.Output).AsSpan().SequenceEqual(clear), "AES-128 segment differs after decryption.");
    }

    private static async Task KeyRotation()
    {
        using var files = new Files();
        var firstKey = Enumerable.Repeat((byte)1, 16).ToArray();
        var secondKey = Enumerable.Repeat((byte)2, 16).ToArray();
        var iv = new byte[16]; iv[15] = 1;
        var first = Encrypt(Encoding.ASCII.GetBytes("FIRST"), firstKey, iv);
        var second = Encrypt(Encoding.ASCII.GetBytes("SECOND"), secondKey, iv);
        var keyRequests = 0;
        using var http = new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return Response(name switch { "key" => ++keyRequests == 1 ? firstKey : secondKey, "a.ts" => first, "b.ts" => second, _ => Encoding.ASCII.GetBytes("CLEAR") });
        }));
        var playlist = Parse("#EXT-X-KEY:METHOD=AES-128,URI=\"key\",IV=0x1\na.ts\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\",IV=0x1\nb.ts\n#EXT-X-KEY:METHOD=NONE\nc.ts\n");
        await new HlsDownloader(http).DownloadAsync(playlist, files.Output, null, default, retries: 0);
        Assert(File.ReadAllText(files.Output) == "FIRSTSECONDCLEAR" && keyRequests == 2, "Rotated keys were incorrectly cached by URL.");
    }

    private static async Task Ranges()
    {
        using var files = new Files();
        var content = Encoding.ASCII.GetBytes("0123456789");
        var ranges = new List<(long, long)>();
        using var http = new MediaHttp(new Handler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            ranges.Add((range.From!.Value, range.To!.Value));
            var bytes = content.AsSpan((int)range.From.Value, (int)(range.To.Value - range.From.Value + 1)).ToArray();
            var response = Response(bytes); response.StatusCode = HttpStatusCode.PartialContent;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From.Value, range.To.Value, content.Length);
            return response;
        }));
        await new HlsDownloader(http).DownloadAsync(Parse("#EXT-X-BYTERANGE:3@1\nmedia.bin\n#EXT-X-BYTERANGE:2\nmedia.bin\n"), files.Output, null, default, retries: 0);
        Assert(File.ReadAllText(files.Output) == "12345" && ranges.SequenceEqual(new[] { (1L, 3L), (4L, 5L) }), "HLS ranges were not honored.");
    }

    private static async Task Initialization()
    {
        using var files = new Files();
        var initRequests = 0;
        using var http = new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            if (name == "init.mp4") initRequests++;
            return Response(Encoding.ASCII.GetBytes(name == "init.mp4" ? "INIT" : name == "a.m4s" ? "A" : "B"));
        }));
        await new HlsDownloader(http).DownloadAsync(Parse("#EXT-X-MAP:URI=\"init.mp4\"\na.m4s\nb.m4s\n"), files.Output, null, default, retries: 0);
        Assert(File.ReadAllText(files.Output) == "INITAB" && initRequests == 1, "The initialization map was repeated or omitted.");
    }

    private static async Task Failures()
    {
        using var files = new Files();
        File.WriteAllText(files.Output, "previous result");
        using var rangeHttp = new MediaHttp(new Handler(_ => Response(Encoding.ASCII.GetBytes("whole-resource"))));
        await Expect<InvalidDataException>(() => new HlsDownloader(rangeHttp).DownloadAsync(Parse("#EXT-X-BYTERANGE:3@1\nmedia.bin\n"), files.Output, null, default, retries: 0));
        using var keyHttp = new MediaHttp(new Handler(request => Response(new byte[request.RequestUri!.AbsolutePath.EndsWith("key") ? 15 : 16])));
        await Expect<InvalidDataException>(() => new HlsDownloader(keyHttp).DownloadAsync(Parse("#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\na.ts\n"), files.Output, null, default, retries: 0));
        Assert(File.ReadAllText(files.Output) == "previous result" && Directory.GetDirectories(files.Directory).Length == 0, "Failed HLS transfers changed the previous output or leaked staging.");
    }

    private static void InvalidDescriptors()
    {
        Throws<InvalidDataException>(() => Parse("#EXT-X-BYTERANGE:3\nmedia.bin\n"));
        Throws<InvalidDataException>(() => Parse("#EXT-X-BYTERANGE:3@0\na.bin\n#EXT-X-BYTERANGE:3\nb.bin\n"));
        Throws<InvalidDataException>(() => Parse("#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n#EXT-X-MAP:URI=\"init.mp4\"\na.m4s\n"));
        Throws<InvalidDataException>(() => Parse("#EXT-X-KEY:METHOD=AES-128,URI=\"key\",IV=0xGG\na.ts\n"));
        Throws<InvalidDataException>(() => Parse("#EXT-X-KEY:METHOD=AES-128,METHOD=NONE,URI=\"key\"\na.ts\n"));
        Throws<InvalidDataException>(() => Parse("a.ts\n#EXT-X-STREAM-INF:BANDWIDTH=100\nvariant.m3u8\n"));
    }

    private static async Task AudioPrerequisite()
    {
        using var files = new Files();
        var requests = new List<string>();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler(request =>
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            requests.Add(name);
            var text = name == "master.m3u8"
                ? "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"sound\",NAME=\"Audio\",DEFAULT=YES,URI=\"audio.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=100,AUDIO=\"sound\"\nvideo.m3u8\n"
                : "#EXTM3U\nsegment.ts\n#EXT-X-ENDLIST\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        })));
        await Expect<InvalidOperationException>(() => engine.DownloadAsync(new NativeDownloadRequest
        { Url = new Uri("https://media.example/master.m3u8"), OutputDirectory = files.Directory }, null, null, default));
        Assert(requests.SequenceEqual(["master.m3u8", "video.m3u8", "audio.m3u8"]) && !Directory.EnumerateFiles(files.Directory).Any(),
            "Missing ffmpeg was discovered after transferring media or publishing a silent result.");
    }
    private static async Task PeriodPrerequisites()
    {
        using var files = new Files();
        await File.WriteAllTextAsync(files.Output, "previous result");
        using var http = new MediaHttp(new Handler(_ => throw new InvalidOperationException("Unsupported periods must not fetch media.")));
        foreach (var body in new[] {
            "#EXTINF:1,\na.ts\n#EXT-X-DISCONTINUITY\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:1,\nb.m4s\n",
            "#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:1,\na.m4s\n#EXT-X-MAP:URI=\"changed.mp4\"\n#EXTINF:1,\nb.m4s\n#EXT-X-DISCONTINUITY\n#EXTINF:1,\nc.m4s\n" })
        {
            try { await new HlsDownloader(http).DownloadAsync(Parse(body), files.Output, null, default, retries: 0); }
            catch (NotSupportedException ex) when (ex.Message.Contains("initialization", StringComparison.Ordinal)) { continue; }
            throw new InvalidOperationException("An unsupported initialization layout was not rejected before tool/media access.");
        }
        Assert(File.ReadAllText(files.Output) == "previous result" && Directory.GetDirectories(files.Directory).Length == 0, "A preflight refusal changed the destination.");
    }

    private static HlsPlaylist Parse(string body) => HlsPlaylistParser.Parse("#EXTM3U\n" + body + "#EXT-X-ENDLIST\n", new Uri("https://media.example/list.m3u8"));
    private static byte[] Encrypt(byte[] value, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create(); aes.Key = key; aes.IV = iv;
        return aes.EncryptCbc(value, iv, PaddingMode.PKCS7);
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Directory, "output.ts");
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { foreach (var file in System.IO.Directory.EnumerateFiles(Directory)) File.Delete(file); System.IO.Directory.Delete(Directory); }
    }
}
