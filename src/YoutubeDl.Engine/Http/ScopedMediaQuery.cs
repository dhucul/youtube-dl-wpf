namespace YoutubeDl.Engine.Http;

/// <summary>Replays offered query credentials only inside one exact HTTPS resource scope.</summary>
internal sealed class ScopedMediaQuery
{
    private readonly Uri _scope;
    private readonly bool _exactResource;
    private readonly (string Name, string Pair)[] _parameters;
    internal ScopedMediaQuery(Uri scope, IEnumerable<string> encodedPairs, bool exactResource = false)
    {
        if (!scope.IsAbsoluteUri || scope.Scheme != "https" || !scope.IsDefaultPort || scope.UserInfo.Length > 0 || scope.Query.Length > 0 || scope.Fragment.Length > 0 || !exactResource && !scope.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("The media query scope is invalid.");
        _scope = scope; _exactResource = exactResource;
        _parameters = encodedPairs.Take(17).Select(pair =>
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2 || split[0].Length is < 1 or > 64 || split[1].Length is < 1 or > 16384 || pair.Any(char.IsControl) || pair.Contains('&') || pair.Contains('#') ||
                split[0].Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')) throw new ArgumentException("The offered media query parameters are invalid or excessive.");
            return (Name: split[0], Pair: pair);
        }).ToArray();
        if (_parameters.Length is < 1 or > 16 || _parameters.Select(pair => pair.Name).Distinct(StringComparer.Ordinal).Count() != _parameters.Length)
            throw new ArgumentException("The offered media query parameters are ambiguous.");
    }
    internal bool Matches(Uri url) => url.Scheme==_scope.Scheme && url.Host==_scope.Host && url.Port==_scope.Port && url.UserInfo.Length==0 && (_exactResource ? url.AbsolutePath==_scope.AbsolutePath : url.AbsolutePath.StartsWith(_scope.AbsolutePath,StringComparison.Ordinal));
    internal Uri Apply(Uri url)
    {
        if (url.Scheme != _scope.Scheme || url.Host != _scope.Host || url.Port != _scope.Port || url.UserInfo.Length > 0 || (_exactResource ? url.AbsolutePath != _scope.AbsolutePath : !url.AbsolutePath.StartsWith(_scope.AbsolutePath, StringComparison.Ordinal))) return url;
        var existing = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries); var additions = new List<string>();
        foreach (var parameter in _parameters)
        {
            var matches = existing.Where(pair => Uri.UnescapeDataString(pair.Split('=', 2)[0]) == parameter.Name).ToArray();
            if (matches.Length > 1 || matches.Length == 1 && matches[0] != parameter.Pair) throw new InvalidDataException("The resource mixes conflicting playback query credentials.");
            if (matches.Length == 0) additions.Add(parameter.Pair);
        }
        return additions.Count == 0 ? url : new UriBuilder(url) { Query = string.Join('&', existing.Concat(additions)) }.Uri;
    }
    public override string ToString() => "Scoped media query [redacted]";
}
