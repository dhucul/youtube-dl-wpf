using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static class EngineFoundationChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All =>
    [
        ("Interrupted downloads resume with matching entity validators", Resume),
        ("Changed entities restart without appending stale bytes", ChangedEntity),
        ("Invalid content ranges restart from the full resource", InvalidRange),
        ("HTTP 416 never publishes an incomplete partial file", UnsatisfiedRange),
        ("Unidentified partial files restart safely", UnidentifiedPartial),
        ("Partial files from another URL are not reused", ChangedResource),
        ("Cancellation retains partial data and preserves the previous result", Cancellation),
        ("HTTP failures preserve the previous result and partial data", HttpFailure),
        ("Discovery limits count streamed bytes and stop oversized bodies", DiscoveryLimits),
        ("Unsupported HLS features fail before publishing a file", UnsupportedHls),
        ("Unsupported DASH layouts cannot be mistaken for full media files", UnsupportedDash),
        ("DASH representation URLs resolve inherited base addresses", DashBaseUrls),
        ("Prototype options fail explicitly instead of being silently ignored", UnsupportedOptions),
        ("Concurrent static HLS downloads isolate segment staging", ConcurrentHls),
        ("Dedicated providers take priority over generic discovery", ProviderPriority),
        ("Missing native site providers produce explicit errors", MissingProvider),
        ("The C# engine completes a direct download without an external engine", NativeDownload)
    ];

    private static async Task Resume()
    {
        using var fixture = new Fixture();
        var handler = new Handler((request, index, _) => Task.FromResult(index == 1
            ? Response("abc", declared: 6)
            : Partial("def", 3, 5, 6)));
        using var http = new MediaHttp(handler);
        var downloader = new FileDownloader(http);
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, retries: 0));
        Assert(!File.Exists(fixture.Output), "An incomplete file was published.");
        await downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, retries: 0);
        Assert(File.ReadAllText(fixture.Output) == "abcdef", "Resumed bytes differ.");
        Assert(handler.Requests[1].Range == 3 && handler.Requests[1].Validator == "\"v1\"", "Range/If-Range are missing.");
        Assert(!File.Exists(fixture.Output + ".part.json"), "Resume receipt survived completion.");
    }

    private static async Task ChangedEntity()
    {
        using var fixture = new Fixture();
        var handler = new Handler((_, index, _) => Task.FromResult(index == 1
            ? Response("abc", declared: 6) : Response("new resource", tag: "\"v2\"")));
        using var http = new MediaHttp(handler);
        var downloader = new FileDownloader(http);
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
        await downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0);
        Assert(File.ReadAllText(fixture.Output) == "new resource", "Old entity bytes were retained.");
        Assert(handler.Requests.Count == 2, "A full response was unnecessarily requested twice.");
    }

    private static async Task InvalidRange()
    {
        foreach (var wrong in new[] { "offset", "tag", "length" })
        {
            using var fixture = new Fixture();
            var handler = new Handler((_, index, _) => Task.FromResult(index switch
            {
                1 => Response("abc", declared: 6),
                2 => wrong switch
                {
                    "offset" => Partial("xyz", 0, 2, 3),
                    "tag" => Partial("def", 3, 5, 6, "\"v2\""),
                    _ => Partial("defgh", 3, 7, 8)
                },
                _ => Response("replacement", tag: "\"v2\"")
            }));
            using var http = new MediaHttp(handler);
            var downloader = new FileDownloader(http);
            await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
            await downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0);
            Assert(File.ReadAllText(fixture.Output) == "replacement", wrong + " produced a corrupt file.");
            Assert(handler.Requests.Count == 3 && handler.Requests[2].Range is null, "Invalid range did not restart.");
        }
    }

    private static async Task UnsatisfiedRange()
    {
        using var fixture = new Fixture();
        var handler = new Handler((_, index, _) =>
        {
            var response = index switch
            {
                1 => Response("abc", declared: 6),
                2 => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) },
                _ => Response("abcdef")
            };
            if (index == 2) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(6);
            return Task.FromResult(response);
        });
        using var http = new MediaHttp(handler);
        var downloader = new FileDownloader(http);
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
        await downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0);
        Assert(File.ReadAllText(fixture.Output) == "abcdef", "An incomplete partial file was accepted as complete.");
        Assert(handler.Requests.Count == 3 && handler.Requests[2].Range is null, "416 did not fetch the full entity.");
    }

    private static async Task UnidentifiedPartial()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Output + ".part", "unverified");
        var handler = new Handler((_, _, _) => Task.FromResult(Response("fresh")));
        using var http = new MediaHttp(handler);
        await new FileDownloader(http).DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0);
        Assert(handler.Requests[0].Range is null && File.ReadAllText(fixture.Output) == "fresh", "Unverified bytes were reused.");
    }

    private static async Task ChangedResource()
    {
        using var fixture = new Fixture();
        var handler = new Handler((_, index, _) => Task.FromResult(index == 1 ? Response("abc", declared: 6) : Response("other")));
        using var http = new MediaHttp(handler);
        var downloader = new FileDownloader(http);
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
        await downloader.DownloadAsync(new Uri("https://media.example/other.mp4"), fixture.Output, null, null, default, 0);
        Assert(handler.Requests[1].Range is null && File.ReadAllText(fixture.Output) == "other", "Different resources were concatenated.");
    }

    private static async Task Cancellation()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Output, "previous result");
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler((_, _, _) =>
        {
            var response = Response("");
            response.Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes("abcdef"), 3));
            response.Content.Headers.ContentLength = 6;
            return Task.FromResult(response);
        });
        using var http = new MediaHttp(handler);
        await Expect<OperationCanceledException>(() => new FileDownloader(http).DownloadAsync(fixture.Url, fixture.Output,
            null, new InlineProgress(_ => cancellation.Cancel()), cancellation.Token, 0));
        Assert(File.ReadAllText(fixture.Output) == "previous result", "Cancellation replaced an existing output.");
        Assert(File.ReadAllText(fixture.Output + ".part") == "abc", "Cancellation lost partial data.");
    }

    private static async Task HttpFailure()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Output, "previous result");
        var handler = new Handler((_, index, _) => Task.FromResult(index == 1 ? Response("abc", declared: 6)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new ByteArrayContent([]) }));
        using var http = new MediaHttp(handler);
        var downloader = new FileDownloader(http);
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
        await Expect<IOException>(() => downloader.DownloadAsync(fixture.Url, fixture.Output, null, null, default, 0));
        Assert(File.ReadAllText(fixture.Output) == "previous result" && File.ReadAllText(fixture.Output + ".part") == "abc", "An HTTP error changed files.");
        Assert(handler.Requests.Count == 2, "Authentication failures were incorrectly treated as ignored ranges.");
    }

    private static async Task DiscoveryLimits()
    {
        var stream = new ChunkStream(new byte[50000], 8192);
        using var http = new MediaHttp(new Handler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        })));
        await Expect<InvalidDataException>(() => http.GetBytesAsync(new Uri("https://media.example/asset"), 1024, default));
        Assert(stream.BytesRead <= 8192, "The entire oversized body was buffered before checking its size.");
        using var unicodeHttp = new MediaHttp(new Handler((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ChunkStream(Encoding.UTF8.GetBytes(new string('\u754c', 100)), 300))
        })));
        await Expect<InvalidDataException>(() => unicodeHttp.GetStringAsync(new Uri("https://media.example/page"), 200, default));
    }

    private static async Task UnsupportedHls()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Output, "previous result");
        var handler = new Handler((_, _, _) => throw new InvalidOperationException("Unsupported manifests must not download."));
        using var http = new MediaHttp(handler);
        var tags = new[]
        {
            "#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"key\"", "#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"key,METHOD=NONE\"",
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Audio\",URI=\"audio.m3u8\"", "#EXT-X-GAP"
        };
        foreach (var tag in tags)
        {
            var playlist = HlsPlaylistParser.Parse("#EXTM3U\n" + tag + "\nsegment.ts\n#EXT-X-ENDLIST\n", fixture.Url);
            await Expect<NotSupportedException>(() => new HlsDownloader(http).DownloadAsync(playlist, fixture.Output, null, default));
        }
        var live = HlsPlaylistParser.Parse("#EXTM3U\nsegment.ts\n", fixture.Url);
        var changedTimeline = HlsPlaylistParser.Parse("#EXTM3U\n#EXTINF:1,\na.ts\n#EXT-X-DISCONTINUITY\n#EXTINF:1,\nb.ts\n#EXT-X-ENDLIST", fixture.Url);
        await Expect<NotSupportedException>(() => new HlsDownloader(http).DownloadAsync(changedTimeline, fixture.Output, null, default));
        await Expect<NotSupportedException>(() => new HlsDownloader(http).DownloadAsync(live, fixture.Output, null, default));
        Assert(handler.Requests.Count == 0 && File.ReadAllText(fixture.Output) == "previous result", "An unsupported HLS playlist was published.");
    }

    private static async Task ConcurrentHls()
    {
        using var fixture = new Fixture();
        var handler = new Handler(async (request, _, _) =>
        {
            await Task.Yield();
            return Response(request.RequestUri!.AbsolutePath.Trim('/'));
        });
        using var http = new MediaHttp(handler);
        var first = HlsPlaylistParser.Parse("#EXTM3U\na1.ts\na2.ts\n#EXT-X-ENDLIST\n", new Uri("https://media.example/"));
        var second = HlsPlaylistParser.Parse("#EXTM3U\nb1.ts\nb2.ts\n#EXT-X-ENDLIST\n", new Uri("https://media.example/"));
        var other = Path.Combine(fixture.Directory, "other.ts");
        await Task.WhenAll(new HlsDownloader(http).DownloadAsync(first, fixture.Output, null, default, retries: 0),
            new HlsDownloader(http).DownloadAsync(second, other, null, default, retries: 0));
        Assert(File.ReadAllText(fixture.Output) == "a1.tsa2.ts" && File.ReadAllText(other) == "b1.tsb2.ts", "Concurrent downloads mixed segment files.");
        Assert(Directory.GetDirectories(fixture.Directory).Length == 0, "HLS staging was not removed.");
    }

    private static async Task UnsupportedDash()
    {
        foreach (var body in new[]
        {
            "<SegmentBase/>", "<ContentProtection schemeIdUri=\"test\"/>"
        })
            await Expect<NotSupportedException>(() => Task.Run(() => DashManifestParser.Parse(
                "<MPD><Period><AdaptationSet>" + body + "<Representation><BaseURL>https://media.example/file.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>",
                new Uri("https://media.example/manifest.mpd"))));
        var withoutMedia = DashManifestParser.Parse("<MPD><Period><AdaptationSet><Representation id=\"missing\"/></AdaptationSet></Period></MPD>",
            new Uri("https://media.example/manifest.mpd"));
        Assert(withoutMedia.Representations.Count == 0, "The manifest itself became a media representation.");
    }

    private static Task DashBaseUrls()
    {
        var manifest = DashManifestParser.Parse("<MPD><BaseURL>../media/</BaseURL><Period><BaseURL>episode/</BaseURL><AdaptationSet contentType=\"video\"><Representation id=\"1\"><BaseURL>clip.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>",
            new Uri("https://media.example/manifests/main.mpd"));
        var representation = manifest.Representations.Single();
        Assert(representation.Url.AbsoluteUri == "https://media.example/media/episode/clip.mp4" && representation.IsVideo, "The DASH base address or type was lost.");
        return Task.CompletedTask;
    }

    private static async Task UnsupportedOptions()
    {
        using var fixture = new Fixture();
        var engine = new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Unsupported options must be checked before HTTP."));
        foreach (var request in new[]
        {
            new NativeDownloadRequest { Url = fixture.Url, OutputDirectory = fixture.Directory, BrowserCookies = "safari" }
        })
            await Expect<NotSupportedException>(() => engine.DownloadAsync(request, null, null, default));
    }

    private static async Task ProviderPriority()
    {
        using var fixture = new Fixture();
        var registry = new MediaProviderRegistry([new GenericPageProvider(), new FixtureProvider()]);
        Assert(registry.Resolve(new Uri("https://media.example/watch")) is FixtureProvider, "Generic extraction masked a dedicated provider.");
        IMediaEngine engine = new NativeEngine([new GenericPageProvider(), new FixtureProvider()],
            _ => new MediaHttp(new Handler((_, _, _) => throw new InvalidOperationException("The fixture provider needs no HTTP."))));
        var info = await engine.InspectAsync(fixture.Request, default);
        Assert(info.Extractor == "fixture" && info.Formats.Single().Url == fixture.Url, "Provider metadata was lost.");
        await Expect<ArgumentException>(() => Task.Run(() => new MediaProviderRegistry([new FixtureProvider(), new FixtureProvider()])));
    }

    private static async Task MissingProvider()
    {
        using var fixture = new Fixture();
        var engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler((_, _, _) => throw new InvalidOperationException("No external fallback is allowed."))));
        await Expect<NotSupportedException>(() => engine.InspectAsync(new NativeDownloadRequest
        {
            Url = new Uri("https://www.twitch.tv/fixture"), OutputDirectory = fixture.Directory
        }, default));
        Assert(!NativeEngine.CanHandle(new Uri("https://www.twitch.tv/fixture")), "Twitch was claimed without a provider.");
        Assert(!NativeEngine.CanHandle(new Uri("relative", UriKind.Relative)), "A relative address was accepted.");
    }

    private static async Task NativeDownload()
    {
        using var fixture = new Fixture();
        IMediaEngine engine = new NativeEngine(httpFactory: _ => new MediaHttp(new Handler((_, _, _) => Task.FromResult(Response("media bytes")))));
        var result = await engine.DownloadAsync(fixture.Request, null, null, default);
        Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "media bytes", "The independent C# path did not complete.");
    }

    private static HttpResponseMessage Response(string text, long? declared = null, string tag = "\"v1\"")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(text)) };
        response.Headers.ETag = EntityTagHeaderValue.Parse(tag);
        if (declared is not null) response.Content.Headers.ContentLength = declared;
        return response;
    }

    private static HttpResponseMessage Partial(string text, long from, long to, long total, string tag = "\"v1\"")
    {
        var response = Response(text, tag: tag);
        response.StatusCode = HttpStatusCode.PartialContent;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
        return response;
    }

    private static async Task Expect<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Directory, "output.mp4");
        public Uri Url { get; } = new("https://media.example/clip.mp4");
        public NativeDownloadRequest Request => new() { Url = Url, OutputDirectory = Directory };
        public Fixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory)) File.Delete(file);
            System.IO.Directory.Delete(Directory);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<(long? Range, string? Validator)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int count;
            lock (Requests)
            {
                Requests.Add((request.Headers.Range?.Ranges.First().From, request.Headers.IfRange?.ToString()));
                count = Requests.Count;
            }
            return response(request, count, ct);
        }
    }

    private sealed class InlineProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => action(value);
    }

    private sealed class FixtureProvider : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("fixture", "Test provider", 200);
        public bool CanHandle(Uri url) => url.Host == "media.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => Task.FromResult(new PageMetadata
        {
            Extractor = Info.Id, Title = "Fixture", Media = [new MediaCandidate { Url = request.Url, Extension = "mp4", Kind = MediaKind.Progressive }]
        });
    }

    private sealed class ChunkStream(byte[] bytes, int chunkSize) : Stream
    {
        private int _position;
        public int BytesRead => _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(count, chunkSize), bytes.Length - _position);
            bytes.AsSpan(_position, take).CopyTo(buffer.AsSpan(offset, take));
            _position += take;
            return take;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var take = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - _position);
            bytes.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return ValueTask.FromResult(take);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
