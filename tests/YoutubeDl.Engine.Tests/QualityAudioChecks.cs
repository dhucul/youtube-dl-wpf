using System.Text.Json.Nodes;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Protocols.Sabr;
using YoutubeDl.Engine.Providers.YouTube;

internal static class QualityAudioChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { "identity", "renewal", "mode", "duplicate-mode", "capabilities", "unknown-capabilities", "inventory" }
        .Select(mode => ("Quality audio " + mode, (Func<Task>)(() => Contract(mode))));
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Contract(string mode)
    {
        if (mode == "inventory")
        {
            var count = 0; var engine = new NativeEngine(httpFactory: _ => new YoutubeDl.Engine.Http.MediaHttp(new SabrFixture.Handler(request =>
            { Assert(request.Method == HttpMethod.Get, "Metadata preview changed player clients or requested playback."); count++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(SabrFixture.Watch) }); }), new() { AllowAutoRedirect = false }));
            var info = await engine.InspectAsync(new() { Url = new("https://youtu.be/" + SabrFixture.VideoId), OutputDirectory = Path.GetTempPath(), SkipDownload = true, MetadataInventoryOnly = true }, default);
            Assert(count == 1 && info.Formats.Count > 0 && info.Formats.All(f => f.Availability == FormatAvailability.AdaptiveTransportRequired), "Web metadata inventory required a playback URL or changed client identity."); return;
        }
        var player = SabrFixture.Player(); var formats = player["streamingData"]!["adaptiveFormats"]!.AsArray(); var standard = formats[1]!; standard["audioChannels"] = 2;
        var boosted = standard.DeepClone(); boosted["xtags"] = "CgcKAnZiEgEx"; boosted["lastModified"] = "201"; formats.Add(boosted);
        var surround = standard.DeepClone(); surround["itag"] = 258; surround["audioChannels"] = 6; surround["lastModified"] = "202"; formats.Add(surround);
        var other = standard.DeepClone(); other["itag"] = 328; other["audioChannels"] = 6; other["mimeType"] = "audio/mp4; codecs=\"ec-3\""; other["lastModified"] = "203"; formats.Add(other);
        var page = YouTubePlayerParser.ParseResponse(player.ToJsonString(), SabrFixture.VideoId, sabrSession: new(SabrFixture.VideoId, SabrFixture.Endpoint, 1, "fixture", [1]));
        if (mode is "identity" or "renewal")
        {
            Assert(page.Media.Select(m => m.Id).Distinct().Count() == page.Media.Count && page.Media.Single(m => m.Id == "140:en.0-vb").Sabr!.AudioTrack!.VoiceBoost, "Voice-boost tracks collided with original audio or lost their selected mode.");
            if (mode == "renewal") { var source = page.Media.Single(m => m.Id == "140:en.0-vb"); var refreshed = source.WithSabrSession(source.Sabr!.Session with { ProofToken = [4] }); Assert(refreshed.Sabr!.AudioTrack == source.Sabr.AudioTrack, "Session binding dropped the chosen audio mode."); } return;
        }
        var state = new ProtobufWriter(); state.UInt64(46, 1); state.String(69, "en.0"); state.UInt64(76, 0); state.UInt64(71, 17);
        if (mode == "duplicate-mode") { state.UInt64(46, 0); await ProviderFamiliesChecks.Expect<InvalidDataException>(() => Task.FromResult((QualityBootstrapChecks.Request() with { ClientAbrState = state.ToArray(), AudioTrack = new(false) }).Encode())); return; }
        if (mode == "mode")
        {
            var root = ProtobufReader.Read((QualityBootstrapChecks.Request() with { ClientAbrState = state.ToArray(), AudioTrack = new(false, "fr.0", true, 2) }).Encode()); var actual = ProtobufReader.Read(root.Single(f => f.Number == 1).Bytes);
            Assert(actual.Single(f => f.Number == 46).Integer == 0 && actual.Single(f => f.Number == 76).Integer == 1 && System.Text.Encoding.UTF8.GetString(actual.Single(f => f.Number == 69).Bytes.Span) == "fr.0" && actual.Single(f => f.Number == 71).Integer == 17, "The request reused the browser's previous DRC, language or voice-boost choice."); return;
        }
        if (mode == "unknown-capabilities") { Assert(YouTubePlaybackCapabilities.ApplyAudio(page, ReadOnlyMemory<byte>.Empty).Media.Count == page.Media.Count, "Absent capability evidence invented a codec restriction."); return; }
        var capabilities = new ProtobufWriter(); capabilities.Message(38, media => media.Message(2, audio => { audio.UInt64(1, 1); audio.UInt64(2, 2); })); var selected = YouTubePlaybackCapabilities.ApplyAudio(page, capabilities.ToArray());
        Assert(selected.Media.All(m => m.Sabr!.Format.Itag is not (258 or 328)) && selected.Media.Any(m => m.Id == "140:en.0") && selected.ReportedFormats.Where(f => f.Id.StartsWith("258:", StringComparison.Ordinal) || f.Id.StartsWith("328:", StringComparison.Ordinal)).All(f => f.Availability != FormatAvailability.Ready && f.Url is null && f.UnavailableReason!.Contains("audio", StringComparison.Ordinal)), "Automatic quality retained an unsupported surround codec/layout or lost ordinary stereo.");
    }
}
