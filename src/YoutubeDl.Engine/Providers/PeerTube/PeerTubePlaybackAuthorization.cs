using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.PeerTube;

/// <summary>Single-flight renewable file tokens, restricted to the verified video's resource scopes.</summary>
internal sealed class PeerTubePlaybackAuthorization(PeerTubeSession session,string videoId,IReadOnlyList<ScopedMediaQuery> scopes):IPlaybackAuthorization,IDisposable
{
    private readonly SessionLifetime _lifetime=new();
    private readonly Queue<string> _issued=new();
    private string _token="";private DateTimeOffset _expires;private long _generation;
    public bool AppliesTo(Uri url)=>_lifetime.Read(()=>scopes.Any(scope=>scope.Matches(url)));
    public override string ToString()=>"PeerTube playback authorization [redacted]";
    public Task<PlaybackLease> AuthorizeAsync(Uri url,CancellationToken ct)=>LeaseAsync(url,null,ct);
    public Task<PlaybackLease> RenewAsync(Uri url,long rejectedGeneration,CancellationToken ct)=>LeaseAsync(url,rejectedGeneration,ct);
    private async Task<PlaybackLease> LeaseAsync(Uri url,long? rejected,CancellationToken ct)
    {
        using var operation=_lifetime.Begin(ct);ct=operation.Token;
        if(!AppliesTo(url))throw new InvalidDataException("Playback authorization left its verified resource scope.");
        await _lifetime.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var canonical=Canonicalize(url);
            if(_token.Length==0||DateTimeOffset.UtcNow>=_expires||(rejected is { } old&&old==_generation))
            {
                var descriptor=await session.RequestFileTokenAsync(videoId,ct).ConfigureAwait(false);
                var lifetime=descriptor.Expires-DateTimeOffset.UtcNow;
                _lifetime.Commit(ct,()=>{_token=descriptor.Token;_expires=descriptor.Expires-TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(2).Ticks,Math.Max(0,lifetime.Ticks/10)));_generation=checked(_generation+1);
                    lock(_issued){_issued.Enqueue(_token);while(_issued.Count>16)_issued.Dequeue();}});
            }
            var pair="videoFileToken="+Uri.EscapeDataString(_token);
            ct.ThrowIfCancellationRequested();return new(new UriBuilder(canonical){Query=string.Join('&',new[]{canonical.Query.TrimStart('?'),pair}.Where(value=>value.Length>0))}.Uri,_generation);
        }
        finally{_lifetime.Gate.Release();}
    }
    public Uri Canonicalize(Uri url)
    {
        return _lifetime.Read(()=>CanonicalCore(url));
    }
    private Uri CanonicalCore(Uri url)
    {
        if(!AppliesTo(url))return url;
        var pairs=url.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries);
        var tokens=pairs.Where(pair=>Uri.UnescapeDataString(pair.Split('=',2)[0])=="videoFileToken").ToArray();
        if(tokens.Length>1)throw new InvalidDataException("The resource contains ambiguous playback credentials.");
        if(tokens.Length==1)
        {
            var value=Uri.UnescapeDataString(tokens[0].Split('=',2).ElementAtOrDefault(1)??"");
            lock(_issued){if(!_issued.Contains(value,StringComparer.Ordinal))throw new InvalidDataException("The resource contains an unrecognized playback credential.");}
        }
        return tokens.Length==0?url:new UriBuilder(url){Query=string.Join('&',pairs.Where(pair=>Uri.UnescapeDataString(pair.Split('=',2)[0])!="videoFileToken"))}.Uri;
    }
    public void Dispose()=>_lifetime.Dispose(()=>{_token="";lock(_issued)_issued.Clear();});
}
