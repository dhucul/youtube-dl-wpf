using YoutubeDl.Engine.Discovery;

namespace YoutubeDl.Engine.Download;

internal static class FreeFormatPreference
{
    internal static int Rank(MediaCandidate candidate) => candidate.IsAudioOnly ? candidate.Extension?.ToLowerInvariant() switch
    {
        "ogg" => 6, "opus" => 5, "webm" => 4, "mp3" => 3, "m4a" => 2, "aac" => 1, _ => 0
    } : candidate.Extension?.ToLowerInvariant() switch
    {
        "webm" => 4, "mp4" => 3, "mov" => 2, "flv" => 1, _ => 0
    };
}
