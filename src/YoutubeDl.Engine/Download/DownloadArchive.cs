using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

public sealed record DownloadArchiveIdentity(string Extractor, string Id)
{
    public string Record
    {
        get
        {
            if (!Regex.IsMatch(Extractor, @"\A[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
                string.IsNullOrWhiteSpace(Id) || Id != Id.Trim() || Encoding.UTF8.GetByteCount(Id) > 16384 || Id.Any(value => char.IsControl(value) || value is '\u2028' or '\u2029'))
                throw new InvalidDataException("The media identifier cannot be represented safely in a download archive.");
            return Extractor.ToLowerInvariant() + " " + Id;
        }
    }

    public static DownloadArchiveIdentity FromMetadata(PageMetadata page, Uri source)
    {
        if (page.ArchiveIdentity is { } identity) return identity;
        if (!string.IsNullOrWhiteSpace(page.Id)) return new(page.Extractor switch { "brightcove" => "brightcovenew", "archive.org" => "archiveorg", _ => page.Extractor }, page.Id);
        // A host/title is not a unique media identity. Keep URL-derived identities in their own
        // namespace, so unrelated files cannot collide with legacy generic extractor records.
        var canonical = new UriBuilder(source) { Fragment = "" }.Uri.AbsoluteUri;
        return new("native:" + page.Extractor, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
    }
}

/// <summary>Serializes archive-backed jobs and publishes complete UTF-8 archive replacements atomically.</summary>
internal sealed class DownloadArchive : IDisposable
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly FileStream? _lock;
    private readonly IDisposable? _gate;
    private readonly string _record;
    public string Path { get; }
    public string LockPath => Path + ".native.lock";
    public IReadOnlyList<string> ProtectedPaths => [Path, LockPath];
    public bool AlreadyRecorded { get; }

    private DownloadArchive(string path, string record, bool recorded, FileStream? fileLock, IDisposable? gate)
    { Path = path; _record = record; AlreadyRecorded = recorded; _lock = fileLock; _gate = gate; }

    public static async Task<bool> ContainsAsync(string path, DownloadArchiveIdentity identity, CancellationToken ct) =>
        Contains(await ReadAsync(ResolvePath(path), ct).ConfigureAwait(false), identity.Record);
    internal static IReadOnlyList<string> ProtectedOutputPaths(string path)
    {
        var full = ResolvePath(path); return [full, full + ".native.lock"];
    }

    public static async Task<DownloadArchive> OpenAsync(string path, DownloadArchiveIdentity identity, CancellationToken ct)
    {
        path = ResolvePath(path);
        var record = identity.Record;
        if (Contains(await ReadAsync(path, ct).ConfigureAwait(false), record)) return new(path, record, true, null, null);
        var gate = await DestinationLock.AcquireArchiveAsync(path, ct).ConfigureAwait(false);
        FileStream? fileLock = null;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            while (fileLock is null)
            {
                ct.ThrowIfCancellationRequested();
                try { fileLock = new FileStream(path + ".native.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException ex) when (SharingViolation(ex)) { await Task.Delay(100, ct).ConfigureAwait(false); }
            }
            var recorded = Contains(await ReadAsync(path, ct).ConfigureAwait(false), record);
            if (!recorded && File.Exists(path))
            {
                // Check permissions before starting media transfer; do not truncate existing bytes.
                using var writable = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
            }
            return new(path, record, recorded, fileLock, gate);
        }
        catch { fileLock?.Dispose(); gate.Dispose(); throw; }
    }

    public async Task RecordAsync(DownloadWorkspace.Completion? completion=null,Func<string,Task>? prepareTemporary=null)
    {
        if (AlreadyRecorded) return;
        // Publication has already committed. Finish this short ledger commit even if the user's
        // cancellation arrives now, so completed files do not acquire an unrecorded success.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Prevent ordinary external appenders from changing the snapshot through its handle
            // during the commit. Delete sharing permits our atomic replacement; a replacement by
            // an editor is detected by the byte comparison immediately before publication.
            using var snapshotLock = File.Exists(Path) ? new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null;
            var before = await ReadAsync(Path, CancellationToken.None).ConfigureAwait(false);
            if (Contains(before, _record)) return;
            var append = Utf8.GetBytes((before.Length > 0 && before[^1] != (byte)'\n' ? "\n" : "") + _record + "\n");
            if (before.Length + append.Length > MaximumBytes) throw new InvalidDataException("The download archive exceeds the native 64 MiB limit.");
            var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".native-archive-" + Guid.NewGuid().ToString("N") + ".tmp");
            if(prepareTemporary is not null)await prepareTemporary(temporary).ConfigureAwait(false);
            else if(completion is not null)await DownloadWorkspace.RegisterArchiveTemporaryAsync(completion,temporary).ConfigureAwait(false);
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    var first=before.Length>0?before:append;var half=first.Length/2;await output.WriteAsync(first.AsMemory(0,half)).ConfigureAwait(false);await output.FlushAsync().ConfigureAwait(false);
                    await PublicationFaults.AtAsync("archive-writing").ConfigureAwait(false);
                    await output.WriteAsync(first.AsMemory(half)).ConfigureAwait(false);if(before.Length>0)await output.WriteAsync(append).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                    await PublicationFaults.AtAsync("archive-flushed").ConfigureAwait(false);
                }
                // Preserve edits/appends made by a non-native writer while the download ran.
                if (!(await ReadAsync(Path, CancellationToken.None).ConfigureAwait(false)).AsSpan().SequenceEqual(before)) continue;
                if (File.Exists(Path)) File.Replace(temporary, Path, null);
                else File.Move(temporary, Path);
                await PublicationFaults.AtAsync("archive-replaced").ConfigureAwait(false);
                return;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        throw new IOException("The download archive kept changing while its new entry was being saved.");
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(path)) throw new IOException("The download archive path is a directory.");
        FileStream input;
        try { input = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        await using (input)
        {
            if (input.Length > MaximumBytes) throw new InvalidDataException("The download archive exceeds the native 64 MiB limit.");
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            while (true)
            {
                var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > MaximumBytes) throw new InvalidDataException("The download archive exceeds the native 64 MiB limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }
    private static bool Contains(byte[] bytes, string record)
    {
        string text;
        try { text = Utf8.GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("The download archive is not valid UTF-8; it was left unchanged.", ex); }
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        while (reader.ReadLine() is { } line) if (line.Trim() == record) return true;
        return false;
    }
    private static bool SharingViolation(IOException ex) => (ex.HResult & 0xffff) is 32 or 33;
    private static string ResolvePath(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var name = System.IO.Path.GetFileName(full);
        if (OperatingSystem.IsWindows() && (name.Contains(':') || full.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            Regex.IsMatch(name.TrimEnd(' ', '.'), @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
            throw new ArgumentException("The download archive must be a regular file, not a Windows device or alternate data stream.", nameof(path));
        return full;
    }
    public void Dispose() { _lock?.Dispose(); _gate?.Dispose(); }
}
