using System.IO;
using System.Text.Json;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Protocols;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine.BrowserRuntime;

internal sealed class YouTubeCaptureBuffer(string videoId)
{
    private readonly List<string> _players = [];
    private readonly List<(Uri Url, byte[] Body)> _requests = [];
    private bool _startupObserved;
    private bool _refreshed;
    internal bool CanRefresh(TimeSpan elapsed) => !_refreshed && _startupObserved && _players.Count > 0 && _requests.Count == 0 && elapsed >= TimeSpan.FromSeconds(6);
    internal void Refreshed() => _refreshed = true;
    private readonly List<(Uri Url, byte[] Body)> _startup = [];
    private readonly List<YouTubeObservedPlaybackEvidence> _evidence = [];
    private bool _evidenceDirty = true;
    private readonly List<(Uri Url, YouTubeObservedPlaybackEvidence Evidence)> _responses = [];
    internal async Task AddResponseAsync(Uri url, byte[] body, CancellationToken ct)
    {
        if (!_startup.Any(request => request.Url == url) && !_requests.Any(request => request.Url == url)) return;
        var evidence = await YouTubeObservedPlayback.ObserveResponseAsync(videoId, body, ct);
        _responses.Add((url, evidence)); if (_responses.Count > 8) _responses.RemoveAt(0);
    }
    internal void AddPlayer(string json)
    {
        if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("The browser player metadata exceeds its limit.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("videoDetails", out var details) ||
            !details.TryGetProperty("videoId", out var id) || id.GetString() != videoId) return;
        if (root.TryGetProperty("playabilityStatus", out var playability) && playability.TryGetProperty("status", out var status) && status.GetString() != "OK")
        {
            _players.Clear(); _requests.Clear(); _startup.Clear(); _evidence.Clear(); _responses.Clear(); _evidenceDirty = true; _startupObserved = false;
            throw new NotSupportedException("YouTube did not expose public playback for this video.");
        }
        if (_players.Contains(json, StringComparer.Ordinal)) return;
        _players.Add(json); if (_players.Count > 8) _players.RemoveAt(0); _evidenceDirty = true;
    }
    internal void AddRequest(Uri url, byte[] body)
    {
        var fields = ProtobufReader.Read(body, maximumBytes: 256 * 1024, maximumFields: 512);
        var contexts = fields.Where(field => field.Number == 19).ToArray();
        if (contexts.Length != 1 || contexts[0].WireType != 2) return;
        var proofs = ProtobufReader.Read(contexts[0].Bytes, maximumBytes: 65536).Where(field => field.Number == 2).ToArray();
        if (proofs.Length != 1 || proofs[0].WireType != 2 || proofs[0].Bytes.Length is 0 or > 16384) return;
        if (proofs[0].Bytes.Length <= 32) { _startupObserved = true; _startup.Add((url, body.ToArray())); if (_startup.Count > 8) _startup.RemoveAt(0); _evidenceDirty = true; return; }
        _requests.Add((url, body.ToArray())); if (_requests.Count > 8) _requests.RemoveAt(0);
    }
    internal PageMetadata? TryRead(string userAgent, CancellationToken ct,bool requireResponse=false)
    {
        if (_evidenceDirty)
        {
            _evidence.Clear();
            foreach (var startup in _startup)
                foreach (var player in _players)
                    try
                    {
                        var observed = YouTubeObservedPlayback.Observe(player, videoId, startup.Url, startup.Body, ct);
                        if (!_evidence.Any(item => item.Formats.SequenceEqual(observed.Formats))) _evidence.Add(observed);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException or JsonException or ArgumentException) { }
            _evidenceDirty = false;
        }
        foreach (var request in _requests.AsEnumerable().Reverse())
        {
            if(requireResponse&&!_responses.Any(response=>response.Url==request.Url))continue;
            foreach (var player in _players.AsEnumerable().Reverse())
            {
                ct.ThrowIfCancellationRequested();
                try { return YouTubeObservedPlayback.Parse(player, videoId, request.Url, request.Body, userAgent, ct); }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
                { }
                foreach (var prior in _evidence.Concat(_responses.Where(response => response.Url == request.Url || _startup.Any(startup => startup.Url == response.Url)).Select(response => response.Evidence)))
                    try { return YouTubeObservedPlayback.Parse(player, videoId, request.Url, request.Body, userAgent, ct, previous: prior); }
                    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException or JsonException or ArgumentException) { }
            }
        }
        return null;
    }
}
