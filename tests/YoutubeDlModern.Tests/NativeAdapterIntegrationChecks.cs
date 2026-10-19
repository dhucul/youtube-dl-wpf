using System.IO;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;

internal static partial class Program
{
    private static PageMetadata AdapterVideo(string id = "first") => new()
    {
        Id = id, Title = id, Extractor = "fixture",
        Media = [new() { Id = id + "-format", Url = new("https://media.example/" + id + ".mp4"), Extension = "mp4", HasVideo = true, HasAudio = true }]
    };

    private static PageMetadata AdapterList(string id, params NativePlaylistEntry[] entries) => new()
    { Id = id, Title = id, Extractor = "fixture", IsPlaylist = true, PlaylistId = id, Entries = entries };

    private static NativePlaylistEntry AdapterEntry(int position, string id, string path, bool available = true) =>
        new(position, id, id, new("https://media.example/" + path), null, null, available);

    private sealed class AdapterInspectionProvider(Func<NativeDownloadRequest, CancellationToken, Task<PageMetadata>> extract) : IMediaProvider
    {
        public MediaProviderInfo Info { get; } = new("adapter-fixture", "Adapter fixture", 1000);
        public bool CanHandle(Uri url) => true;
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct) => extract(request, ct);
    }

    private static NativeEngine AdapterEngine(Func<NativeDownloadRequest, CancellationToken, Task<PageMetadata>> extract) =>
        new([new AdapterInspectionProvider(extract)], _ => new MediaHttp(new NativeCookieMappingHandler()));

    private static async Task NativeAnalysisModes()
    {
        var requests = new List<NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(AdapterEngine((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(request.ExpandPlaylist ? AdapterList("collection", AdapterEntry(1, "first", "first")) : AdapterVideo());
        }));
        foreach (var mode in new[] { "quick", "full", "QUICK" })
        {
            var info = await adapter.GetVideoInfoAsync("https://media.example/watch?list=collection", default, new() { Mode = mode });
            Assert(!info.IsPlaylist && !requests[^1].ExpandPlaylist && !requests[^1].DownloadPlaylist, "Ordinary analysis expanded a linked playlist.");
        }
        var playlist = await adapter.GetVideoInfoAsync("https://media.example/watch?list=collection", default, new() { Mode = "playlist" });
        Assert(playlist.IsPlaylist && playlist.NativePlaylistSnapshot is not null && requests[^1].ExpandPlaylist, "Playlist analysis lost its inventory or inspected selection.");
        var before = requests.Count;
        try { await adapter.GetVideoInfoAsync("https://media.example/watch", default, new() { Mode = "unknown" }); throw new InvalidOperationException("An invalid analysis mode was accepted."); }
        catch (NativeEngineUnsupportedException) { Assert(requests.Count == before, "Invalid analysis contacted the provider."); }
    }

    private static async Task NativeAnalysisConnections()
    {
        var settings = new SettingsService(Path.Combine(TestRoot, "analysis-settings"));
        var saved = new AppSettings();
        saved.Network.Proxy = "http://127.0.0.1:8123"; saved.Network.SourceAddress = "127.0.0.2"; saved.Network.IpVersion = "4";
        saved.Network.SocketTimeout = 12; saved.Network.Retries = 23; saved.Network.NoCheckCertificate = true;
        saved.Network.CustomHeaders = "X-Analysis: saved"; saved.Network.UserAgent = "Analysis/1";
        saved.Auth.CookiesFile = Path.Combine(TestRoot, "saved-cookies.txt"); saved.Auth.CookiesFromBrowser = "firefox";
        saved.General.ExtraArgs = "--exec unused"; saved.Formats.RecodeVideo = "webm"; saved.General.WriteInfoJson = true;
        settings.Save(saved);
        var requests = new List<NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(AdapterEngine((request, _) => { requests.Add(request); return Task.FromResult(AdapterVideo()); }), settings);
        await adapter.GetVideoInfoAsync("https://media.example/watch", default);
        var request = requests[^1];
        Assert(request.Proxy == saved.Network.Proxy && request.SourceAddress == "127.0.0.2" && request.IpVersion == 4 && request.TimeoutSeconds == 12 && request.Retries == 23 && request.NoCheckCertificate,
            "Analysis dropped a saved connection setting.");
        Assert(request.HttpHeaders["X-Analysis"] == "saved" && request.UserAgent == "Analysis/1" && request.CookieFile == saved.Auth.CookiesFile && request.BrowserCookies == "firefox", "Analysis dropped saved headers or authentication.");
        Assert(!request.WriteInfoJson && request.RecodeVideo == "" && !request.AddMetadata && !request.EmbedChapters, "Download-only choices leaked into analysis.");
        await adapter.GetVideoInfoAsync("https://media.example/watch", default, new() { CookiesFile = "", CookiesFromBrowser = "none" });
        Assert(requests[^1].CookieFile == "" && requests[^1].BrowserCookies is null, "Explicitly disabling cookies reused saved credentials.");
        var changed = settings.CreateDraft(); changed.Network.CustomHeaders = "X-Analysis: later";
        changed.General.ExtraArgs = "--exec unused --user-agent Extra/2 --socket-timeout 19"; settings.Save(changed);
        Assert(request.HttpHeaders["X-Analysis"] == "saved", "A later save changed an earlier analysis request.");
        await adapter.GetVideoInfoAsync("https://media.example/watch", default);
        Assert(requests[^1].HttpHeaders["X-Analysis"] == "later", "A later analysis did not use the fresh settings snapshot.");
        Assert(requests[^1].UserAgent == "Extra/2" && requests[^1].TimeoutSeconds == 19 && !requests[^1].Simulate,
            "Supported extraction arguments were discarded or download arguments leaked into analysis.");
        changed.General.ExtraArgs = "--extractor-args unsupported=private-fixture-value"; settings.Save(changed);
        var before = requests.Count;
        try { await adapter.GetVideoInfoAsync("https://media.example/watch", default); throw new InvalidOperationException("Unsupported extraction arguments were ignored."); }
        catch (NativeEngineUnsupportedException ex) { Assert(requests.Count == before && !ex.Message.Contains("private-fixture-value"), "Unsupported extraction arguments reached discovery or exposed values."); }
    }

    private static async Task NativePlaylistFormats()
    {
        var requests = new List<NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(AdapterEngine((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(request.Url.AbsolutePath switch
            {
                "/collection" => AdapterList("collection", AdapterEntry(2, "second", "second"), AdapterEntry(1, "nested", "nested")),
                "/nested" => AdapterList("nested", AdapterEntry(1, "first", "first")),
                _ => AdapterVideo(request.Url.AbsolutePath.Trim('/'))
            });
        }));
        var formats = await adapter.ListFormatsAsync("https://media.example/collection", default, new() { Mode = "playlist", CookiesFile = "selected-cookies", CookiesFromBrowser = "firefox" }, true);
        Assert(formats.Single().FormatId == "first-format" && requests.Select(request => request.Url.AbsolutePath).SequenceEqual(new[] { "/collection", "/nested", "/first" }), "Format choice inspected a collection or the wrong original entry.");
        Assert(requests[0].ExpandPlaylist && requests.Skip(1).All(request => !request.ExpandPlaylist && !request.DownloadPlaylist), "Child format inspection expanded another linked playlist.");
        Assert(requests.All(request => request.CookieFile == "selected-cookies" && request.BrowserCookies == "firefox"), "Child inspection lost the selected authentication.");
        requests.Clear();
        await adapter.ListFormatsAsync("https://media.example/first", default, new() { Mode = "playlist" }, false);
        Assert(requests.Count == 1 && !requests[0].ExpandPlaylist, "Single-video format choice honored an unrelated playlist analysis mode.");
    }

    private static async Task NativeResolvedFormats()
    {
        var leaf = AdapterVideo("chosen") with { ReportedFormats = [new() { Id = "provider-720", Url = new("https://media.example/signed.mp4"), Height = 720 },
            new() { Id = "protected", Availability = FormatAvailability.DrmProtected }] };
        var nested = AdapterList("nested", new NativePlaylistEntry(1, "chosen", "Chosen", null, null, null, true, leaf));
        var calls = 0;
        var adapter = new NativeEngineAdapter(AdapterEngine((_, _) =>
        {
            calls++;
            if (calls > 1) throw new InvalidOperationException("Resolved metadata reopened its parent or fetched the media.");
            return Task.FromResult(AdapterList("root", new NativePlaylistEntry(1, "nested", "Nested", new("https://media.example/root"), null, null, true, nested)));
        }));
        var formats = await adapter.ListFormatsAsync("https://media.example/root", default, isPlaylist: true);
        Assert(calls == 1 && formats.Single().FormatId == "provider-720" && formats[0].Height == 720,
            "Resolved first-entry format IDs or availability were replaced by direct-media discovery.");
        var candidate = NativeEngine.Describe(new("https://media.example/root"), AdapterVideo("candidate"));
        Assert(candidate.Formats.Single().Id == "candidate-format" && candidate.Formats[0].HasAudio == true, "Resolved candidate facts changed during description.");
        var protectedAdapter = new NativeEngineAdapter(AdapterEngine((_, _) => Task.FromResult(AdapterList("root",
            new NativePlaylistEntry(1, "protected", "Protected", null, null, null, true, leaf with { DrmRefused = true, RefusalReason = "Fixture protected media." })))));
        try { await protectedAdapter.ListFormatsAsync("https://media.example/root", default, isPlaylist: true); throw new InvalidOperationException("Resolved protected media offered a ready format."); }
        catch (NativeEngineUnsupportedException) { }
        var entries = new List<NativePlaylistEntry>(); var cycle = AdapterList("cycle") with { Entries = entries };
        entries.Add(new(1, "cycle", "Cycle", null, null, null, true, cycle));
        var cyclicAdapter = new NativeEngineAdapter(AdapterEngine((_, _) => Task.FromResult(cycle)));
        try { await cyclicAdapter.ListFormatsAsync("https://media.example/root", default, isPlaylist: true); throw new InvalidOperationException("A resolved collection cycle was accepted."); }
        catch (NativeEngineUnsupportedException) { }
        try { NativeEngine.Describe(new("relative", UriKind.Relative), leaf); throw new InvalidOperationException("A relative description address was accepted."); }
        catch (ArgumentException) { }
    }

    private static async Task NativePlaylistFormatBoundaries()
    {
        foreach (var page in new[] { AdapterList("empty"), AdapterList("unavailable", AdapterEntry(1, "first", "first", false)), AdapterList("cycle", AdapterEntry(1, "cycle", "cycle")) })
        {
            var calls = 0;
            var adapter = new NativeEngineAdapter(AdapterEngine((_, _) => { calls++; return Task.FromResult(page); }));
            try { await adapter.ListFormatsAsync("https://media.example/cycle", default, isPlaylist: true); throw new InvalidOperationException("Invalid collection formats were accepted."); }
            catch (NativeEngineUnsupportedException) { Assert(calls == 1, "An unavailable or cyclic first entry was inspected again."); }
        }
        var depth = 0;
        var deep = new NativeEngineAdapter(AdapterEngine((_, _) => Task.FromResult(AdapterList("level" + depth, AdapterEntry(1, "level" + ++depth, "level" + depth)))));
        try { await deep.ListFormatsAsync("https://media.example/level0", default, isPlaylist: true); throw new InvalidOperationException("Unbounded nested format inspection was accepted."); }
        catch (NativeEngineUnsupportedException) { Assert(depth == 16, "Nested format inspection exceeded its bound."); }
    }

    private static async Task NativePlaylistFormatCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var childEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var drained = false;
        var adapter = new NativeEngineAdapter(AdapterEngine(async (request, ct) =>
        {
            if (request.Url.AbsolutePath == "/collection") return AdapterList("collection", AdapterEntry(1, "first", "first"));
            childEntered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); return AdapterVideo(); }
            finally { drained = true; }
        }));
        var pending = adapter.ListFormatsAsync("https://media.example/collection", cancellation.Token, isPlaylist: true);
        await childEntered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        try { await pending; throw new InvalidOperationException("Canceled child inspection completed successfully."); }
        catch (OperationCanceledException) { Assert(drained, "Child inspection did not drain cancellation."); }
    }

    private static async Task NativeUnsupportedSettings()
    {
        var secret = "fixture-private-value";
        var cases = new (string Id, Action<AppSettings> Configure)[]
        {
            ("Network.GeoVerificationProxy", s => s.Network.GeoVerificationProxy = secret), ("Network.GeoBypassCountry", s => s.Network.GeoBypassCountry = secret), ("Network.GeoBypassIpBlock", s => s.Network.GeoBypassIpBlock = secret),
            
            ("Network.ExternalDownloader", s => s.Network.ExternalDownloader = secret), ("Network.ExternalDownloaderArgs", s => s.Network.ExternalDownloaderArgs = secret),
            ("Auth.Username", s => s.Auth.Username = secret), ("Auth.Password", s => s.Auth.Password = secret), ("Auth.TwoFactor", s => s.Auth.TwoFactor = secret), ("Auth.UseNetrc", s => s.Auth.UseNetrc = true),
            ("Auth.VideoPassword", s => s.Auth.VideoPassword = secret), ("Auth.ApMso", s => s.Auth.ApMso = secret), ("Auth.ApUsername", s => s.Auth.ApUsername = secret), ("Auth.ApPassword", s => s.Auth.ApPassword = secret),
            ("PostProcess.Fixup", s => s.PostProcess.Fixup = secret), ("PostProcess.PostprocessorArgs", s => s.PostProcess.PostprocessorArgs = secret),
            ("PostProcess.ExecCommand", s => s.PostProcess.ExecCommand = "before_dl:" + secret), ("PostProcess.SponsorBlock", s => s.PostProcess.SponsorBlock = secret)
        };
        var output = Path.Combine(TestRoot, "unsupported-options"); var archive = Path.Combine(TestRoot, "unsupported-archive.txt");
        await File.WriteAllTextAsync(archive, "fixture preserved\n");
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Unsupported settings reached HTTP discovery.")));
        foreach (var item in cases)
        {
            var settings = new AppSettings(); settings.General.DownloadArchive = archive; item.Configure(settings);
            try
            {
                await adapter.DownloadAsync("https://media.example/watch", new() { OutputDirectory = output }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default,
                    _ => throw new InvalidOperationException("Unsupported settings emitted a file completion."));
                throw new InvalidOperationException("Unsupported saved option was silently ignored: " + item.Id);
            }
            catch (NativeEngineUnsupportedException ex)
            {
                Assert(ex.Message.Contains(item.Id) && !ex.Message.Contains(secret), "Unsupported-setting diagnostics lost the option identity or exposed its value.");
                Assert(!Directory.Exists(output) && await File.ReadAllTextAsync(archive) == "fixture preserved\n", "Unsupported settings changed output or archive data.");
            }
        }
        var combined = new AppSettings(); combined.Auth.Password = secret; combined.PostProcess.Fixup = secret;
        try { await adapter.DownloadAsync("https://media.example/watch", new(), combined, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("Combined unsupported settings were ignored."); }
        catch (NativeEngineUnsupportedException ex) { Assert(ex.Message.Contains("Auth.Password") && ex.Message.Contains("PostProcess.Fixup") && !ex.Message.Contains(secret), "The preflight did not identify every configured unsupported setting."); }
        var service = new SettingsService(Path.Combine(TestRoot, "analysis-unsupported")); var auth = new AppSettings(); auth.Auth.Username = secret; service.Save(auth);
        var analysis = new NativeEngineAdapter(new NativeEngine(httpFactory: _ => throw new InvalidOperationException("Unsupported analysis authentication reached HTTP.")), service);
        try { await analysis.GetVideoInfoAsync("https://media.example/watch", default); throw new InvalidOperationException("Analysis silently dropped username authentication."); }
        catch (NativeEngineUnsupportedException ex) { Assert(ex.Message.Contains("Auth.Username") && !ex.Message.Contains(secret), "Analysis exposed or discarded unsupported authentication."); }
    }

    private static async Task NativeRequestValues()
    {
        var requests = new List<NativeDownloadRequest>();
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: request => { requests.Add(request); return new MediaHttp(new NativeCookieMappingHandler()); }));
        var settings = new AppSettings(); settings.Subtitles.SubLangs = "fr,de";
        foreach (var retries in new[] { 0, 23, 1000 })
        {
            await adapter.DownloadAsync("https://media.example/value.mp4", new() { OutputDirectory = TestRoot, Retries = retries, ExtraArgs = "--simulate", AddMetadata = false, EmbedChapters = false },
                settings, null, new Progress<ProgressUpdate>(), _ => { }, default);
            Assert(requests[^1].Retries == retries && requests[^1].SubLanguages == "fr,de", "Retries were clamped or saved subtitle languages were discarded.");
        }
        var before = requests.Count;
        try { await adapter.DownloadAsync("https://media.example/value.mp4", new() { Retries = 1001 }, settings, null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("Invalid retries were silently clamped."); }
        catch (ArgumentOutOfRangeException) { Assert(requests.Count == before, "Invalid retries reached HTTP discovery."); }
    }

    private static async Task NativeInvalidChoices()
    {
        var adapter = new NativeEngineAdapter(new NativeEngine(httpFactory: _ => throw new InvalidOperationException("An invalid job reached HTTP discovery.")));
        foreach (var options in new[] { new DownloadOptions { DownloadMode = "unknown" }, new DownloadOptions { QualityPreset = "unknown" }, new DownloadOptions { QualityPreset = "0" }, new DownloadOptions { QualityPreset = "custom", CustomFormat = "" } })
        {
            try { await adapter.DownloadAsync("https://media.example/watch", options, new(), null, new Progress<ProgressUpdate>(), _ => { }, default); throw new InvalidOperationException("An invalid job became a default download."); }
            catch (NativeEngineUnsupportedException) { }
        }
    }
}
