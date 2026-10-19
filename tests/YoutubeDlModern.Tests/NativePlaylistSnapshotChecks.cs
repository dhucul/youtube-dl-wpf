using System.IO;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    private sealed class SnapshotProvider : IMediaProvider
    {
        internal string[] Order = ["a", "b"];
        internal int Parents;
        internal List<string> Children = [];
        public MediaProviderInfo Info { get; } = new("queue-snapshot", "Queue selection fixture", 1000);
        public bool CanHandle(Uri url) => url.Host == "queue.example";
        public Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
        {
            if (request.Url.AbsolutePath == "/list")
            {
                Parents++; return Task.FromResult(new PageMetadata { Id = "list", PlaylistId = "list", IsPlaylist = true, Extractor = Info.Id, Title = "Queue list",
                    Entries = Order.Select((id, index) => new NativePlaylistEntry(index + 1, id, "Item " + id, new("https://queue.example/item/" + id), 2, null, true)).ToArray() });
            }
            var child = request.Url.Segments.Last(); Children.Add(child);
            return Task.FromResult(new PageMetadata { Id = child, Extractor = Info.Id, Title = "Item " + child,
                Media = [new() { Url = new("https://media.example/" + child + ".mp4"), Kind = MediaKind.Progressive, Extension = "mp4", HasVideo = true, HasAudio = true }] });
        }
    }
    private static async Task NativeSnapshotQueue()
    {
        var directory = Path.Combine(TestRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var provider = new SnapshotProvider(); var adapter = new NativeEngineAdapter(new NativeEngine([provider])); var settings = new SettingsService(directory);
        using var history = new HistoryService(directory); await using var queue = new DownloadQueueService(adapter, new FakeFfmpeg(), settings, history);
        var preview = await adapter.GetVideoInfoAsync("https://queue.example/list", default, new AnalysisOptions { Mode = "playlist" });
        provider.Order = ["b", "a", "new"];
        var options = new DownloadOptions { OutputDirectory = directory, PlaylistItems = "2", ExtraArgs = "--skip-download --write-info-json" };
        var item = queue.Enqueue("https://queue.example/list", options, preview);
        await Until(() => item.Status is DownloadStatus.Completed or DownloadStatus.Failed);
        Assert(item.Status == DownloadStatus.Completed && provider.Parents == 1 && provider.Children.SequenceEqual(new[] { "b" }), "Queued native download rediscovered its reordered playlist or selected another item.");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Directory.EnumerateFiles(directory, "*.info.json", SearchOption.AllDirectories).Single()));
        Assert(json.RootElement.GetProperty("id").GetString() == "b" && json.RootElement.GetProperty("playlist_index").GetInt32() == 2 &&
            json.RootElement.GetProperty("n_entries").GetInt32() == 2, "Queued preview/source metadata diverged.");
        Assert(options.NativePlaylistSnapshot is null && item.Options.NativePlaylistSnapshot is not null, "Enqueue mutated caller options or discarded its native selection.");
    }
    private static async Task NativeSnapshotIsolation()
    {
        await using var fixture = new Fixture(); var provider = new SnapshotProvider(); var adapter = new NativeEngineAdapter(new NativeEngine([provider]));
        var first = await adapter.GetVideoInfoAsync("https://queue.example/list", default, new AnalysisOptions { Mode = "playlist" });
        var options = new DownloadOptions { PlaylistItems = "2" };
        var a = new DownloadItemViewModel(fixture.Queue, "https://queue.example/list", options, new AppSettings(), first, null);
        provider.Order = ["c", "b", "a"]; var second = await adapter.GetVideoInfoAsync("https://queue.example/list", default, new AnalysisOptions { Mode = "playlist" });
        var b = new DownloadItemViewModel(fixture.Queue, "https://queue.example/list", options, new AppSettings(), second, null); options.PlaylistItems = "1";
        Assert(a.Options.PlaylistItems == "2" && b.Options.PlaylistItems == "2" && a.Options.NativePlaylistSnapshot?.Count == 2 && b.Options.NativePlaylistSnapshot?.Count == 3 &&
            !ReferenceEquals(a.Options, b.Options), "Another queue item or later options/preview replaced an existing inspected selection.");
        Assert(!JsonSerializer.Serialize(a.Options).Contains("Snapshot", StringComparison.Ordinal) && !JsonSerializer.Serialize(first).Contains("Snapshot", StringComparison.Ordinal),
            "Runtime playlist selections leaked into ordinary persisted options/preview JSON.");
    }
}
