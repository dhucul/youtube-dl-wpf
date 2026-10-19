using System.Text;
using YoutubeDl.Engine.Media;

internal static class SubtitleOverlapChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => [("Caption overlap preparation preserves large disjoint documents and bounds dense rendering", Bounds)];
    private static Task Bounds()
    {
        var text = new StringBuilder("WEBVTT\n\n");
        for (var index = 0; index < 10001; index++) text.Append(TimeSpan.FromMilliseconds(index * 2).ToString(@"hh\:mm\:ss\.fff")).Append(" --> ")
            .Append(TimeSpan.FromMilliseconds(index * 2 + 1).ToString(@"hh\:mm\:ss\.fff")).Append("\nA\n\n");
        if (SubtitleOverlap.Normalize(Encoding.UTF8.GetBytes(text.ToString()), ".vtt", default) is not null) throw new InvalidOperationException("A disjoint caption document was rewritten.");
        text.Clear().Append("WEBVTT\n\n");
        for (var index = 0; index < 2000; index++) text.Append(TimeSpan.FromMilliseconds(index).ToString(@"hh\:mm\:ss\.fff")).Append(" --> 00:00:05.000\n").Append(new string('A', 100)).Append("\n\n");
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        try { SubtitleOverlap.Normalize(bytes, ".vtt", default); throw new InvalidOperationException("Dense caption rendering exceeded its budget without failing."); }
        catch (InvalidDataException) { }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { SubtitleOverlap.Normalize(bytes, ".vtt", stop.Token); throw new InvalidOperationException("Caption cancellation was ignored."); }
        catch (OperationCanceledException) { }
        return Task.CompletedTask;
    }
}
