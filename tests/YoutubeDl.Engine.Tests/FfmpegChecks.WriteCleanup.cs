internal static partial class FfmpegChecks
{
    internal static IEnumerable<(string Name,Func<Task> Test)> WriteCleanupMediaChecks(string ffmpeg)=>
        new[]{"receipt-plan-writing","receipt-plan-flushed","output-writing:0","output-writing:1","output-writing:2","output-flushed:0",
            "receipt-commit-writing","receipt-commit-flushed","receipt-commit-replaced","archive-writing","archive-flushed","archive-replaced",
            "receipt-archive-writing","receipt-claim-writing","cleanup-sibling:0","cleanup-entry:0","cleanup-receipt","cleanup-empty","cleanup-root"}
        .Select(phase=>("Write cleanup media "+phase,(Func<Task>)(()=>PublicationMedia(ffmpeg,phase))));
}
