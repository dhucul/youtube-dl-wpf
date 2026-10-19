using System.Text.Json;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;

internal static class QualityBootstrapChecks
{
    private static readonly string[] Modes = ["capture", "copy", "privacy", "capabilities", "mutable", "duplicate-cookie", "duplicate-context", "large-cookie", "bad-state", "seed-policy", "seed-renewal", "seed-limits"];
    public static IEnumerable<(string Name, Func<Task> Test)> All => Modes.Select(mode => ("Quality bootstrap " + mode, (Func<Task>)(() => Contract(mode))));
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static byte[] State()
    {
        var w = new ProtobufWriter(); w.UInt64(16, 1080); w.UInt64(18, 1920); w.UInt64(19, 1080); w.UInt64(28, 2000); w.UInt64(36, 9000); w.UInt64(40, 3);
        w.UInt64(71, 17); w.Bytes(72, [3, 4, 5]); w.Field(new(73, 1, 0xfedcba9876543210, default)); w.Field(new(74, 5, 0x7fc12345, default)); return w.ToArray();
    }
    internal static SabrRequest Request() => ObservedYouTubeChecks.Request() with { ClientAbrState = State(), PlaybackCookie = [71, 72], Contexts = new Dictionary<uint, byte[]> { [7] = [81, 82] } };
    private static async Task Contract(string mode)
    {
        if (mode is "duplicate-cookie" or "duplicate-context" or "large-cookie")
        {
            var fields = ProtobufReader.Read(Request().Encode()); var w = new ProtobufWriter();
            foreach (var field in fields.Where(field => field.Number != 19)) w.Field(field);
            w.Message(19, context => { foreach (var field in ProtobufReader.Read(fields.Single(field => field.Number == 19).Bytes)) context.Field(field); if (mode == "duplicate-cookie") context.Bytes(3, [1]); else if (mode == "large-cookie") context.Bytes(3, new byte[16385]); else context.Message(5, row => { row.UInt64(1, 7); row.Bytes(2, [9]); }); });
            await ProviderFamiliesChecks.Expect<InvalidDataException>(() => Task.FromResult(ObservedYouTubeChecks.Parse(w.ToArray()))); return;
        }
        if (mode == "bad-state")
        {
            var w = new ProtobufWriter(); w.UInt64(16, 1); w.UInt64(16, 2);
            await ProviderFamiliesChecks.Expect<InvalidDataException>(() => Task.FromResult((Request() with { ClientAbrState = w.ToArray() }).Encode())); return;
        }
        if (mode.StartsWith("seed-", StringComparison.Ordinal))
        {
            var state = new SabrControlState(SabrFixture.VideoId); var cookie = new byte[] { 1, 2 }; var values = new Dictionary<uint, byte[]> { [7] = [3, 4] };
            if (mode == "seed-limits") { await ProviderFamiliesChecks.Expect<InvalidDataException>(() => { state.SeedSession(new byte[16385], values); return Task.CompletedTask; }); return; }
            state.SeedSession(cookie, values); cookie[0] = 9; values[7][0] = 9; Assert(state.PlaybackCookie[0] == 1 && state.OutgoingContexts[7][0] == 3, "Bootstrap state retained borrowed buffers.");
            var policy = new ProtobufWriter(); policy.UInt64(3, 7); state.Accept(new(59, policy.ToArray())); state.SeedSession([8], new Dictionary<uint, byte[]> { [7] = [8] });
            Assert(!state.OutgoingContexts.ContainsKey(7) && state.PlaybackCookie[0] == 1, "An initial context was resurrected after server policy removed it.");
            if (mode == "seed-renewal") { state.ResetSession(); state.SeedSession([5], new Dictionary<uint, byte[]> { [8] = [6] }); Assert(state.PlaybackCookie[0] == 5 && state.OutgoingContexts.Keys.SequenceEqual(new uint[] { 8 }), "Renewal reused the old generation's bootstrap."); } return;
        }
        var body = Request().Encode(); var page = ObservedYouTubeChecks.Parse(body); var session = page.Media[0].Sabr!.Session;
        if (mode == "capture") { Assert(session.InitialPlaybackCookie.SequenceEqual(new byte[] { 71, 72 }) && session.InitialContexts[7].SequenceEqual(new byte[] { 81, 82 }) && session.ClientAbrState.Length > 0, "Observed browser bootstrap was discarded."); return; }
        if (mode == "copy") { Array.Fill(body, (byte)0); Assert(session.InitialPlaybackCookie[0] == 71 && session.InitialContexts[7][0] == 81 && ProtobufReader.Read(session.ClientAbrState).Any(field => field.Number == 71), "Caller mutation changed the captured state."); return; }
        if (mode == "privacy") { using var json = JsonDocument.Parse(JsonSerializer.Serialize(session)); Assert(!json.RootElement.TryGetProperty("ClientAbrState", out _) && !json.RootElement.TryGetProperty("InitialContexts", out _) && !json.RootElement.TryGetProperty("InitialPlaybackCookie", out _), "Bootstrap secrets entered serialized output."); return; }
        var encoded = ProtobufReader.Read((Request() with { ClientAbrState = session.ClientAbrState, Width = 256, Height = 144, PlaybackTimeMs = 0, ElapsedTimeMs = 25 }).Encode()); var abr = ProtobufReader.Read(encoded.Single(field => field.Number == 1).Bytes);
        if (mode == "capabilities")
        {
            var original = ProtobufReader.Read(State()).Where(field => field.Number >= 71).ToArray(); var actual = abr.Where(field => field.Number >= 71).ToArray();
            Assert(original.Length == actual.Length && original.Zip(actual).All(pair => pair.First.Number == pair.Second.Number && pair.First.WireType == pair.Second.WireType && pair.First.Integer == pair.Second.Integer && pair.First.Bytes.Span.SequenceEqual(pair.Second.Bytes.Span)), "Unknown codec capability fields changed or disappeared."); return;
        }
        Assert(abr.Single(field => field.Number == 16).Integer == 144 && abr.Single(field => field.Number == 18).Integer == 256 && abr.Single(field => field.Number == 28).Integer == 0 && abr.Single(field => field.Number == 36).Integer == 25, "Selected geometry and current playback clocks were not applied.");
    }
}
