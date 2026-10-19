using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

internal sealed record RetainedMedia(string Path, string Role, string Extension, MediaCandidate? Format, DateTime ModifiedUtc);

internal sealed class RetainedMediaCollector(string directory) : IDisposable
{
    private readonly List<RetainedMedia> _items = [];
    public IReadOnlyList<RetainedMedia> Items => _items;
    public async Task CaptureAsync(string source, string role, string extension, MediaCandidate? format, CancellationToken ct)
    {
        if (role is not ("video" or "audio" or "part") || !Regex.IsMatch(extension, @"\A[A-Za-z0-9]{1,10}\z")) throw new InvalidDataException("A retained media component has an invalid role or extension.");
        var path = System.IO.Path.Combine(directory, ".retained-" + Guid.NewGuid().ToString("N") + "." + extension);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        lock(_items)_items.Add(new(path, role, extension, format, File.GetLastWriteTimeUtc(source)));
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }
    public void Dispose()
    {
        foreach (var item in _items) try { File.Delete(item.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal sealed record StagedMediaOutput(string Source, string Target, IReadOnlyList<string> Sidecars, bool NoOverwrite = false, DateTime? ModifiedUtc = null);
internal sealed record PublishedMediaOutputs(IReadOnlyList<string> AllPaths, IReadOnlyList<string> NewPaths)
{
    internal IReadOnlyList<string> CommittedSidecars { get; init; } = [];
}
