using System.Net;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Providers.Whyp;

internal static class WhypChecks
{
    internal const string PublicId = "AbCdEf123456";
    internal static NativeDownloadRequest Request => new() { Url = new("https://whyp.it/tracks/fixture-" + PublicId), OutputDirectory = Path.GetTempPath() };
    internal sealed class Fixture
    {
        internal byte[] Data = [1, 2, 3]; internal string Extension = "mp3"; internal int Metadata, Media, Rejects; internal bool Renew, ChangeVersion; internal Action<Dictionary<string, object?>>? Change;
        internal NativeEngine Engine => new([new WhypProvider()], _ => AdaptiveAccountChecks.Http((request, _) =>
        {
            if (request.RequestUri!.Host == "api.whyp.it")
            {
                Metadata++; var row = Row(); Change?.Invoke(row);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { track = row })) });
            }
            if (request.RequestUri.Host != "cdn.whyp.it") throw new Exception("A foreign Whyp resource was requested.");
            Media++;
            if (Renew && request.RequestUri.Query.Contains("token=one", StringComparison.Ordinal)) { Rejects++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Data) });
        }));
        internal Dictionary<string, object?> Row() => new() { ["id"] = 12345, ["public_id"] = PublicId, ["permalink"] = "/tracks/fixture-" + PublicId, ["api_path"] = "/tracks/" + PublicId, ["title"] = "Fixture", ["duration"] = 3, ["audio_changed_at"] = ChangeVersion && Metadata > 1 ? "2026-10-15T12:00:00Z" : "2026-10-10T12:00:00Z", ["created_at"] = "2026-10-01T12:00:00Z", ["public"] = true, ["allow_downloads"] = true, [Extension == "mp3" ? "lossy_url" : "lossless_url"] = "https://cdn.whyp.it/fixture." + Extension + "?token=" + (Metadata == 1 ? "one" : "two") + "&expires=2000000000", [Extension == "mp3" ? "lossy_size" : "lossless_size"] = Data.Length };
    }
    public static IEnumerable<(string Name, Func<Task> Test)> All => new[] { ("Whyp public current track binding", (Func<Task>)Positive), ("Whyp numeric legacy identity binding", (Func<Task>)Legacy) }
        .Concat(new[] { "identity", "public-id", "canonical", "api-path", "duration", "version", "host", "scheme", "size", "encoding" }.Select(mode => ("Whyp metadata refusal " + mode, (Func<Task>)(() => Refuse(mode)))))
        .Concat(new[] { "private", "link", "one-person", "disabled" }.Select(mode => ("Whyp public access boundary " + mode, (Func<Task>)(() => Access(mode)))))
        .Concat(new[] { "https://whyp.it/tracks/123?token=private", "https://whyp.it/collections/123", "https://whyp.it:444/tracks/123", "https://user@whyp.it/tracks/123", "https://whyp.it/tracks/a/b/c", "https://whyp.it/tracks/123#fragment" }.Select((url, index) => ("Whyp route refusal " + index, (Func<Task>)(() => { try { WhypProvider.Parse(new(url)); throw new Exception("Unsupported Whyp route accepted."); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { } return Task.CompletedTask; }))));
    private static async Task Positive() { var fixture = new Fixture(); var info = await fixture.Engine.InspectAsync(Request, default); ProcessingOptionsChecks.Assert(info.Id == "12345" && info.Formats.Count == 1 && info.DurationSeconds == 3 && fixture.Media == 0 && NativeEngine.CanHandle(Request.Url), "Public Whyp metadata or route binding failed."); }
    private static async Task Legacy() { var fixture = new Fixture(); var info = await fixture.Engine.InspectAsync(Request with { Url = new("https://whyp.it/tracks/12345/old-slug") }, default); ProcessingOptionsChecks.Assert(info.Id == "12345" && fixture.Media == 0, "Legacy Whyp identity failed."); }
    private static async Task Refuse(string mode)
    {
        var fixture = new Fixture { Change = row => { row[mode switch { "identity" => "id", "public-id" => "public_id", "canonical" => "permalink", "api-path" => "api_path", "duration" => "duration", "version" => "audio_changed_at", "size" => "lossy_size", _ => "lossy_url" }] = mode switch { "identity" => 98765, "duration" => 0, "size" => -1, "host" => "https://foreign.example/fixture.mp3", "scheme" => "http://cdn.whyp.it/fixture.mp3", "encoding" => "https://cdn.whyp.it/fixture.wav", _ => "changed" }; } };
        try { await fixture.Engine.InspectAsync(mode == "identity" ? Request with { Url = new("https://whyp.it/tracks/12345") } : Request, default); throw new Exception("Invalid Whyp metadata was accepted."); } catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException) { ProcessingOptionsChecks.Assert(fixture.Media == 0, "Refused Whyp metadata fetched media."); }
    }
    private static async Task Access(string mode) { var fixture = new Fixture { Change = row => row[mode switch { "private" => "public", "link" => "via_link", "one-person" => "one_person_link", _ => "allow_downloads" }] = mode is "link" or "one-person" }; var info = await fixture.Engine.InspectAsync(Request, default); ProcessingOptionsChecks.Assert(info.Formats.Count == 0 && info.Warnings.Count > 0 && fixture.Media == 0, "Whyp access refusal became downloadable media."); }
}
