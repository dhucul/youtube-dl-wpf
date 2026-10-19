using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

internal static partial class ProviderFamiliesChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> ClockChecks => new[] { "live-small", "live-boundary", "live-large", "static-small", "duration", "range", "cipher", "lost-clock", "refresh-small", "static-recovery" }.Select(mode => ("Provider families live clock " + mode, (Func<Task>)(() => Clock(mode))));
    private static async Task Clock(string mode)
    {
        var graph = new AdaptivePlaybackGraph(); var root = new Uri("https://clock.example/master.m3u8"); var segment = new Uri(root, "segment.m4s");
        var oldText = Manifest(0, mode, false); graph.Observe("root", MediaKind.Hls, root, root, oldText); var identity = graph.RecoveryIdentity(segment);
        var offset = mode switch { "live-boundary" => 50, "live-large" => 51, "static-recovery" => 0, _ => 10 };
        var next = Manifest(offset, mode, true); var allowed = mode is "live-small" or "live-boundary" or "refresh-small" or "static-recovery";
        if (allowed)
        {
            if (mode == "refresh-small") (await graph.PrepareRefreshAsync(new Dictionary<string, Uri> { ["root"] = root }, (_, _) => Task.FromResult(new MediaDocument(root, next, "application/vnd.apple.mpegurl")), default))();
            else graph.Observe("root", MediaKind.Hls, root, root, next);
        }
        else await Expect<InvalidDataException>(() => { graph.Observe("root", MediaKind.Hls, root, root, next); return Task.CompletedTask; });
        Assert(graph.RecoveryIdentity(segment) == identity && graph.Resolve(segment).Url == segment, "Clock validation changed an established source or recovery binding.");
    }
    private static string Manifest(int offset, string mode, bool next)
    {
        var date = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(offset).ToString("O");
        var key = mode == "cipher" ? "#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x" + (next ? "00000000000000000000000000000001" : "00000000000000000000000000000000") + "\n" : "";
        return "#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:7\n" + key + "#EXT-X-MAP:URI=\"init.mp4\"\n" + (mode == "lost-clock" && next ? "" : "#EXT-X-PROGRAM-DATE-TIME:" + date + "\n") +
            "#EXTINF:" + (mode == "duration" && next ? "2.006" : "2.00533") + ",\n" + (mode == "range" && next ? "#EXT-X-BYTERANGE:4@0\n" : "") + "segment.m4s\n" + (mode.StartsWith("static", StringComparison.Ordinal) ? "#EXT-X-ENDLIST\n" : "");
    }
}
