using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.ReverbNation;

internal sealed record ReverbNationAddress(string? Artist, string? SongId = null, string? WidgetArtistId = null, string? WidgetSongId = null)
{
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (ArgumentException) { return false; } }
    internal static ReverbNationAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host is not ("reverbnation.com" or "www.reverbnation.com" or "legacy.reverbnation.com") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use a public ReverbNation song, artist or selected-song widget.");
        var path = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray(); var widget = path.Length == 3 && path[0] == "widget_code" && path[1] == "html_widget" && path[2].StartsWith("artist_", StringComparison.Ordinal);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2); var key = Uri.UnescapeDataString(parts[0]);
            if (!query.TryAdd(key, parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "") || !(key.StartsWith("utm_", StringComparison.Ordinal) || widget && key is "widget_id" or "client_id" or "context_type" or "id_unique" or "pwc[autoplay]" or "pwc[included_songs]" or "pwc[song_ids]" or "pwc[color]" or "pwc[size]" or "pwc[layout]"))
                throw new ArgumentException("This ReverbNation access or collection selector needs additional native support.");
        }
        if (widget)
        {
            var owner = path[2][7..]; if (!Numeric(owner) || !query.TryGetValue("pwc[song_ids]", out var song) || !Numeric(song)) throw new ArgumentException("Select one public song in the ReverbNation player.");
            return new(null, WidgetArtistId: owner, WidgetSongId: song);
        }
        if (path.Length is not (1 or 3) || !Slug(path[0]) || path[0] is "api" or "control_room" or "login" or "signup" or "widget_code") throw new ArgumentException("This ReverbNation route needs additional native support.");
        if (path.Length == 1) return new(path[0]);
        var match = Regex.Match(path[2], @"\A(?<id>[1-9][0-9]{0,14})(?:-[a-zA-Z0-9_-]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (path[1] != "song" || !match.Success) throw new ArgumentException("Use a public numeric ReverbNation song."); return new(path[0], match.Groups["id"].Value);
    }
    internal static bool Numeric(string? value) => value is not null && Regex.IsMatch(value, @"\A[1-9][0-9]{0,14}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool Slug(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,199}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Web => new("https://legacy.reverbnation.com/" + (WidgetArtistId is null ? Artist + (SongId is null ? "" : "/song/" + SongId) : "widget_code/html_widget/artist_" + WidgetArtistId + "?widget_id=55&pwc[included_songs]=0&pwc[song_ids]=" + WidgetSongId));
}
