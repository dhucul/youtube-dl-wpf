using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace YoutubeDl.Engine.Http;

public sealed record MediaDocument(Uri Url, string Text, string? ContentType)
{
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
    internal Uri? EffectiveUrl { get; init; }
}

/// <summary>
/// Shared HTTP entry point for discovery and downloads. Callers pass cookies,
/// a proxy, and a user agent; nothing here stores credentials of its own.
/// </summary>
public sealed class MediaHttp : IDisposable
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _client;
    private readonly HttpMessageHandler _handler;
    private readonly TimeSpan _readTimeout;
    private readonly MediaHttpOptions _configuration;
    private readonly bool _managedHandler;
    private readonly bool _ownsHandler;
    public bool AllowAutoRedirect { get; }
    internal bool CanControlRedirects=>_managedHandler||!AllowAutoRedirect;
    internal MediaHttp CreateDiscoverySession(bool sensitive)=>CanControlRedirects||sensitive?CreateWithoutRedirects():WithHeaders(new Dictionary<string,string>());
    public IReadOnlyList<string> SessionWarnings => _configuration.SessionWarnings;
    private readonly Dictionary<string, IDisposable> _sessionState = new(StringComparer.Ordinal);
    private bool _disposed;
    internal T SessionState<T>(string key, Func<T> create) where T : class, IDisposable
    {
        lock (_sessionState) { ObjectDisposedException.ThrowIf(_disposed,this);if (!_sessionState.TryGetValue(key,out var state)) _sessionState.Add(key,state=create()); return (T)state; }
    }

    public MediaHttp(MediaHttpOptions? options = null)
        : this(CopyOptions(options ?? new MediaHttpOptions()), true) { }

    private MediaHttp(MediaHttpOptions options, bool managed) : this(CreateHandler(options), options, true, managed) { }

    public MediaHttp(HttpMessageHandler handler, MediaHttpOptions? options = null)
        : this(handler, options, true, false) { }

    private MediaHttp(HttpMessageHandler handler, MediaHttpOptions? options, bool ownsHandler, bool managedHandler)
    {
        _ownsHandler = ownsHandler;
        _managedHandler = managedHandler;
        options ??= new MediaHttpOptions();
        NativeHttpHeaders.Validate(options.Headers);
        NetworkConnection.Validate(options.SourceAddress, options.IpVersion);
        _configuration = CopyOptions(options);
        AllowAutoRedirect = options.AllowAutoRedirect;
        _readTimeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 300));
        _handler = handler;
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = _readTimeout };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            string.IsNullOrWhiteSpace(options.UserAgent) ? DefaultUserAgent : options.UserAgent);
        if (!string.IsNullOrWhiteSpace(options.Referer))
            _client.DefaultRequestHeaders.Referrer = CreateReferrer(options.Referer);
        foreach (var (name, value) in options.Headers)
        {
            // Content headers are applied to the actual body in SendAsync.
            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) continue;
            _client.DefaultRequestHeaders.Remove(name);
            if (!_client.DefaultRequestHeaders.TryAddWithoutValidation(name, value))
                throw new NotSupportedException("This custom HTTP header requires additional native support.");
        }
        if (options.ForwardedFor is { } address && !options.Headers.ContainsKey("X-Forwarded-For"))
        {
            if (!System.Net.IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("The forwarded address must be one IPv4 address.");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", address);
        }
    }

    public MediaHttp CreateWithoutRedirects()
    {
        var options = CopyConfiguration(); options.AllowAutoRedirect = false;
        if (!_managedHandler)
        {
            if (AllowAutoRedirect) throw new InvalidOperationException("Injected HTTP handlers must disable automatic redirects explicitly for controlled requests.");
            return new MediaHttp(_handler,options,false,false);
        }
        return new MediaHttp(options);
    }
    internal MediaHttp WithHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var options = CopyConfiguration();
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Referer", StringComparison.OrdinalIgnoreCase)) { options.Referer = value; options.Headers.Remove(name); }
            else if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) { options.UserAgent = value; options.Headers.Remove(name); }
            else options.Headers[name] = value;
        }
        return new MediaHttp(_handler, options, false, _managedHandler);
    }
    internal MediaHttp WithAuthorization(IPlaybackAuthorization authorization)
    {
        var options=CopyConfiguration();options.PlaybackAuthorization=authorization;options.AllowAutoRedirect=false;
        if(_managedHandler)return new MediaHttp(options);
        if(AllowAutoRedirect)throw new NotSupportedException("Private resource handlers must disable redirects explicitly.");
        return new MediaHttp(_handler,options,false,false);
    }
    internal Uri CanonicalizePlaybackUri(Uri url)=>_configuration.PlaybackAuthorization?.Canonicalize(url)??url;
    internal string RecoveryIdentity(Uri url)=>_configuration.PlaybackAuthorization?.AppliesTo(url)==true?
        _configuration.PlaybackAuthorization.RecoveryIdentity(url)??Download.AdaptiveFragmentCheckpoint.Hash(url.AbsoluteUri):Download.AdaptiveFragmentCheckpoint.Hash(url.AbsoluteUri);
    internal Task ObservePlaybackManifestAsync(MediaDocument document,CancellationToken ct)=>_configuration.PlaybackAuthorization?.AppliesTo(document.Url)==true?_configuration.PlaybackAuthorization.ObserveManifestAsync(document,ct):Task.CompletedTask;
    internal async Task<MediaDocument> ReadPlaybackManifestAsync(Uri url,Func<Uri,bool>? redirect,string? referer,CancellationToken ct)
    {
        if(AllowAutoRedirect)throw new InvalidOperationException("Adaptive graph renewal requires controlled redirects.");
        var current=url;
        for(var hop=0;hop<=5;hop++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,current);if(referer is not null)request.Headers.Referrer=CreateReferrer(referer);
            using var response=await SendAsync(request,ct).ConfigureAwait(false);
            if((int)response.StatusCode is >=300 and <400)
            {
                var location=response.Headers.Location;var next=location is null?null:location.IsAbsoluteUri?location:new Uri(current,location);
                if(next is null||hop==5||redirect?.Invoke(next)!=true)throw new InvalidDataException("The renewed manifest redirected outside its declared playback scope.");current=next;continue;
            }
            response.EnsureSuccessStatusCode();var bytes=await ReadLimitedAsync(response,8*1024*1024,ct).ConfigureAwait(false);using var memory=new MemoryStream(bytes);using var reader=new StreamReader(memory,Encoding.UTF8,true);
            return new(current,await reader.ReadToEndAsync(ct).ConfigureAwait(false),response.Content.Headers.ContentType?.MediaType){EffectiveUrl=current};
        }
        throw new InvalidDataException("The adaptive redirect limit was exceeded.");
    }
    private static readonly HttpRequestOptionsKey<Uri> EffectivePlaybackUri=new("NativeEffectivePlaybackUri");
    internal MediaHttp WithQueries(IReadOnlyList<ScopedMediaQuery> queries)
    {
        if (queries.Count > 16) throw new ArgumentException("Too many media query scopes were supplied.");
        var options = CopyConfiguration(); options.ResourceQueries = queries.ToArray();
        return new MediaHttp(_handler, options, false, _managedHandler);
    }
    internal MediaHttp CreatePublicServiceSession(bool preserveForwardedFor = false)
    {
        var options = CopyConfiguration();
        if (!_managedHandler && (AllowAutoRedirect || options.CookieContainer is { Count: > 0 })) throw new NotSupportedException("The injected service handler must isolate redirects and cookies.");
        if (!preserveForwardedFor) options.ForwardedFor = null;
        options.Headers.Clear(); options.CookieContainer = new CookieContainer(); options.Referer = ""; options.UserAgent = DefaultUserAgent;
        options.ResourceQueries = []; options.PlaybackAuthorization = null; options.AllowAutoRedirect = false; options.SessionWarnings = [];
        return _managedHandler ? new MediaHttp(options) : new MediaHttp(_handler, options, false, false);
    }
    internal MediaHttp CreateVerificationSession(string? proxy)
    {
        if (string.IsNullOrEmpty(proxy)) return CreatePublicServiceSession(preserveForwardedFor: true);
        _ = NativeGeoVerification.ProxyValue(proxy);
        if (!_managedHandler) throw new NotSupportedException("An opaque injected HTTP handler cannot replace its geo verification proxy.");
        var options = CopyConfiguration(); options.Proxy = proxy; options.CookieContainer = new CookieContainer(); options.Headers.Clear();
        options.UserAgent = DefaultUserAgent; options.Referer = ""; options.ResourceQueries = []; options.PlaybackAuthorization = null; options.SessionWarnings = []; options.AllowAutoRedirect = false;
        return new MediaHttp(options);
    }
    internal Task<byte[]> ReadBoundedBytesAsync(HttpResponseMessage response, long maximumBytes, CancellationToken ct) => ReadLimitedAsync(response, maximumBytes, ct);

    internal MediaHttp CreateAnonymousSession(bool allowTlsResume = true)
    {
        var options = CopyConfiguration();
        options.CookieContainer = new CookieContainer();
        options.AllowTlsResume = options.AllowTlsResume && allowTlsResume;
        foreach (var name in new[] { "Authorization", "Cookie", "X-Lbry-Auth-Token" }) options.Headers.Remove(name);
        if (_managedHandler) return new MediaHttp(options);
        // Injected handlers are used by test/host integrations. Their cookie
        // behavior is opaque, so configured authenticated jars are not reusable.
        if (_configuration.CookieContainer is { Count: > 0 }) throw new InvalidOperationException("An injected HTTP handler with cookies cannot provide an anonymous session.");
        return new MediaHttp(_handler, options, false, false);
    }
    internal Providers.YouTube.YouTubePlaybackCookies YouTubeCookies(CancellationToken ct) => Providers.YouTube.YouTubePlaybackCookies.FromContainer(_configuration.CookieContainer!, ct);
    internal MediaHttp CreateCookieSession(Providers.YouTube.YouTubePlaybackCookies cookies)
    {
        if (!_managedHandler) throw new NotSupportedException("An injected HTTP handler cannot replace its cookie ownership safely.");
        var options = CopyConfiguration(); options.CookieContainer = cookies.ToContainer();
        foreach (var name in new[] { "Authorization", "Cookie", "X-Lbry-Auth-Token" }) options.Headers.Remove(name);
        return new MediaHttp(options);
    }
    private static MediaHttpOptions CopyOptions(MediaHttpOptions source)
    {
        var options = new MediaHttpOptions { AllowTlsResume = source.AllowTlsResume, AllowAutoRedirect = source.AllowAutoRedirect, UserAgent = source.UserAgent, Referer = source.Referer,
            ForwardedFor = source.ForwardedFor, Proxy = source.Proxy, SourceAddress = source.SourceAddress, IpVersion = source.IpVersion, NoCheckCertificate = source.NoCheckCertificate,
            TimeoutSeconds = source.TimeoutSeconds, CookieContainer = source.CookieContainer ?? new CookieContainer(), SessionWarnings = source.SessionWarnings.ToArray(), ResourceQueries = source.ResourceQueries.ToArray(),PlaybackAuthorization=source.PlaybackAuthorization };
        foreach (var pair in source.Headers) options.Headers.Add(pair.Key, pair.Value);
        return options;
    }
    private MediaHttpOptions CopyConfiguration(){lock(_sessionState){ObjectDisposedException.ThrowIf(_disposed,this);return CopyOptions(_configuration);}}

    private static HttpMessageHandler CreateHandler(MediaHttpOptions options)
    {
        NativeHttpHeaders.Validate(options.Headers);
        var (source, family) = NetworkConnection.Validate(options.SourceAddress, options.IpVersion);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = options.AllowAutoRedirect,
            MaxAutomaticRedirections = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120))
        };
        if (!string.IsNullOrWhiteSpace(options.Proxy))
            handler.Proxy = new WebProxy(options.Proxy);
        handler.CookieContainer = options.CookieContainer ?? new CookieContainer();
        handler.UseCookies = true;
        handler.SslOptions.AllowTlsResume = options.AllowTlsResume;
        if (source is not null || options.IpVersion != 0)
            handler.ConnectCallback = (context, ct) => NetworkConnection.ConnectAsync(context, source, family, ct);
        if (options.NoCheckCertificate) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        return handler;
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => SendCoreAsync(request, _client, ct);

    internal Task<HttpResponseMessage> SendDownloadAsync(HttpRequestMessage request, NativeExternalDownloader.Selection? selection, CancellationToken ct)
    {
        if (selection is null) return SendAsync(request, ct);
        if (!_managedHandler) throw new NotSupportedException("An injected HTTP handler cannot delegate its opaque transport and cookie state to curl.");
        var key = "curl-download:" + selection.Program + "\0" + string.Join("\0", selection.Arguments);
        var state = SessionState(key, () =>
        {
            var client = new HttpClient(new CurlDownloadHandler(CopyConfiguration(), selection)) { Timeout = _readTimeout };
            foreach (var header in _client.DefaultRequestHeaders) client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            return new CurlClient(client);
        });
        return SendCoreAsync(request, state.Client, ct);
    }
    private sealed class CurlClient(HttpClient client) : IDisposable { internal HttpClient Client => client; public void Dispose() => client.Dispose(); }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, HttpClient client, CancellationToken ct)
    {
        await NativeExtractionDelay.BeforeRequestAsync(ct).ConfigureAwait(false);
        if (request.RequestUri is { } uri)
        {
            foreach (var query in _configuration.ResourceQueries) uri = query.Apply(uri);
            request.RequestUri = uri;
        }
        foreach (var (name, value) in _configuration.Headers.Where(pair => pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)))
        {
            request.Content ??= new ByteArrayContent([]);
            request.Content.Headers.Remove(name);
            if (!request.Content.Headers.TryAddWithoutValidation(name, value)) throw new NotSupportedException("This custom content header requires additional native support.");
        }
        var authorization=_configuration.PlaybackAuthorization;
        PlaybackLease? lease=null;var original=request.RequestUri;
        if(original is not null&&authorization?.AppliesTo(original)==true)
        {
            if(request.Method!=HttpMethod.Get&&request.Method!=HttpMethod.Head)throw new NotSupportedException("Private resource renewal requires a read-only request.");
            lease=await authorization.AuthorizeAsync(original,ct).ConfigureAwait(false);request.RequestUri=lease.Value.Url;
        }
        if(lease is not null)
        {
            var current=request;var renewals=0;var redirects=0;
            try
            {
                while(true)
                {
                    authorization!.ApplyRequestHeaders(current);
                    var response=await client.SendAsync(current,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
                    Uri? next=null;
                    if((int)response.StatusCode is >=300 and <400)
                    {
                        var location=response.Headers.Location;var target=location is null?null:location.IsAbsoluteUri?location:new Uri(current.RequestUri!,location);
                        if(target is null||++redirects>5||!authorization!.TryRedirect(current.RequestUri!,target)){response.Dispose();throw new InvalidDataException("The playback redirect is outside its permitted resource scope.");}next=target;
                    }
                    else if(renewals < Math.Clamp(authorization!.MaximumRenewalAttempts, 0, 6) && authorization.ShouldRenew(response.StatusCode))
                    {response.Dispose();lease=await authorization!.RenewAsync(original!,lease.Value.Generation,ct).ConfigureAwait(false);next=lease.Value.Url;renewals++;}
                    if(next is null)
                    {
                        response.RequestMessage??=current;var message=response.RequestMessage;var final=message.RequestUri??current.RequestUri!;
                        message.Options.Set(EffectivePlaybackUri,final);message.RequestUri=authorization!.Canonicalize(final);
                        return response;
                    }
                    response.Dispose();if(!ReferenceEquals(current,request))current.Dispose();
                    current=new HttpRequestMessage(request.Method,next){Version=request.Version,VersionPolicy=request.VersionPolicy};
                    foreach(var header in request.Headers)current.Headers.TryAddWithoutValidation(header.Key,header.Value);
                }
            }
            finally{if(!ReferenceEquals(current,request))current.Dispose();request.RequestUri=authorization!.Canonicalize(request.RequestUri!);}
        }
        return await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
    }

    public async Task<string> GetStringAsync(Uri url, long maximumBytes, CancellationToken ct)
        => (await GetDocumentAsync(url, maximumBytes, ct).ConfigureAwait(false)).Text;

    public async Task<MediaDocument> GetDocumentAsync(Uri url, long maximumBytes, CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null, bool mediaHeadersOnly = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null) foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUrl = response.RequestMessage?.RequestUri ?? url;
        var effectiveUrl=response.RequestMessage?.Options.TryGetValue(EffectivePlaybackUri,out var effective)==true?effective:finalUrl;
        var mime = response.Content.Headers.ContentType?.MediaType;
        if (mediaHeadersOnly && Discovery.PageMediaExtractor.Classify(finalUrl, mime) is Discovery.MediaKind.Progressive or Discovery.MediaKind.Hls or Discovery.MediaKind.Dash)
            return new(finalUrl, "", mime) { StatusCode = response.StatusCode };
        var bytes = await ReadLimitedAsync(response, maximumBytes, ct).ConfigureAwait(false);
        using var memory = new MemoryStream(bytes);
        Encoding defaultEncoding = Encoding.UTF8;
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0, 60 })) defaultEncoding = new UTF32Encoding(bigEndian: true, byteOrderMark: false);
        else if (bytes.AsSpan().StartsWith(new byte[] { 60, 0, 0, 0 })) defaultEncoding = Encoding.UTF32;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 60, 0, 63 })) defaultEncoding = Encoding.BigEndianUnicode;
        else if (bytes.AsSpan().StartsWith(new byte[] { 60, 0, 63, 0 })) defaultEncoding = Encoding.Unicode;
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (string.IsNullOrWhiteSpace(charset))
        {
            var declaration = Regex.Match(Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024)),
                @"\A\s*<\?xml\s[^?]{0,512}?\bencoding\s*=\s*['""](?<encoding>[A-Za-z0-9._-]{1,64})['""]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (declaration.Success) charset = declaration.Groups["encoding"].Value;
        }
        using var reader = new StreamReader(memory, string.IsNullOrWhiteSpace(charset) ? defaultEncoding : Encoding.GetEncoding(charset), detectEncodingFromByteOrderMarks: true);
        return new(finalUrl, await reader.ReadToEndAsync(ct).ConfigureAwait(false), mime) { StatusCode = response.StatusCode,EffectiveUrl=effectiveUrl };
    }

    internal async ValueTask<int> ReadAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_readTimeout);
        try
        {
            return await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("The media server stopped sending data.");
        }
    }

    public async Task<byte[]> GetBytesAsync(Uri url, long maximumBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(response, maximumBytes, ct).ConfigureAwait(false);
    }

    public async Task<string> PostJsonAsync(Uri url, object body, long maximumBytes, IReadOnlyDictionary<string, string>? headers, CancellationToken ct, bool includeCharset = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (!includeCharset) request.Content.Headers.ContentType!.CharSet = null;
        if (headers is not null) foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new UTF8Encoding(false, true).GetString(await ReadLimitedAsync(response, maximumBytes, ct).ConfigureAwait(false));
    }

    internal async Task<MediaDocument> PostJsonDocumentAsync(Uri url, object body, long maximumBytes, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        if (headers is not null) foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await SendAsync(request, ct).ConfigureAwait(false); response.EnsureSuccessStatusCode();
        var text = new UTF8Encoding(false, true).GetString(await ReadLimitedAsync(response, maximumBytes, ct).ConfigureAwait(false));
        return new(response.RequestMessage?.RequestUri ?? url, text, response.Content.Headers.ContentType?.MediaType);
    }

    public async Task<MediaDocument> PostFormDocumentAsync(Uri url, IEnumerable<KeyValuePair<string, string>> fields, long maximumBytes, CancellationToken ct,IReadOnlyDictionary<string,string>? headers=null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        if(headers is not null)foreach(var header in headers)request.Headers.TryAddWithoutValidation(header.Key,header.Value);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = new UTF8Encoding(false, true).GetString(await ReadLimitedAsync(response, maximumBytes, ct).ConfigureAwait(false));
        return new(response.RequestMessage?.RequestUri ?? url, text, response.Content.Headers.ContentType?.MediaType) { StatusCode = response.StatusCode };
    }

    internal Stream WithReadTimeout(Stream stream) => new TimedReadStream(this, stream);

    private sealed class TimedReadStream(MediaHttp owner, Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => owner.ReadAsync(source, buffer, ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }



    private async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long maximumBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (maximumBytes > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (response.Content.Headers.ContentLength is > 0 and var declared && declared > maximumBytes)
            throw new InvalidDataException("The resource is larger than the allowed size.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await ReadAsync(stream, buffer, ct).ConfigureAwait(false);
            if (count == 0) break;
            if (memory.Length + count > maximumBytes)
                throw new InvalidDataException("The resource is larger than the allowed size.");
            memory.Write(buffer, 0, count);
        }
        if (response.Content.Headers.ContentLength is { } length && memory.Length != length)
            throw new InvalidDataException("The resource was incomplete.");
        return memory.ToArray();
    }

    public void Dispose()
    {
        lock (_sessionState) { if(_disposed)return;_disposed=true;foreach (var state in _sessionState.Values) state.Dispose(); _sessionState.Clear(); }
        _client.Dispose();
        if (_ownsHandler) _handler.Dispose();
    }

    private static Uri? CreateReferrer(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

public sealed class MediaHttpOptions
{
    internal string? ForwardedFor { get; set; }
    internal bool AllowTlsResume { get; set; } = true;
    public bool AllowAutoRedirect { get; set; } = true;
    public string UserAgent { get; set; } = MediaHttp.DefaultUserAgent;
    public string Referer { get; set; } = "";
    public string Proxy { get; set; } = "";
    public string SourceAddress { get; set; } = "";
    public int IpVersion { get; set; }
    public bool NoCheckCertificate { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public CookieContainer? CookieContainer { get; set; }
    public IReadOnlyList<string> SessionWarnings { get; set; } = [];
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<ScopedMediaQuery> ResourceQueries { get; set; } = [];
    internal IPlaybackAuthorization? PlaybackAuthorization { get; set; }
}

