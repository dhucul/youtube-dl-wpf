using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YoutubeDlModern.Models;
using YoutubeDlModern.Services;
using YoutubeDlModern.ViewModels;

internal static partial class Program
{
    internal static IEnumerable<(string Name, Func<Task> Test)> AuditRegressionChecks =>
    [
        ("Repeated pause and resume never accumulates scheduled runs", SchedulerToggling),
        ("Removed items still record files without announcing completion", RemovedCompletion),
        ("Undecryptable history links keep every other entry", HistoryForeignSecret),
        ("Unreadable history is preserved before it is replaced", HistoryUnreadable),
        ("Undecryptable settings secrets keep every other setting", SettingsForeignSecret),
        ("Thumbnails keep their aspect ratio", ThumbnailAspect)
    ];

    // DPAPI accepts any bytes, but these were never protected for this account.
    private const string ForeignSecret = "dpapi:v1:AAAAAAAAAAAAAAAAAAAAAA==";

    private static async Task SchedulerToggling()
    {
        await using var f = new Fixture();
        f.Settings.Data.General.MaxConcurrentDownloads = 1;
        f.Queue.ApplyConcurrency();
        var errors = new List<string>();
        f.Queue.QueueError += errors.Add;
        f.Engine.Download = async (ct, _) => { await Task.Delay(Timeout.Infinite, ct); return new(); };
        var items = Enumerable.Range(0, 40).Select(_ => f.Enqueue()).ToList();
        for (var cycle = 0; cycle < 25; cycle++)
        {
            f.Queue.PauseAll();
            f.Queue.StartAll();
            Assert(f.Queue.PendingRunCount <= items.Count, "Paused runs stayed in the schedule.");
        }
        Assert(errors.Count == 0 && items.All(item => item.Status != DownloadStatus.Failed), string.Join("; ", errors));
    }

    private static async Task RemovedCompletion()
    {
        await using var f = new Fixture();
        var path = f.MediaFile();
        var announced = 0;
        f.Queue.ItemCompleted += (_, _) => announced++;
        var exited = new TaskCompletionSource<DownloadRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Engine.Download = (_, file) => { file!(path); return exited.Task; };
        var item = f.Enqueue();
        await Until(() => item.Status == DownloadStatus.Downloading);
        f.Queue.Remove(item);
        exited.SetResult(new() { FinalFilePaths = [path] });
        await Until(() => item.Status == DownloadStatus.Completed);
        await f.History.FlushAsync();
        Assert(announced == 0, "A removed item announced its completion.");
        Assert(f.History.Entries.Single().FilePath == path, "A removed item's finished file was not recorded.");
    }

    private static async Task HistoryForeignSecret()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var entries = new[]
        {
            new HistoryEntry { Title = "Foreign", Url = ForeignSecret, FilePath = "a.mp4" },
            new HistoryEntry { Title = "Local", Url = "https://example.com/b", FilePath = "b.mp4" }
        };
        await File.WriteAllTextAsync(Path.Combine(dir, "history.json"), JsonSerializer.Serialize(entries));
        using var history = new HistoryService(dir);
        history.Load();
        Assert(history.Entries.Count == 2, "One undecryptable link discarded the whole history.");
        Assert(history.Entries.Single(entry => entry.Title == "Foreign").Url == "" &&
               history.Entries.Single(entry => entry.Title == "Local").Url == "https://example.com/b",
            "History links were not recovered individually.");
        Assert(history.LoadWarning is not null, "Cleared history links were not reported.");
    }

    private static async Task HistoryUnreadable()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "history.json"), "{ not json");
        using (var history = new HistoryService(dir))
        {
            history.Load();
            Assert(history.Entries.Count == 0 && history.LoadWarning is not null, "Unreadable history was not reported.");
            history.Add(new HistoryEntry { Title = "New", Url = "https://example.com/c", FilePath = "c.mp4" });
            await history.FlushAsync();
        }
        var preserved = Directory.GetFiles(dir, "history.json.unreadable-*");
        Assert(preserved.Length == 1 && File.ReadAllText(preserved[0]) == "{ not json",
            "The unreadable history was overwritten without keeping a copy.");
    }

    private static async Task SettingsForeignSecret()
    {
        var dir = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stored = new AppSettings();
        stored.General.MaxConcurrentDownloads = 5;
        stored.Network.Proxy = ForeignSecret;
        await File.WriteAllTextAsync(Path.Combine(dir, "settings.json"), JsonSerializer.Serialize(stored));
        var settings = new SettingsService(dir);
        settings.Load();
        Assert(settings.Data.General.MaxConcurrentDownloads == 5, "One undecryptable secret reset every setting.");
        Assert(settings.Data.Network.Proxy == "" && settings.LoadWarning is not null, "The undecryptable secret was not cleared and reported.");
    }

    private static Task ThumbnailAspect()
    {
        var source = BitmapSource.Create(1280, 720, 96, 96, PixelFormats.Bgr32, null, new byte[1280 * 720 * 4], 1280 * 4);
        using var data = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(data);
        var image = (BitmapSource)ThumbnailService.Decode(data)!;
        Assert(image.PixelWidth == 512 && image.PixelHeight == 288, $"Thumbnail decoded as {image.PixelWidth}x{image.PixelHeight}.");
        return Task.CompletedTask;
    }
}
