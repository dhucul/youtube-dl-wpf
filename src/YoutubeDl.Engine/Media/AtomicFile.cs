namespace YoutubeDl.Engine.Media;

internal static class AtomicFile
{
    public static async Task WriteAsync(string destination, byte[] bytes, CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".sidecar-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
