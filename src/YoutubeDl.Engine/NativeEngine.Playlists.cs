using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private async Task<NativeDownloadResult> DownloadPlaylistAsync(PageMetadata playlist, NativeDownloadRequest request, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, Action<string>? onFileCompleted)
    {
        var traversal = request.PlaylistTraversal ?? new PlaylistTraversalState();
        request = request with { PlaylistTraversal = traversal, PlaylistAncestors = traversal.Enter(playlist, request) };
        var entries = PlaylistSelection.Select(playlist.Entries, request, Random.Shared, ct);
        if (!string.IsNullOrWhiteSpace(request.PlaylistItems) && (request.PlaylistStart != 0 || request.PlaylistEnd != 0 || request.PlaylistEndExplicit))
            log?.Invoke("[playlist] Explicit item selection takes precedence over playlist start/end settings.");
        if (entries.Count == 0) log?.Invoke("[playlist] No entries match the requested selection.");
        var files = new List<string>();
        var newFiles = new List<string>();
        var sidecars = new List<string>(); var newSidecars = new List<string>();
        var failures = new List<NativeDownloadFailure>();
        var limited = false;
        var savedItems = request.PlaylistSnapshot?.Items.ToDictionary(item => item.Entry.Position);
        for (var selectedIndex = 0; selectedIndex < entries.Count; selectedIndex++)
        {
            var entry = entries[selectedIndex];
            var savedItem = savedItems?.GetValueOrDefault(entry.Position);
            ct.ThrowIfCancellationRequested();
            if (request.SelectionState?.Exhausted == true)
            {
                limited = true; log?.Invoke("[playlist] The requested download limit has been reached."); break;
            }
            log?.Invoke($"[playlist] Item {entry.Position}: {entry.Title}");
            try
            {
                if ((!entry.Available || entry.Url is null) && !(request.IgnoreNoFormatsError && entry.ResolvedMedia is { } metadata &&
                    (request.SkipDownload || request.Simulate || metadata.Media.Count == 0)))
                    throw new InvalidOperationException("This playlist item is unavailable or requires access that has not been supplied.");
                var item = request with
                {
                    Url = entry.Url ?? entry.ResolvedMedia?.SourcePageUrl ?? request.Url, DownloadPlaylist = false, ExpandPlaylist = false, PlaylistSnapshot = null, PlaylistItems = "", PlaylistStart = 0, PlaylistEnd = 0, PlaylistEndExplicit = false,
                    Referer = !string.IsNullOrWhiteSpace(request.Referer) || savedItem is null || savedItem.RefreshParent ? request.Referer : savedItem.Referer,
                    PlaylistTitle = playlist.Title, PlaylistId = playlist.PlaylistId ?? playlist.Id, PlaylistIndex = entry.Position, PlaylistAutoNumber = selectedIndex + 1, PlaylistCount = playlist.Entries.Count,
                    FileName = entry.Position.ToString("D3") + " - " + (string.IsNullOrWhiteSpace(request.FileName) ? entry.Title : request.FileName)
                };
                var result = entry.ResolvedMedia is { } snapshot
                    ? await DownloadSnapshotAsync(item, snapshot, http, progress, log, ct, entry.ExpectedArchiveIdentity, onFileCompleted).ConfigureAwait(false)
                    : await DownloadPlaylistItemAsync(item, http, progress, log, ct, entry.ExpectedArchiveIdentity,
                        savedItem is { RefreshParent: false } ? savedItem.ExpectedMediaId : null, onFileCompleted).ConfigureAwait(false);
                files.AddRange(result.OutputPaths);
                newFiles.AddRange(NewlySaved(result));
                sidecars.AddRange(result.SidecarPaths); newSidecars.AddRange(result.NewSidecarPaths);
                if (!result.CompletionsForwarded)await CompleteCallbacksAsync(result,onFileCompleted).ConfigureAwait(false);
                limited |= result.DownloadLimitReached;
                if (!result.Succeeded && result.CompletionsForwarded && result.Failures.Count > 0)
                {
                    failures.AddRange(result.Failures); if (!request.IgnoreErrors) break;
                }
                else if (!result.Succeeded) throw new InvalidOperationException(result.Error ?? "The playlist item could not be downloaded.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (NativeSelectionException) { throw; }
            catch (NativeMetadataParseException) { throw; }
            catch (NativePlaylistTraversalException) { throw; }
            catch (Exception ex)
            {
                failures.Add(new(entry.Position, entry.Title, ex.Message, request.PlaylistParents));
                log?.Invoke($"[playlist] Item {entry.Position} failed: {ex.Message}");
                if (!request.IgnoreErrors) break;
            }
        }
        return new NativeDownloadResult
        {
            CompletionsForwarded = true,
            Succeeded = failures.Count == 0, Skipped = failures.Count == 0 && newFiles.Count == 0 && newSidecars.Count == 0,
            OutputPath = files.Count == 1 ? files[0] : null, OutputPaths = files, NewOutputPaths = newFiles, Failures = failures,
            SidecarPaths = sidecars, NewSidecarPaths = newSidecars, Simulated = request.Simulate,
            DownloadLimitReached = limited,
            Error = failures.Count > 0 ? $"{failures.Count} playlist item(s) failed. Completed files were retained." : null
        };
    }

    private async Task<NativeDownloadResult> DownloadPlaylistItemAsync(NativeDownloadRequest request, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, DownloadArchiveIdentity? expected, string? expectedMediaId = null, Action<string>? onFileCompleted = null)
    {
        ValidateRequest(request);
        var known = _providers.Resolve(request.Url).GetArchiveIdentity(request);
        if (expected is not null && known is not null && expected.Record != known.Record) throw new InvalidDataException("The playlist entry and its URL declare different media identities.");
        known ??= expected;
        if (!string.IsNullOrWhiteSpace(request.DownloadArchiveFile) && known is not null &&
            await DownloadArchive.ContainsAsync(request.DownloadArchiveFile, known, ct).ConfigureAwait(false))
        {
            log?.Invoke("[native] already recorded in the download archive");
            return new() { Succeeded = true, Skipped = true };
        }
        // The parent owns this session until every selected entry has finished.
        // Provider playback headers stay scoped; separate top-level jobs still
        // create independent sessions and cookie jars.
        var page = await LoadAsync(request, http, ct).ConfigureAwait(false);
        if (expectedMediaId is not null && page.Id != expectedMediaId) throw new InvalidDataException("The selected video changed identity since playlist inspection.");
        VerifyPlaylistIdentity(request, page, expected);
        if (page.IsPlaylist) return await DownloadNestedPlaylistAsync(request, page, http, progress, log, ct, onFileCompleted).ConfigureAwait(false);
        return await DownloadResolvedAsync(request, page, http, progress, log, ct).ConfigureAwait(false);
    }

    private async Task<NativeDownloadResult> DownloadSnapshotAsync(NativeDownloadRequest request, PageMetadata snapshot, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, DownloadArchiveIdentity? expected, Action<string>? onFileCompleted)
    {
        ValidateRequest(request);
        VerifyPlaylistIdentity(request, snapshot, expected);
        if (snapshot.IsPlaylist) return await DownloadNestedPlaylistAsync(request, snapshot, http, progress, log, ct, onFileCompleted).ConfigureAwait(false);
        return await DownloadResolvedAsync(request, snapshot, http, progress, log, ct).ConfigureAwait(false);
    }
    private Task<NativeDownloadResult> DownloadNestedPlaylistAsync(NativeDownloadRequest request, PageMetadata page, MediaHttp http,
        IProgress<DownloadProgress>? progress, Action<string>? log, CancellationToken ct, Action<string>? onFileCompleted)
    {
        var parents = request.PlaylistParents.Append(new NativePlaylistContext(request.PlaylistId, request.PlaylistTitle ?? "", request.PlaylistIndex ?? 0,
            request.PlaylistAutoNumber ?? 0, request.PlaylistCount ?? 0)).ToArray();
        return DownloadPlaylistAsync(page, request with { DownloadPlaylist = true, ExpandPlaylist = true, PlaylistParents = Array.AsReadOnly(parents) }, http, progress, log, ct, onFileCompleted);
    }
    private static void VerifyPlaylistIdentity(NativeDownloadRequest request, PageMetadata page, DownloadArchiveIdentity? expected)
    {
        if (expected is not null && DownloadArchiveIdentity.FromMetadata(page, request.Url).Record != expected.Record)
            throw new InvalidDataException("The resolved playlist video does not match the advertised media identity.");
    }
}
