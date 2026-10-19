using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Providers.Bandlab;

internal sealed record BandlabAddress(string Kind, string? Id, string? Owner = null, string? Slug = null, string? Revision = null)
{
    internal static bool Matches(Uri url) { try { Parse(url); return true; } catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { return false; } }
    internal static BandlabAddress Parse(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host is not ("bandlab.com" or "www.bandlab.com") || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use a public BandLab track, revision, embed or playlist address.");
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2); var name = Uri.UnescapeDataString(pair[0]);
            if (!query.TryAdd(name, pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "")) throw new ArgumentException("BandLab repeated a selection parameter.");
            if (name is not ("id" or "revId" or "blur" or "triggeredFrom" or "commentId" or "replyId") && !name.StartsWith("utm_", StringComparison.Ordinal)) throw new ArgumentException("This BandLab access or selection query needs additional native support.");
        }
        var path = url.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (path.SequenceEqual(new[] { "embed" }) && query.Remove("id", out var embedded)) { CheckQuery(false); return new("revision", GuidId(embedded)); }
        if (query.ContainsKey("id") || query.ContainsKey("blur")) throw new ArgumentException("BandLab embed settings need an embedded track address.");
        if (path.Length == 2 && path[0] is "track" or "post" or "revisions") { CheckQuery(false); return new(path[0] == "revisions" ? "revision" : "post", GuidId(path[1])); }
        if (path.Length == 2 && path[0] == "collections") { CheckQuery(false); return new("collection", GuidId(path[1])); }
        if (path.Length == 3 && Username(path[0]) && path[1] == "collections") { CheckQuery(false); return new("collection", GuidId(path[2]), path[0]); }
        if (path.Length == 2 && Username(path[0]) && SlugValue(path[1])) { CheckQuery(true); return new("song", null, path[0], path[1], query.TryGetValue("revId", out var revision) ? GuidId(revision) : null); }
        throw new ArgumentException("This BandLab route needs additional native support.");
        void CheckQuery(bool selectedRevision) { if (!selectedRevision && query.ContainsKey("revId")) throw new ArgumentException("This explicit BandLab revision selector needs a canonical song address."); }
    }
    internal static string GuidId(string? value) => value is not null && Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id.ToString("D") : throw new InvalidDataException("BandLab returned an invalid recording identity.");
    internal static bool Username(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9_]{2,19}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static bool SlugValue(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9-]{0,127}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal Uri Web => new("https://www.bandlab.com/" + (Kind == "song" ? Owner + "/" + Slug + (Revision is null ? "" : "?revId=" + Revision) : Kind == "collection" ? (Owner is null ? "" : Owner + "/") + "collections/" + Id : Kind == "post" ? "track/" + Id : "revisions/" + Id));
}
