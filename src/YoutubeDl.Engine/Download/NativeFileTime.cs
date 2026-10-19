namespace YoutubeDl.Engine.Download;

/// <summary>Best-effort remote time preservation on owned, complete local files.</summary>
internal static class NativeFileTime
{
    internal static bool TrySet(string path, DateTime? modifiedUtc)
    {
        if (modifiedUtc is null || modifiedUtc.Value.Year < 1601) return false;
        try
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            File.SetLastWriteTimeUtc(path, DateTime.SpecifyKind(modifiedUtc.Value, DateTimeKind.Utc)); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or NotSupportedException) { return false; }
    }
}
