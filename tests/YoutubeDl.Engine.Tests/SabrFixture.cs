using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

// Independently authored frames used by protocol and end-to-end engine checks.
internal static class SabrFixture
{
    internal const string VideoId = "abcdefghijk";
    internal static readonly SabrFormatId Video = new(137, 100);
    internal static readonly SabrFormatId Audio = new(140, 200);
    internal static readonly Uri Endpoint = new("https://r1.googlevideo.com/videoplayback?fixture=sabr");

    internal static JsonNode Player()
    {
        var node = JsonNode.Parse(YouTubeChecks.FixtureJson)!;
        var formats = node["streamingData"]!["adaptiveFormats"]!.AsArray();
        while (formats.Count > 2) formats.RemoveAt(formats.Count - 1);
        for (var index = 0; index < formats.Count; index++)
        {
            formats[index]!.AsObject().Remove("url");
            formats[index]!["lastModified"] = index == 0 ? "100" : "200";
        }
        node["streamingData"]!["formats"] = new JsonArray();
        node["playerConfig"] = JsonNode.Parse("""{"mediaCommonConfig":{"mediaUstreamerRequestConfig":{"videoPlaybackUstreamerConfig":"AQID"}}}""");
        return node;
    }
    internal static string Watch => "var ytInitialPlayerResponse=" + Player().ToJsonString() + ";";
    internal static HttpResponseMessage Response(byte[] bytes)
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.yt-ump");
        return result;
    }
    internal static void Status(Stream output, uint value)
    { var writer = new ProtobufWriter(); writer.UInt64(1, value); Part(output, 58, writer.ToArray()); }
    internal static void Metadata(Stream output, SabrFormatId format, uint last, ulong ticks, uint scale = 1000)
    {
        var writer = new ProtobufWriter(); writer.String(1, VideoId); writer.Message(2, nested => Format(nested, format));
        writer.UInt64(4, last); writer.UInt64(9, ticks); writer.UInt64(10, scale); Part(output, 42, writer.ToArray());
    }
    internal static void Segment(Stream output, SabrFormatId format, byte id, uint number, byte[] data,
        SabrTimeRange? time = null, bool complete = true)
    {
        var header = Header(format, id, number, data.Length, time);
        Part(output, 20, header); Part(output, 21, new byte[] { id }.Concat(data).ToArray());
        if (complete) Part(output, 22, [id]);
    }
    internal static byte[] Header(SabrFormatId format, byte id, uint number, int length, SabrTimeRange? time = null)
    {
        var header = new ProtobufWriter(); header.UInt64(1, id); header.String(2, VideoId);
        header.Message(13, nested => Format(nested, format)); header.UInt64(8, number == 0 ? 1UL : 0);
        header.UInt64(9, number); header.UInt64(14, (ulong)length);
        if (time is not null) header.Message(15, nested =>
        { nested.UInt64(1, (ulong)time.StartTicks); nested.UInt64(2, (ulong)time.DurationTicks); nested.UInt64(3, (ulong)time.Timescale); });
        return header.ToArray();
    }
    private static void Format(ProtobufWriter output, SabrFormatId format)
    {
        output.UInt64(1, format.Itag); output.UInt64(2, format.LastModified);
        if (format.ExtraTags.Length > 0) output.String(3, format.ExtraTags);
    }
    internal static void Part(Stream output, byte type, byte[] payload)
    {
        output.WriteByte(type);
        var size = (uint)payload.Length;
        var length = size < 128 ? 1 : size < 16384 ? 2 : size < 2097152 ? 3 : size < 268435456 ? 4 : 5;
        if (length == 5)
        {
            output.WriteByte(0xf0);
            for (var index = 0; index < 4; index++) output.WriteByte((byte)(size >> (index * 8)));
        }
        else
        {
            var bits = 8 - length;
            var prefix = (0xffu << (9 - length)) & 0xff;
            output.WriteByte((byte)(prefix | (size & ((1u << bits) - 1))));
            size >>= bits;
            for (var index = 1; index < length; index++) { output.WriteByte((byte)size); size >>= 8; }
        }
        output.Write(payload);
    }
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }
    internal sealed class Files : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "native-sabr-test-" + Guid.NewGuid().ToString("N"));
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
    internal static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    internal static async Task Expect<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
