namespace YoutubeDl.Engine.Download;

/// <summary>Serializes jobs sharing an output stem without retaining locks for completed downloads.</summary>
internal static class DestinationLock
{
    private sealed class Gate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References;
    }
    private static readonly Dictionary<string, Gate> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static Task<IDisposable> AcquireAsync(string path, CancellationToken ct) => AcquireKeyAsync("file:" +
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path))), ct);

    public static Task<IDisposable> AcquirePublicationAsync(string directory, CancellationToken ct) =>
        AcquireKeyAsync("publish:" + Path.GetFullPath(directory), ct);
    public static async Task<IDisposable> AcquirePublicationsAsync(IEnumerable<string> directories, CancellationToken ct)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var leases = new List<IDisposable>();
        try
        {
            foreach (var directory in directories.Select(Path.GetFullPath).Distinct(comparer).Order(comparer))
                leases.Add(await AcquirePublicationAsync(directory, ct).ConfigureAwait(false));
            return new ManyLeases(leases);
        }
        catch { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }
    public static Task<IDisposable> AcquireArchiveAsync(string path, CancellationToken ct) =>
        AcquireKeyAsync("archive:" + Path.GetFullPath(path), ct);

    private static async Task<IDisposable> AcquireKeyAsync(string key, CancellationToken ct)
    {
        Gate gate;
        lock (Gates)
        {
            if (!Gates.TryGetValue(key, out gate!)) Gates[key] = gate = new();
            gate.References++;
        }
        try { await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false); }
        catch { ReleaseReference(key, gate); throw; }
        return new Lease(key, gate);
    }

    private static void ReleaseReference(string key, Gate gate)
    {
        lock (Gates)
        {
            if (--gate.References != 0) return;
            Gates.Remove(key);
            gate.Semaphore.Dispose();
        }
    }

    private sealed class Lease(string key, Gate gate) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            gate.Semaphore.Release();
            ReleaseReference(key, gate);
        }
    }
    private sealed class ManyLeases(List<IDisposable> leases) : IDisposable
    {
        private List<IDisposable>? _leases = leases;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _leases, null) is { } held)
                foreach (var lease in held.AsEnumerable().Reverse()) lease.Dispose();
        }
    }
}
