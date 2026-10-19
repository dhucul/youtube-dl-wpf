namespace YoutubeDl.Engine.Download;

internal static class NativeHttpChunkSize
{
    internal static long? Parse(string? value)
    {
        var size = NativeFileSizeLimits.Parse(value);
        return size is null or 0 ? null : size;
    }
}
