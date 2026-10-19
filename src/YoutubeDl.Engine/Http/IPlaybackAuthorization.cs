namespace YoutubeDl.Engine.Http;

internal readonly record struct PlaybackLease(Uri Url,long Generation);
internal interface IPlaybackAuthorization
{
    // Invoked for each actual authorized request, including renewed URLs and redirects.
    void ApplyRequestHeaders(HttpRequestMessage request) { }
    int MaximumRenewalAttempts => 1;
    bool ShouldRenew(System.Net.HttpStatusCode status) => status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
    bool AppliesTo(Uri url);
    Task<PlaybackLease> AuthorizeAsync(Uri url,CancellationToken ct);
    Task<PlaybackLease> RenewAsync(Uri url,long rejectedGeneration,CancellationToken ct);
    Uri Canonicalize(Uri url);
    string? RecoveryIdentity(Uri url)=>null;
    bool TryRedirect(Uri source,Uri destination)=>false;
    Task ObserveManifestAsync(MediaDocument document,CancellationToken ct)=>Task.CompletedTask;
}
