using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Jint;
using YoutubeDl.Engine.Providers.YouTube;

namespace YoutubeDl.Engine.PlayerRuntime;

/// <summary>
/// Runs the public player's own transformations in a managed interpreter. Only strings cross
/// the boundary; CLR interop, file/network access, modules, browser accounts, and timers are absent.
/// A session owns its interpreter and is discarded after any execution or validation failure.
/// </summary>
public sealed class ManagedYouTubePlayerRuntime : IYouTubePlayerRuntime
{
    public const int MaximumScriptBytes = 3 * 1024 * 1024;
    private static readonly TimeSpan OperationLimit = TimeSpan.FromSeconds(8);

    public IYouTubePlayerSession CreateSession(string playerScript, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(playerScript);
        if (Encoding.UTF8.GetByteCount(playerScript) > MaximumScriptBytes)
            throw new InvalidDataException("The YouTube player script exceeds the native runtime limit.");
        // Discover the URL-building helper by behavior, rather than pinning a minified function name.
        // Unknown/ambiguous layouts remain unsupported; no guessed transformation is accepted.
        var matches = Regex.Matches(playerScript,
            @"(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*function\s*\(\s*(?<arg>[A-Za-z_$][A-Za-z0-9_$]*)\s*,[^)]{0,100}\)\s*\{\s*\k<arg>\s*=\s*new\s+g\.[A-Za-z_$][A-Za-z0-9_$]*\s*\(\s*\k<arg>\s*,\s*!0\s*\)\s*;\s*\k<arg>\.set\(\s*""alr""\s*,\s*""yes""\s*\)\s*;",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var tail = Regex.Match(playerScript, @"\}\)\(\s*_yt_player\s*\)\s*;\s*\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (matches.Count != 1 || !tail.Success)
            throw new NotSupportedException("This YouTube player layout is not supported by the native runtime.");
        var instrumented = playerScript[..tail.Index] + "globalThis.__nativePlayerTransform=" +
            matches[0].Groups["name"].Value + ";" + playerScript[tail.Index..];
        return new Session(instrumented, ct);
    }

    private sealed class Session : IYouTubePlayerSession
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly OperationConstraint _operation = new();
        private Jint.Engine? _engine;

        public Session(string script, CancellationToken ct)
        {
            _engine = new Jint.Engine(options =>
            {
                options.LimitMemory(256 * 1024 * 1024).MaxStatements(2_000_000).LimitRecursion(100).Constraint(_operation);
                options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(250);
            });
            _operation.Begin(ct);
            try
            {
                _engine.Execute(Environment);
                _engine.Execute(script);
                _operation.Check();
            }
            catch (OperationCanceledException) { Fault(); throw; }
            catch (Exception ex) { Fault(); throw new NotSupportedException("The native runtime could not initialize this YouTube player.", ex); }
            finally { _operation.End(); }
        }

        public Uri Transform(Uri url, string signatureParameter, string signature, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateInput(url, signatureParameter, signature);
            _gate.Wait(ct);
            try
            {
                ObjectDisposedException.ThrowIf(_engine is null, this);
                _operation.Begin(ct);
                try
                {
                    _engine.SetValue("__nativeUrl", url.AbsoluteUri)
                        .SetValue("__nativeParameter", signatureParameter).SetValue("__nativeSignature", signature);
                    var output = _engine.Evaluate(Serialize).AsString();
                    _operation.Check();
                    if (output.Length > 64 * 1024 || !Uri.TryCreate(output, UriKind.Absolute, out var result) ||
                        result.Scheme != "https" || result.Host != url.Host || result.Port != url.Port ||
                        result.AbsolutePath != url.AbsolutePath || result.UserInfo.Length != 0 || result.Fragment.Length != 0)
                        throw new InvalidDataException("The YouTube player transformation changed the media origin or path.");
                    return result;
                }
                catch (OperationCanceledException) { Fault(); throw; }
                catch (InvalidDataException) { Fault(); throw; }
                catch (Exception ex) { Fault(); throw new NotSupportedException("The native runtime could not transform this YouTube media address.", ex); }
                finally { _operation.End(); }
            }
            finally { _gate.Release(); }
        }

        public void Dispose()
        {
            _gate.Wait();
            try { Fault(); }
            finally { _gate.Release(); }
        }

        private void Fault() { _engine?.Dispose(); _engine = null; }

        private static void ValidateInput(Uri url, string parameter, string signature)
        {
            if (!url.IsAbsoluteUri || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
                !(url.Host == "googlevideo.com" || url.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)) || url.AbsoluteUri.Length > 64 * 1024)
                throw new InvalidDataException("The player runtime requires a bounded HTTPS GoogleVideo address.");
            if (signature.Length > 16 * 1024 || !Regex.IsMatch(parameter, @"\A[A-Za-z0-9_]{1,32}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidDataException("The player runtime received an invalid signature input.");
        }
    }

    private sealed class OperationConstraint : Constraint
    {
        private CancellationToken _ct;
        private long _deadline;
        public void Begin(CancellationToken ct) { _ct = ct; _deadline = Stopwatch.GetTimestamp() + (long)(OperationLimit.TotalSeconds * Stopwatch.Frequency); }
        public void End() { _deadline = 0; _ct = default; }
        public override void Check()
        {
            _ct.ThrowIfCancellationRequested();
            if (_deadline != 0 && Stopwatch.GetTimestamp() >= _deadline) throw new TimeoutException("The player operation exceeded its time limit.");
        }
        // Execute/Evaluate reset normal counters. The host-owned deadline spans the whole operation.
        public override void Reset() { }
    }

    private const string Serialize = """
        (function(){
          var value=__nativePlayerTransform(__nativeUrl,__nativeParameter,__nativeSignature);
          var keys=Object.getOwnPropertyNames(Object.getPrototypeOf(value));
          for(var key of keys){
            if(key==='constructor'||key==='clone'||key==='get'||key==='set')continue;
            var method=value[key];
            if(typeof method==='function' && method.length===0){
              var result=method.call(value);
              if(typeof result==='string' && /^https:\/\//.test(result))return result;
            }
          }
          throw new Error('No player URL serializer found');
        })()
        """;

    // Minimal inert DOM-shaped values needed while initializing the player. These have no
    // backing browser or native objects. Requests, event callbacks and timers cannot run.
    private const string Environment = """
        var window=globalThis,self=globalThis,location={href:'https://www.youtube.com/',protocol:'https:',hostname:'www.youtube.com'};
        var navigator={userAgent:'Mozilla/5.0',platform:'Win32',language:'en-US',languages:['en-US'],cookieEnabled:false};
        var performance={now:function(){return 1},timeOrigin:0};
        var setTimeout=function(){return 0},clearTimeout=function(){},setInterval=function(){return 0},clearInterval=function(){};
        var addEventListener=function(){},removeEventListener=function(){};
        var XMLHttpRequest=function(){this.open=function(){};this.send=function(){};this.abort=function(){};this.setRequestHeader=function(){};this.addEventListener=function(){}};
        var document={location:location,documentElement:{style:{}},body:{},cookie:'',readyState:'complete',createElement:function(tag){return {style:{},tagName:tag,canPlayType:function(){return ''},setAttribute:function(){},appendChild:function(){},getContext:function(){return null}}},getElementsByTagName:function(){return []},querySelector:function(){return null},querySelectorAll:function(){return []},addEventListener:function(){},removeEventListener:function(){}};
        var console={log:function(){},warn:function(){},error:function(){}};
        """;
}
