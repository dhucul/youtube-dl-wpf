using System.IO;

namespace YoutubeDlModern.Services;

internal static class FileLaunchSafety
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".aac", ".aiff", ".alac", ".avi", ".flac", ".flv", ".gif", ".jpeg", ".jpg",
        ".m4a", ".m4v", ".mka", ".mkv", ".mov", ".mp3", ".mp4", ".mpeg", ".mpg", ".oga",
        ".ogg", ".ogv", ".opus", ".png", ".ts", ".wav", ".webm", ".webp", ".wma", ".wmv"
    };

    public static bool IsSafeMediaFile(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        MediaExtensions.Contains(Path.GetExtension(path));
}
