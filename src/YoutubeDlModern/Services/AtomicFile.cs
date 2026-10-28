using System.IO;
using System.Text;

namespace YoutubeDlModern.Services;

internal static class AtomicFile
{
    private const int BufferSize = 81920;

    public static string BackupPath(string path) => path + ".bak";

    /// <summary>
    /// Copies an existing store that could not be read aside before a later save replaces it
    /// and its backup, so its contents remain recoverable by hand.
    /// </summary>
    /// <returns>The preserved copy, or null when nothing exists or the copy failed.</returns>
    public static string? PreserveUnreadable(string path)
    {
        var source = File.Exists(path) ? path : File.Exists(BackupPath(path)) ? BackupPath(path) : null;
        if (source is null)
            return null;
        var target = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            File.Copy(source, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return File.Exists(target) ? target : null;
        }
    }

    public static string ReadAllText(string path, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' exceeds the {maximumBytes:N0}-byte safety limit.");

        using var memory = new MemoryStream((int)Math.Min(stream.Length, maximumBytes));
        var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        var total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException(
                    $"'{Path.GetFileName(path)}' exceeds the {maximumBytes:N0}-byte safety limit.");
            memory.Write(buffer, 0, read);
        }
        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(memory.GetBuffer(), 0, total);
    }

    public static void WriteAllText(string path, string contents, bool createBackup)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The file path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (createBackup && File.Exists(path))
                File.Copy(path, BackupPath(path), overwrite: true);

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort cleanup */ }
        }
    }
}
