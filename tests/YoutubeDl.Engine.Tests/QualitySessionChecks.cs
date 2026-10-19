using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using static SabrFixture;

internal static class QualitySessionChecks
{
    private static void Assert(bool value, string message) => SabrFixture.Assert(value, message);
    public static IEnumerable<(string Name, Func<Task> Test)> All => [("Quality bootstrap native delivery preserves initial state and adopts only renewed state", Delivery)];
    private static async Task Delivery()
    {
        using var files = new Files(); var calls = 0; var renewals = 0;
        var session = new SabrPlaybackSession(VideoId, Endpoint, 1, "fixture", [1])
        {
            InitialPlaybackCookie = [71], InitialContexts = new Dictionary<uint, byte[]> { [7] = [72] }, ClientAbrState = QualityBootstrapChecks.State(),
            Renewal = (previous, _) => { renewals++; return Task.FromResult(previous with { InitialPlaybackCookie = [81], InitialContexts = new Dictionary<uint, byte[]> { [8] = [82] } }); }
        };
        using var http = new YoutubeDl.Engine.Http.MediaHttp(new Handler(async request =>
        {
            var fields = ProtobufReader.Read(await request.Content!.ReadAsByteArrayAsync()); var context = ProtobufReader.Read(fields.Single(f => f.Number == 19).Bytes);
            var state = ProtobufReader.Read(fields.Single(f => f.Number == 1).Bytes);
            Assert(state.Single(f => f.Number == 16).Integer == 144 && state.Single(f => f.Number == 18).Integer == 256 && state.Single(f => f.Number == 71).Integer == 17, "Native delivery dropped capability fields or the selected geometry.");
            var cookie = context.Single(f => f.Number == 3).Bytes.ToArray(); var contexts = context.Where(f => f.Number == 5).Select(f => ProtobufReader.Read(f.Bytes).Single(v => v.Number == 1).Integer).ToArray();
            using var body = new MemoryStream();
            if (calls++ == 0)
            {
                Assert(cookie.SequenceEqual(new byte[] { 71 }) && contexts.SequenceEqual(new ulong[] { 7 }), "Initial observed playback state was not sent.");
                var policy = new ProtobufWriter(); policy.UInt64(3, 7); Part(body, 59, policy.ToArray());
                Metadata(body, Video, 2, 1000); Segment(body, Video, 1, 0, [1]); Segment(body, Video, 2, 1, [2], new(0, 500, 1000));
            }
            else if (calls == 2)
            {
                Assert(cookie.SequenceEqual(new byte[] { 71 }) && contexts.Length == 0, "Initial contexts were resurrected after the server discarded them.");
                Part(body, 46, []);
            }
            else
            {
                Assert(cookie.SequenceEqual(new byte[] { 81 }) && contexts.SequenceEqual(new ulong[] { 8 }), "Reload kept the old session bootstrap.");
                Assert(fields.Count(f => f.Number == 3) == 1, "Reload forgot completed selected media.");
                Metadata(body, Video, 2, 1000); Segment(body, Video, 3, 2, [3], new(500, 500, 1000));
            }
            return Response(body.ToArray());
        }), new() { AllowAutoRedirect = false });
        var path = Path.Combine(files.Directory, "selected.mp4");
        await SabrDownloader.DownloadAsync(http, new MediaCandidate { Url = Endpoint, Id = "137", Kind = MediaKind.Sabr, Sabr = new(session, Video), Width = 256, Height = 144, Extension = "mp4", HasVideo = true }, null,
            path, new NativeDownloadRequest { Url = new("https://youtu.be/" + VideoId), OutputDirectory = files.Directory, NoContinue = true }, null, default);
        Assert(calls == 3 && renewals == 1 && (await File.ReadAllBytesAsync(path)).SequenceEqual(new byte[] { 1, 2, 3 }), "Native quality/session delivery did not complete once with exact selected bytes.");
    }
}
