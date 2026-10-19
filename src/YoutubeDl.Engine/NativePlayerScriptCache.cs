using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine;

/// <summary>Caches bounded public versioned player source, never playback/session data.</summary>
internal static class NativePlayerScriptCache
{
    private const int MaximumBytes = 3 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private sealed record Entry(int Schema, string Engine, string Url, DateTimeOffset Created, string Hash, string Source);
    internal static void Validate(NativeDownloadRequest request)
    {
        if (request.NoCache && !request.RemoveCache || string.IsNullOrWhiteSpace(request.CacheDirectory)) return;
        if (request.CacheDirectory.Length > 4096 || request.CacheDirectory.Any(char.IsControl)) throw new ArgumentException("The native cache directory is invalid.");
        _ = Path.GetFullPath(request.CacheDirectory);
    }
    internal static async Task<IYouTubePlayerSession> CreateAsync(NativeDownloadRequest request, MediaHttp http, Uri url, IYouTubePlayerRuntime runtime, bool publicSession, CancellationToken ct)
    {
        var enabled = !request.NoCache && !string.IsNullOrWhiteSpace(request.CacheDirectory) && publicSession && url.Query.Length == 0 && YouTubePageJson.IsPlayerScriptUrl(url) &&
            !request.Authentication.IsConfigured && string.IsNullOrWhiteSpace(request.CookieFile) && string.IsNullOrWhiteSpace(request.BrowserCookies) && request.HttpHeaders.Count == 0;
        async Task<(IYouTubePlayerSession Session, MediaDocument Document)> Fetch()
        {
            var document = await http.GetDocumentAsync(url, MaximumBytes, ct).ConfigureAwait(false);
            if (!YouTubePageJson.IsPlayerScriptUrl(document.Url) || enabled && document.Url != url) throw new InvalidDataException("The YouTube player script redirect is invalid.");
            return (await Task.Run(() => runtime.CreateSession(document.Text, ct), ct).ConfigureAwait(false), document);
        }
        if (!enabled) return (await Fetch().ConfigureAwait(false)).Session;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? path = null;
            try
            {
                var root = Path.Combine(Path.GetFullPath(request.CacheDirectory!), "native-public-player-v1");
                Plain(root); Directory.CreateDirectory(root); Plain(root);
                path = Path.Combine(root, Hash(url.AbsoluteUri) + ".json"); Plain(path);
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length <= MaximumBytes * 7L)
                    {
                        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                        if (file.Length > MaximumBytes * 7L) throw new InvalidDataException("The cached player envelope exceeds its limit.");
                        var entry = await JsonSerializer.DeserializeAsync<Entry>(file, cancellationToken: ct).ConfigureAwait(false);
                        if (entry is not null && entry.Schema == 1 && entry.Engine == NativeEngine.Version && entry.Url == url.AbsoluteUri &&
                            entry.Created <= DateTimeOffset.UtcNow.AddMinutes(5) && entry.Created >= DateTimeOffset.UtcNow.AddDays(-30) &&
                            entry.Source is not null && Encoding.UTF8.GetByteCount(entry.Source) <= MaximumBytes && entry.Hash == Hash(entry.Source))
                        {
                            try { return await Task.Run(() => runtime.CreateSession(entry.Source, ct), ct).ConfigureAwait(false); }
                            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { }
                        }
                    }
                }
            }
            catch (JsonException) { /* Replace malformed cache data after a fresh validated fetch. */ }
            catch (Exception ex) when (CacheFailure(ex)) { path = null; }
            // Public, versioned source is fetched in an anonymous owned session.
            MediaHttp anonymous;
            try { anonymous = http.CreateAnonymousSession(); }
            catch (InvalidOperationException) { return (await Fetch().ConfigureAwait(false)).Session; }
            using var anonymousLifetime = anonymous;
            var document = await anonymous.GetDocumentAsync(url, MaximumBytes, ct).ConfigureAwait(false);
            if (document.Url != url) throw new InvalidDataException("The cached player source changed its versioned resource.");
            var session = await Task.Run(() => runtime.CreateSession(document.Text, ct), ct).ConfigureAwait(false);
            string? temporary = null;
            try
            {
                if (path is not null)
                {
                    Plain(path);
                    temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await JsonSerializer.SerializeAsync(file, new Entry(1, NativeEngine.Version, url.AbsoluteUri, DateTimeOffset.UtcNow, Hash(document.Text), document.Text), cancellationToken: ct).ConfigureAwait(false);
                        await file.FlushAsync(ct).ConfigureAwait(false); file.Flush(true);
                    }
                    ct.ThrowIfCancellationRequested(); Plain(path); File.Move(temporary, path, true); temporary = null;
                }
                ct.ThrowIfCancellationRequested(); return session;
            }
            catch (OperationCanceledException) { session.Dispose(); throw; }
            catch (Exception ex) when (CacheFailure(ex)) { return session; }
            finally { if (temporary is not null) try { File.Delete(temporary); } catch (Exception ex) when (CacheFailure(ex)) { } }
        }
        finally { Gate.Release(); }
    }
    internal static async Task<int> RemoveAsync(NativeDownloadRequest request, CancellationToken ct)
    {
        Validate(request); ct.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(request.CacheDirectory)) return 0;
        var root = Path.Combine(Path.GetFullPath(request.CacheDirectory), "native-public-player-v1");
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Plain(root); if (!Directory.Exists(root)) return 0; var removed = 0; var seen = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested(); if (++seen > 10000) throw new IOException("The native cache cleanup exceeded its file limit.");
                var name = Path.GetFileName(path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-F0-9]{64}(?:\.json|\.[a-f0-9]{32}\.tmp)\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) continue;
                var full = Path.GetFullPath(path);
                if (!string.Equals(Path.GetDirectoryName(full), root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("The native cache cleanup left its owned directory.");
                Plain(full); File.Delete(full); removed++;
            }
            return removed;
        }
        finally { Gate.Release(); }
    }
    private static bool CacheFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Plain(string path)
    {
        for (var item = new FileInfo(Path.GetFullPath(path)); item is not null; item = item.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) && File.GetAttributes(item.FullName).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The native public-script cache cannot follow linked paths.");
    }
}
