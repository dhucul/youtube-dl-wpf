using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;

internal static partial class RadioCompletionChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> GeoChecks => new[] { "countries", "cidr", "invalid", "precedence", "disable", "analysis", "privacy", "sessions", "explicit-header", "real-http", "denied" }.Select(mode => ("Radio completion geo " + mode, (Func<Task>)(() => Geo(mode))))
        .Concat(new[] { "prefix", "complete", "wrong-tail", "two-bytes", "truncated", "changed-profile", "tagged" }.Select(mode => ("Radio completion MP3 boundary " + mode, (Func<Task>)(() => Boundary(mode)))));
    private static NativeDownloadRequest BaseRequest(string directory = "unused") => new() { Url = new("https://owned.example/source"), OutputDirectory = directory, NoCache = true };
    private static async Task Geo(string mode)
    {
        var request = BaseRequest();
        if (mode == "countries")
        {
            Assert(GeoCountryRanges.Blocks.Count == 240, "The frozen geographic header table lost entries.");
            foreach (var item in GeoCountryRanges.Blocks) { var prepared = NativeGeoVerification.Prepare(request with { GeoVerification = new() { ForwardedForMode = item.Key } }); Assert(InBlock(prepared.GeoVerification!.SelectedAddress!, item.Value), "The chosen header address is outside its frozen country range."); }
            return;
        }
        if (mode == "cidr")
        {
            foreach (var block in new[] { "0.0.0.0/0", "8.0.0.0/8", "8.8.8.0/24", "8.8.8.8/32", "8.8.8.129/25" }) for (var i = 0; i < 10; i++)
            {
                var prepared = NativeGeoVerification.Prepare(request with { GeoVerification = new() { ForwardedForMode = block } }); Assert(InBlock(prepared.GeoVerification!.SelectedAddress!, block), "CIDR forwarding escaped its block."); Assert(NativeGeoVerification.Prepare(prepared).GeoVerification!.SelectedAddress == prepared.GeoVerification.SelectedAddress, "A prepared job changed its forwarded address.");
            }
            return;
        }
        if (mode == "invalid")
        {
            foreach (var value in new[] { "ZZ", "USA", "", "8.8.8.8", "8.8.8.0/33", "8.8.8.0/-1", "256.8.8.0/24", "::1/128", "8.8.8.0/24\r\nOwned:x", "8.8/16" })
            { try { NativeExtraArguments.Apply(request, ["--xff", value]); throw new Exception("Accepted invalid geographic header options."); } catch (ArgumentException ex) { Assert(!ex.Message.Contains(value, StringComparison.Ordinal) || value.Length == 0, "Invalid geographic options leaked their value."); } }
            return;
        }
        if (mode == "precedence")
        {
            var mapped = NativeExtraArguments.Apply(request, ["--no-geo-bypass", "--geo-bypass-country", "PL", "--geo-bypass-ip-block", "8.8.8.8/32"]); Assert(NativeGeoVerification.Prepare(mapped).GeoVerification!.SelectedAddress == "8.8.8.8", "The last explicit geographic mode did not win.");
            Assert(NativeGeoVerification.FromLegacy(null, false, "PL", "8.8.8.8/32")!.ForwardedForMode == "8.8.8.8/32", "WPF classic precedence changed.");
            mapped = NativeExtraArguments.Apply(mapped, ["--geo-bypass-country", "fr", "--geo-verification-proxy", "http://127.0.0.1:1234"]); Assert(mapped.GeoVerification!.ForwardedForMode == "FR" && mapped.GeoVerification.Proxy.EndsWith(":1234", StringComparison.Ordinal), "Verification proxy discarded the forwarded country."); return;
        }
        if (mode == "disable")
        {
            foreach (var flag in new[] { "--no-geo-bypass", "--geo-bypass" }) { var mapped = NativeExtraArguments.Apply(request, ["--geo-bypass-country", "PL", flag]); Assert(NativeGeoVerification.Prepare(mapped).GeoVerification!.SelectedAddress is null, "Default/never mode retained a forced address."); }
            foreach (var value in new[] { "DEFAULT", "NEVER" }) Assert(NativeGeoVerification.Prepare(NativeExtraArguments.Apply(request, ["--xff", value])).GeoVerification!.SelectedAddress is null, "Geographic mode names lost case-insensitive handling.");
            Assert(NativeGeoVerification.FromLegacy(null, true, null, null) is null, "Default geographic behavior changed request state."); return;
        }
        if (mode == "analysis")
        {
            var selected = NativeAnalysisArguments.Select(["--xff", "8.8.8.8/32", "--audio-format", "flac", "--no-geo-bypass"]); var mapped = NativeExtraArguments.Apply(request, selected); Assert(mapped.GeoVerification!.ForwardedForMode == "never", "Analysis lost geographic header arity or ordering."); return;
        }
        if (mode == "privacy")
        {
            var prepared = NativeGeoVerification.Prepare(request with { GeoVerification = new() { ForwardedForMode = "8.8.8.8/32" } }); var json = JsonSerializer.Serialize(prepared); Assert(!json.Contains("8.8.8.8", StringComparison.Ordinal) && !prepared.GeoVerification!.ToString().Contains("8.8.8.8", StringComparison.Ordinal), "Geographic connection options entered request metadata."); return;
        }
        if (mode is "sessions" or "explicit-header")
        {
            var headers = new List<string?>(); var options = new MediaHttpOptions { AllowAutoRedirect = false, ForwardedFor = "8.8.8.8" }; if (mode == "explicit-header") options.Headers["X-Forwarded-For"] = "9.9.9.9";
            using var owner = new MediaHttp(new GeoHandler(headers), options); using var verification = owner.CreateVerificationSession(null); using var service = owner.CreatePublicServiceSession(); using var anonymous = owner.CreateAnonymousSession();
            foreach (var client in new[] { owner, verification, service, anonymous }) { using var message = new HttpRequestMessage(HttpMethod.Get, "https://owned.example/source"); using var response = await client.SendAsync(message, default); }
            Assert(headers.SequenceEqual(mode == "sessions" ? new string?[] { "8.8.8.8", "8.8.8.8", null, "8.8.8.8" } : ["9.9.9.9", "8.8.8.8", null, "9.9.9.9"]), "Geographic headers crossed service boundaries or lost explicit source precedence."); return;
        }
        using var files = new ExecSponsorChecks.Files(); await using var server = new GeoServer { Deny = mode == "denied" }; var engine = new NativeEngine([new GeoProvider(server)]); var real = BaseRequest(files.Directory) with { Url = new(server.Root, "page"), GeoVerification = new() { ForwardedForMode = "8.8.8.8/32" } };
        if (mode == "denied") await ProviderFamiliesChecks.Expect<HttpRequestException>(() => engine.InspectAsync(real, default));
        else { var info = await engine.InspectAsync(real, default); Assert(info.Title == "Owned forwarded radio" && server.Headers.Single().Forwarded == "8.8.8.8", "Actual native HTTP analysis lost its forwarded header."); }
        Assert(!File.Exists(Path.Combine(files.Directory, "archive.txt")), "Denied analysis modified an archive.");
    }
    private static bool InBlock(string text, string block)
    {
        var split = block.Split('/'); var bits = int.Parse(split[1], CultureInfo.InvariantCulture); var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits); return (BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(text).GetAddressBytes()) & mask) == (BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(split[0]).GetAddressBytes()) & mask);
    }
    private sealed class GeoHandler(List<string?> observed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { observed.Add(request.Headers.TryGetValues("X-Forwarded-For", out var values) ? values.Single() : null); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("owned") }); }
    }
    private static async Task Boundary(string mode)
    {
        using var files = new ExecSponsorChecks.Files(); var path = Path.Combine(files.Directory, "owned.mp3"); var frame = new byte[576]; new byte[] { 0xff, 0xfb, 0xb4, 0x64 }.CopyTo(frame, 0); var body = frame.Concat(frame).ToArray();
        if (mode == "tagged") body = "ID3\x04\0\0\0\0\0\0"u8.ToArray().Concat(body).ToArray();
        if (mode == "changed-profile") body[576 + 2] = 0xb8;
        body = mode switch { "complete" => body, "wrong-tail" => body.Concat(new byte[] { 1 }).ToArray(), "two-bytes" => body.Concat(new byte[] { 0xff, 0xfb }).ToArray(), "truncated" => body[..^4].Concat(new byte[] { 0xff }).ToArray(), _ => body.Concat(new byte[] { 0xff }).ToArray() };
        await File.WriteAllBytesAsync(path, body);
        if (mode is "wrong-tail" or "two-bytes" or "truncated" or "changed-profile") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => Mp3ClipBoundary.NormalizeAsync(path, default)); Assert(File.ReadAllBytes(path).SequenceEqual(body), "Refused MP3 normalization changed its source."); return; }
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(path, stamp); var changed = await Mp3ClipBoundary.NormalizeAsync(path, default); var expected = mode is "prefix" or "tagged";
        Assert(changed == expected && File.ReadAllBytes(path).SequenceEqual(expected ? body[..^1] : body), "MP3 normalization changed anything beyond its one proven non-frame sync prefix.");
        Assert(File.GetLastWriteTimeUtc(path) == stamp, "MP3 clipping normalization lost the source modification timestamp.");
    }
    internal sealed class GeoProvider(GeoServer server) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("owned-forwarded-radio", "Owned forwarded radio", 500);
        public bool CanHandle(Uri uri) => uri.Host == "127.0.0.1";
        public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(server.Root, "metadata")), ct); response.EnsureSuccessStatusCode();
            return new() { Id = "owned-radio", Extractor = Info.Id, Title = "Owned forwarded radio", DurationSeconds = 3, Media = [new() { Id = "audio", Url = new(server.Root, "audio.mp3"), Kind = MediaKind.Progressive, Extension = "mp3", HasAudio = true, HasVideo = false, AudioCodec = "mp3", VerificationDemuxer = "mp3", VerificationDurationSeconds = 3, Source = Info.Id }] };
        }
    }
    internal sealed class GeoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(); private readonly Task _loop;
        internal Uri Root { get; } internal byte[] Data = "owned"u8.ToArray(); internal bool Deny; internal List<(string Path, string? Forwarded)> Headers = [];
        internal GeoServer() { _listener.Start(); Root = new("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/"); _loop = Loop(); }
        private async Task Loop()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token); await using var stream = client.GetStream(); var bytes = new List<byte>(); var one = new byte[1];
                    while (bytes.Count < 16384 && await stream.ReadAsync(one, _stop.Token) == 1) { bytes.Add(one[0]); if (bytes.Count >= 4 && bytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break; }
                    var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n"); var path = lines[0].Split(' ')[1]; var header = lines.FirstOrDefault(line => line.StartsWith("X-Forwarded-For:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim(); lock (Headers) Headers.Add((path, header));
                    var body = path.StartsWith("/audio.mp3", StringComparison.Ordinal) ? Data : "owned metadata"u8.ToArray(); var status = Deny ? "403 Forbidden" : "200 OK";
                    var prefix = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: audio/mpeg\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n"); await stream.WriteAsync(prefix, _stop.Token); await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or SocketException or IOException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)); } finally { _stop.Dispose(); } }
    }
}
