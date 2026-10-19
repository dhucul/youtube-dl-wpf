using System.Diagnostics;
using System.Net;
using System.Text;
using YoutubeDl.Engine.Media;

namespace YoutubeDl.Engine.Http;

// Used only for explicitly selected HTTP file transfers. Discovery stays in MediaHttp.
internal sealed class CurlDownloadHandler(MediaHttpOptions options, NativeExternalDownloader.Selection selection) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.Content is not null) throw new NotSupportedException("curl file transfers require a body-free GET request.");
        var current = request.RequestUri!; var initial = current; var redirected = false;
        for (var hop = 0; ; hop++)
        {
            var response = await SendOnce(request, current, initial, redirected, ct).ConfigureAwait(false);
            if (!options.AllowAutoRedirect || response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var location = response.Headers.Location;
            var target = location is null ? null : location.IsAbsoluteUri ? location : new Uri(current, location);
            response.Dispose();
            if (target is null || hop >= 9 || target.Scheme is not ("http" or "https") || current.Scheme == "https" && target.Scheme != "https" || target.UserInfo.Length != 0)
                throw new InvalidDataException("The curl media redirect is invalid or exceeds its limit.");
            current = target; redirected = true;
        }
    }

    private async Task<HttpResponseMessage> SendOnce(HttpRequestMessage request, Uri url, Uri initial, bool redirected, CancellationToken ct)
    {
        if (url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0) throw new NotSupportedException("curl media URLs must use HTTP/HTTPS without embedded credentials.");
        var config = new StringBuilder();
        void Option(string key, string value)
        {
            if (value.Any(char.IsControl)) throw new ArgumentException("A curl request value contains a control character.");
            config.Append(key).Append(" = \"").Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\"\n");
        }
        Option("url", url.AbsoluteUri);
        Option("connect-timeout", Math.Clamp(options.TimeoutSeconds, 5, 120).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var proxy = options.Proxy ?? "";
        if (proxy.Length == 0 && !HttpClient.DefaultProxy.IsBypassed(url)) proxy = HttpClient.DefaultProxy.GetProxy(url)?.AbsoluteUri ?? "";
        Option("proxy", proxy); Option("noproxy", proxy.Length == 0 ? "*" : "");
        if (options.SourceAddress.Length > 0) Option("interface", options.SourceAddress);
        if (options.IpVersion != 0) config.Append(options.IpVersion == 4 ? "ipv4\n" : "ipv6\n");
        if (options.NoCheckCertificate) config.Append("insecure\n");
        if (!options.AllowTlsResume) config.Append("no-sessionid\n");
        var sameOrigin = url.Scheme == initial.Scheme && url.IdnHost == initial.IdnHost && url.Port == initial.Port;
        string? explicitCookie = null;
        foreach (var header in request.Headers)
        {
            if (redirected && (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || !sameOrigin && !SafeRedirectHeader(header.Key))) continue;
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) { explicitCookie = string.Join("; ", header.Value); continue; }
            foreach (var value in header.Value) Option("header", header.Key + ": " + value);
        }
        var cookies = options.CookieContainer?.GetCookieHeader(url);
        var cookie = string.Join("; ", new[] { explicitCookie, cookies }.Where(value => !string.IsNullOrEmpty(value)));
        if (cookie.Length > 0) Option("header", "Cookie: " + cookie);
        if (!request.Headers.Contains("Accept-Encoding")) Option("header", "Accept-Encoding: identity");
        for (var i = 0; i < selection.Arguments.Length; i++)
        {
            var flag = selection.Arguments[i][2..];
            if (flag is "no-buffer" or "tcp-nodelay" or "no-keepalive" or "http1.1") config.Append(flag).Append('\n');
            else Option(flag, selection.Arguments[++i]);
        }
        var start = new ProcessStartInfo(NativeExternalDownloader.Resolve(selection))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var arg in new[] { "--disable", "--silent", "--globoff", "--no-buffer", "--suppress-connect-headers", "--include", "--http1.1", "--proto", "=http,https", "--config", "-" }) start.ArgumentList.Add(arg);
        // Prevent curl/OpenSSL from implicitly loading account, key-log or TLS override files.
        foreach (var name in new[] { "CURL_CA_BUNDLE", "SSL_CERT_FILE", "SSL_CERT_DIR", "SSLKEYLOGFILE", "CURL_SSL_BACKEND" }) start.Environment.Remove(name);
        var process = new Process { StartInfo = start };
        CurlBodyStream? body = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("Could not start curl.");
            body = new CurlBodyStream(process);
            using var cancel = ct.Register(body.Kill);
            await process.StandardInput.WriteAsync(config.ToString().AsMemory(), ct).ConfigureAwait(false);
            process.StandardInput.Close();
            HttpResponseMessage? response = null;
            try
            {
                for (var interim = 0; interim <= 16; interim++)
                {
                    var lines = await ReadHeaders(body, ct).ConfigureAwait(false);
                    var status = lines[0].Split(' ', 3);
                    if (status.Length < 2 || !status[0].StartsWith("HTTP/", StringComparison.Ordinal) || !int.TryParse(status[1], out var code) || code < 100 || code > 599)
                        throw new InvalidDataException("curl returned invalid HTTP response headers.");
                    if (code is >= 100 and < 200) { if (code == 101 || interim == 16) throw new InvalidDataException("The media server returned an unsupported informational response."); continue; }
                    response = new((HttpStatusCode)code) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, url), Content = new StreamContent(body) };
                    var lengths = 0; var chunked = false;
                    foreach (var line in lines.Skip(1))
                    {
                        var colon = line.IndexOf(':');
                        if (colon <= 0 || line[0] is ' ' or '\t') throw new InvalidDataException("curl returned invalid media headers.");
                        var name = line[..colon]; var value = line[(colon + 1)..].Trim();
                        if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && (++lengths > 1 || !long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length) || length < 0))
                            throw new InvalidDataException("The curl media length is ambiguous or invalid.");
                        if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) { if (chunked || !value.Equals("chunked", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The curl transfer encoding is unsupported."); chunked = true; }
                        if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)) { try { options.CookieContainer?.SetCookies(url, value); } catch (CookieException) { } }
                        if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!response.Headers.TryAddWithoutValidation(name, value)) response.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                    if (chunked && lengths > 0) throw new InvalidDataException("The curl media response has conflicting body lengths.");
                    if (response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                        throw new NotSupportedException("The curl media server ignored the required identity content encoding.");
                    body.HeadersAccepted = true;
                    return response;
                }
                throw new InvalidDataException("The media server did not return final response headers.");
            }
            catch { response?.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new IOException("The curl media transfer could not start or read its response.", ex is System.ComponentModel.Win32Exception ? null : ex);
        }
        finally
        {
            // A successful response owns the body and process. Failed parses/launches are reaped here.
            if (body is null) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } } catch (InvalidOperationException) { } process.Dispose(); }
            else if (!body.HeadersAccepted) body.Dispose();
        }
    }

    private static bool SafeRedirectHeader(string name) => name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) || name.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase) || name.Equals("Range", StringComparison.OrdinalIgnoreCase) || name.Equals("If-Range", StringComparison.OrdinalIgnoreCase);

    private static async Task<string[]> ReadHeaders(CurlBodyStream body, CancellationToken ct)
    {
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < 65536)
        {
            if (await body.ReadAsync(one, ct).ConfigureAwait(false) == 0) throw new EndOfStreamException("curl stopped before complete media headers arrived.");
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.Latin1.GetString(bytes.ToArray(), 0, bytes.Count - 4).Split("\r\n", StringSplitOptions.None);
        }
        throw new InvalidDataException("The curl media headers exceed their limit.");
    }

    private sealed class CurlBodyStream : Stream
    {
        private readonly Process _process; private readonly ProcessLifetime _lifetime; private readonly Stream _output; private readonly Task _error;
        private bool _disposed;
        // A successfully parsed final response transfers process ownership to StreamContent.
        internal bool HeadersAccepted { get; set; }
        internal CurlBodyStream(Process process) { _process = process; _lifetime = ProcessLifetime.Attach(process); _output = process.StandardOutput.BaseStream; _error = Drain(process.StandardError); }
        private static async Task Drain(StreamReader reader) { var buffer = new char[2048]; while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { } }
        internal void Kill() { try { if (!_process.HasExited) _process.Kill(true); } catch (InvalidOperationException) { } }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            using var cancel = ct.Register(Kill);
            var read = await _output.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read != 0 || buffer.Length == 0) return read;
            await _process.WaitForExitAsync(ct).ConfigureAwait(false); await _error.ConfigureAwait(false);
            if (_process.ExitCode != 0) throw new IOException("curl ended the media transfer with exit code " + _process.ExitCode + ".");
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; Kill(); _process.WaitForExit(); _error.GetAwaiter().GetResult(); _output.Dispose(); _lifetime.Dispose(); _process.Dispose(); }
            base.Dispose(disposing);
        }
        public override bool CanRead => !_disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
