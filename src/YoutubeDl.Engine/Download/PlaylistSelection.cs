using System.Globalization;
using System.Numerics;

namespace YoutubeDl.Engine.Download;

public static class PlaylistSelection
{
    private readonly record struct Term(BigInteger? Start, BigInteger? End, BigInteger Step, bool Range, bool InfiniteEnd = false);

    public static void Validate(NativeDownloadRequest request)
    {
        if (request.MaximumPlaylistEntries is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(request.MaximumPlaylistEntries));
        _ = Parse(request.PlaylistItems);
    }

    public static IReadOnlyList<NativePlaylistEntry> Select(IReadOnlyList<NativePlaylistEntry> entries, NativeDownloadRequest request)
        => Select(entries, request, Random.Shared);

    internal static IReadOnlyList<NativePlaylistEntry> Select(IReadOnlyList<NativePlaylistEntry> entries, NativeDownloadRequest request, Random random, CancellationToken ct = default)
    {
        Validate(request); ct.ThrowIfCancellationRequested();
        if (entries.Count > request.MaximumPlaylistEntries) throw new InvalidDataException("The playlist inventory exceeds the configured item limit.");
        var byPosition = new Dictionary<int, NativePlaylistEntry>();
        foreach (var entry in entries)
            if (entry.Position < 1 || !byPosition.TryAdd(entry.Position, entry)) throw new InvalidDataException("The playlist contains invalid or repeated source positions.");
        var terms = Parse(request.PlaylistItems);
        if (terms.Count == 0)
        {
            var start = request.PlaylistStart == 0 ? BigInteger.One : request.PlaylistStart;
            BigInteger? end = request.PlaylistEnd == -1 || request.PlaylistEnd == 0 && !request.PlaylistEndExplicit ? null : request.PlaylistEnd;
            terms = [new(start, end, BigInteger.One, true)];
        }
        if (entries.Count == 0) return [];
        // Positions describe the source inventory, including gaps. Never renumber
        // unavailable/missing positions just because a selector changes work order.
        var extent = byPosition.Keys.Max();
        var ascending = entries.OrderBy(entry => entry.Position).ToArray();
        var selected = new List<NativePlaylistEntry>(); var seen = new HashSet<int>();
        foreach (var term in terms)
        {
            ct.ThrowIfCancellationRequested();
            if (!term.Range)
            {
                var position = Resolve(term.Start!.Value, extent);
                if (position >= 1 && position <= int.MaxValue && byPosition.TryGetValue((int)position, out var entry) && seen.Add(entry.Position)) selected.Add(entry);
            }
            else if (!(term.InfiniteEnd && term.Step.Sign < 0))
            {
                var forward = term.Step.Sign > 0;
                var start = term.Start is { } from ? Resolve(from, extent) : forward ? BigInteger.One : extent;
                var end = term.End is { } to ? Resolve(to, extent) : forward ? extent : BigInteger.One;
                var step = BigInteger.Abs(term.Step);
                // Test only known entries. Huge/open endpoints cannot allocate or
                // iterate a huge numerical range, and skipped indexes keep phase.
                for (var offset = 0; offset < ascending.Length; offset++)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = ascending[forward ? offset : ascending.Length - 1 - offset];
                    var position = (BigInteger)entry.Position;
                    if (forward ? position < start || position > end : position > start || position < end) continue;
                    var distance = forward ? position - start : start - position;
                    if (distance % step == 0 && seen.Add(entry.Position)) selected.Add(entry);
                }
            }
            if (selected.Count == entries.Count) break; // All syntax was already validated.
        }
        var ordered = selected.ToArray();
        if (request.PlaylistReverse) Array.Reverse(ordered);
        if (request.PlaylistRandom) random.Shuffle(ordered.AsSpan());
        return ordered;
    }

    private static BigInteger Resolve(BigInteger value, int extent) => value.Sign < 0 ? (BigInteger)extent + 1 + value : value;
    private static List<Term> Parse(string expression)
    {
        var result = new List<Term>();
        if (expression?.Length > 4096) throw new ArgumentException("The playlist selection is too long.");
        if (string.IsNullOrWhiteSpace(expression)) return result;
        foreach (var raw in expression.Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0 || token.Any(char.IsWhiteSpace)) throw new ArgumentException("The playlist selection contains an empty or malformed item.");
            if (IsNumber(token)) { result.Add(new(Number(token), null, BigInteger.One, false)); continue; }
            var colon = token.IndexOf(':');
            var left = colon < 0 ? token : token[..colon];
            var dash = LegacySeparator(left);
            if (dash >= 0)
            {
                var step = colon < 0 ? BigInteger.One : Number(token[(colon + 1)..]);
                result.Add(Range(left[..dash], left[(dash + 1)..], step));
            }
            else if (colon >= 0)
            {
                var parts = token.Split(':');
                if (parts.Length is < 2 or > 3) throw new ArgumentException("Use playlist ranges such as 1:5:2 or -3:-1.");
                result.Add(Range(parts[0], parts[1], parts.Length == 3 ? Number(parts[2]) : BigInteger.One));
            }
            else throw new ArgumentException("Use playlist indexes or ranges such as 1:3,7,-5::2.");
        }
        return result;
    }
    private static Term Range(string from, string to, BigInteger step)
    {
        if (step.IsZero) throw new ArgumentException("A playlist range step cannot be zero.");
        var infinite = to is "inf" or "infinite";
        return new(from.Length == 0 ? null : Number(from), to.Length == 0 || infinite ? null : Number(to), step, true, infinite);
    }
    private static int LegacySeparator(string token)
    {
        var start = token.Length > 0 && token[0] is '+' or '-' ? 1 : 0;
        var end = start;
        while (end < token.Length && char.IsAsciiDigit(token[end])) end++;
        if (end > start && end < token.Length && token[end] == '-') return end;
        return token.StartsWith('-') && end == start ? 0 : -1;
    }
    private static bool IsNumber(string token)
    {
        var start = token.Length > 0 && token[0] is '+' or '-' ? 1 : 0;
        return start < token.Length && token[start..].All(char.IsAsciiDigit);
    }
    private static BigInteger Number(string token)
    {
        if (!IsNumber(token)) throw new ArgumentException("The playlist selection contains an invalid signed integer.");
        return BigInteger.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}
