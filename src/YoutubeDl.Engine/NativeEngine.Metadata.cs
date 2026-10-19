using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine;

public sealed partial class NativeEngine
{
    private static async Task<NativeDownloadResult> DownloadWithoutMediaAsync(NativeDownloadRequest request, PageMetadata page, MediaHttp http,
        Action<string>? log, IReadOnlyList<string> protectedPaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MediaDownloadPlan? plan = null;
        if (page.Media.Count == 0)
        {
            if (!request.IgnoreNoFormatsError)
                return new() { Succeeded = false, Error = page.RefusalReason ?? page.Warnings.FirstOrDefault() ?? "No downloadable formats were found. Metadata-only access requires IgnoreNoFormatsError." };
        }
        else plan = MediaDownloadPlan.Create(page, request);
        if (request.Simulate)
        {
            log?.Invoke("[native] simulated " + page.Title);
            return new() { Succeeded = true, Skipped = true, Simulated = true };
        }
        if (!request.WriteDescription && !request.WriteInfoJson && !request.WriteThumbnail && !request.WriteAllThumbnails && !request.WriteSubs && !request.WriteAutoSubs)
        {
            log?.Invoke("[native] media download skipped; no standalone files were requested");
            return new() { Succeeded = true, Skipped = true };
        }
        var format = plan?.Primary ?? new MediaCandidate { Url = request.Url, Kind = MediaKind.Unknown, Source = "metadata" };
        var anchor = NativeOutputTemplate.Render(request, page, format, plan?.Extension ?? "NA");
        if (protectedPaths.Any(path => SamePath(anchor, path))) throw new IOException("The output filename conflicts with the download archive or its lock file.");
        using var lease = await DestinationLock.AcquireAsync(anchor, ct).ConfigureAwait(false);
        using var workspace=await DownloadWorkspace.OpenAsync(anchor+".sidecars",request.Url,protectedPaths,DownloadWorkspace.Recipe(request),ct,request.OutputDirectory,PublicationTransaction.ContextFor(request,page)).ConfigureAwait(false);
        if(await workspace.RecoveredAsync().ConfigureAwait(false) is { } recovered)
            return new(){Succeeded=true,Skipped=request.NoOverwrites,SidecarPaths=recovered.AllPaths,NewSidecarPaths=request.NoOverwrites?[]:recovered.NewPaths,PublicationCompletion=workspace.PendingCompletion};
        var standalone = request with { EmbedSubs = false, EmbedThumbnail = false, EmbedChapters = false, AddMetadata = false };
        // Full provider documents need no media clock. DASH text representations also carry their own timeline.
        if ((request.WriteSubs || request.WriteAutoSubs) && page.Subtitles.Count == 0 && plan?.Primary.Kind is MediaKind.Dash)
        {
            var manifest = await new DashDownloader(http).ReadAsync(plan.Primary.Url, ct).ConfigureAwait(false);
            page = page with { Subtitles = await DashSubtitleResolver.ResolveAsync(manifest, page.Subtitles, standalone, http, workspace.RawDirectory, ct).ConfigureAwait(false) };
        }
        else if ((request.WriteSubs || request.WriteAutoSubs) && page.Subtitles.Count == 0 && plan?.Primary.Kind is MediaKind.Hls)
        {
            var master = await new HlsDownloader(http).ReadAsync(plan.Primary.Url, ct).ConfigureAwait(false);
            var offered = master.SubtitleRenditions.Select(track => new SubtitleTrack { Url = track.Url ?? plan.Primary.Url, Language = track.Language ?? "und", Extension = "vtt" }).ToArray();
            if (SubtitleSelection.Select(offered, standalone).Count > 0)
                throw new NotSupportedException("Standalone HLS rendition captions need additional media-clock support. Use an exposed full caption document when available.");
        }
        var stagedAnchor = Path.Combine(workspace.ProcessingDirectory, "metadata.bin");
        var sidecars = await SidecarWriter.WriteAsync(standalone, page, stagedAnchor, http, ct).ConfigureAwait(false);
        using var subtitles = await SubtitleWriter.WriteAsync(standalone, page, stagedAnchor, http, ct).ConfigureAwait(false);
        var generated = sidecars.Files.Concat(subtitles.SavedFiles).ToArray();
        if (generated.Length == 0)
        {
            workspace.CompleteWithoutPublishing();
            log?.Invoke("[native] media download skipped; no requested standalone files were available");
            return new() { Succeeded = true, Skipped = true };
        }
        var stem = Path.Combine(Path.GetDirectoryName(anchor)!, Path.GetFileNameWithoutExtension(anchor));
        var groups = generated.Select(path =>
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith("metadata.", StringComparison.Ordinal)) throw new InvalidDataException("A standalone output has an unexpected filename.");
            return new StagedMediaOutput(path, stem + name["metadata".Length..], []);
        }).ToArray();
        using var publication = await DestinationLock.AcquirePublicationsAsync(groups.Select(group => Path.GetDirectoryName(group.Target)!), ct).ConfigureAwait(false);
        var saved = await workspace.PublishAsync(groups, request.NoOverwrites, ct).ConfigureAwait(false);
        foreach (var path in saved.AllPaths) log?.Invoke((saved.NewPaths.Contains(path) ? "[native] saved " : "[native] already saved ") + path);
        return new() { Succeeded = true, Skipped = saved.NewPaths.Count == 0, SidecarPaths = saved.AllPaths, NewSidecarPaths = saved.NewPaths,PublicationCompletion=workspace.PendingCompletion };
    }
}
