using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;

internal static partial class CookieImportChecks
{
    private static IEnumerable<(string Name, Func<Task> Test)> Chromium =>
    [
        ("Chromium profiles honor last-used identity, display names, direct paths and browser roots", () => Sync(ChromiumProfileSelection)),
        ("Chromium plaintext and encrypted imports preserve scopes and microsecond expiry", () => Sync(ChromiumValues)),
        ("Chromium cookie import decrypts legacy current-user DPAPI records", () => Sync(ChromiumLegacy)),
        ("Chromium host-bound cookies reject swapped hosts and corrupted authentication tags", () => Sync(ChromiumIntegrity)),
        ("Chromium application-bound and unknown encryption fail without exposing a partial jar", () => Sync(ChromiumUnsupported)),
        ("Chromium cookie import reads encrypted WAL updates without writing database bytes", () => Sync(ChromiumWal)),
        ("Chromium source-bound duplicates and unknown schemas fail explicitly", () => Sync(ChromiumSchema)),
        ("Native Chromium credentials reach HTTP transfers and explicit cookie files take priority", ChromiumHttp)
    ];
    private static void ChromiumProfileSelection()
    {
        using var files = new Files(); var root = Path.Combine(files.Directory, "User Data");
        var first = Path.Combine(root, "Default"); var second = Path.Combine(root, "Profile 1");
        Directory.CreateDirectory(Path.Combine(first, "Network")); Directory.CreateDirectory(Path.Combine(second, "Network"));
        File.WriteAllText(Path.Combine(first, "Cookies"), "legacy"); File.WriteAllText(Path.Combine(first, "Network", "Cookies"), "current"); File.WriteAllText(Path.Combine(second, "Network", "Cookies"), "current");
        File.WriteAllText(Path.Combine(root, "Local State"), "{\"profile\":{\"last_used\":\"Profile 1\",\"info_cache\":{\"Default\":{\"name\":\"Personal\"},\"Profile 1\":{\"name\":\"Work\"}}}}");
        foreach (var browser in new[] { "chrome", "edge", "brave", "chromium", "opera", "vivaldi" })
        {
            Assert(BrowserCookieSelection.Parse(browser)!.Browser == browser && ChromiumProfiles.Resolve(browser, null, root).CookiePath == Path.Combine(second, "Network", "Cookies"), "The selected browser or last-used profile changed.");
            Assert(ChromiumProfiles.Resolve(browser, "Personal", root).CookiePath == Path.Combine(first, "Network", "Cookies") && ChromiumProfiles.Resolve(browser, "Default", root).LocalStatePath == Path.Combine(root, "Local State"), "Profile display/folder names or key location were lost.");
        }
        Assert(ChromiumProfiles.Resolve("chrome", Path.Combine(first, "Cookies"), root).CookiePath == Path.Combine(first, "Cookies"), "An explicit legacy database path was replaced by another database.");
        Assert(ChromiumProfiles.Resolve("chrome", root, root).CookiePath == Path.Combine(second, "Network", "Cookies"), "An explicit user-data root did not resolve its default profile.");
        File.WriteAllText(Path.Combine(root, "Local State"), "{\"profile\":{\"last_used\":\"../outside\"}}");
        Throws<InvalidDataException>(() => ChromiumProfiles.Resolve("chrome", null, root));
        File.WriteAllText(Path.Combine(root, "Local State"), "{\"profile\":{\"last_used\":7}}");
        Throws<InvalidDataException>(() => ChromiumProfiles.Resolve("chrome", null, root));
    }
    private static void ChromiumValues()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory);
        var expiry = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1234500);
        using (var db = new FixtureDatabase(profile.Database, chromium: true))
        {
            db.Chrome("media.example", "plain", "plain-value");
            db.Chrome("media.example", "empty");
            db.Chrome("media.example", "encrypted", encrypted: profile.Encrypt("media.example", "encrypted-value"), secure: 1, httpOnly: 1, expires: WindowsMicros(expiry));
            db.Chrome(".media.example", "domain", encrypted: profile.Encrypt(".media.example", "domain-value"), path: "/private");
            db.Chrome("media.example", "expired", encrypted: "v20expired"u8.ToArray(), expires: 1);
            db.Chrome("media.example", "isolated", encrypted: "v20partitioned"u8.ToArray(), partition: "https://top.example");
        }
        var result = BrowserCookieReader.Read(profile.Selection); var cookies = result.Cookies.GetCookies(new("https://media.example/private"));
        Assert(result.ImportedCount == 4 && result.ExpiredCount == 1 && cookies["encrypted"]!.Value == "encrypted-value" && cookies["encrypted"]!.HttpOnly && cookies["encrypted"]!.Expires.ToUniversalTime() == expiry.UtcDateTime,
            "Chromium values, host binding, flags or timestamp precision changed.");
        Assert(result.Cookies.GetCookieHeader(new("https://sub.media.example/private")) == "domain=domain-value" && result.Cookies.GetCookies(new("http://media.example/")).Count == 2 && cookies["plain"]!.Expires == DateTime.MinValue,
            "Host-only, secure, path or session scope was broadened.");
        Assert(result.Warnings.Count == 1 && result.Warnings[0].Contains("partitioned") && !string.Join(" ", result.Warnings).Contains("encrypted-value"), "Partitioned credentials or values were exposed.");
    }
    private static void ChromiumLegacy()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory, writeKey: false);
        using (var db = new FixtureDatabase(profile.Database, chromium: true, version: 23))
            db.Chrome("media.example", "legacy", encrypted: Protect("legacy-value"u8.ToArray()));
        Assert(BrowserCookieReader.Read(profile.Selection).Cookies.GetCookieHeader(new("https://media.example/")) == "legacy=legacy-value", "An older DPAPI cookie incorrectly required an AES key or host digest.");
    }
    private static void ChromiumIntegrity()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory);
        using var decoder = new ChromiumCookieCryptography(profile.LocalState);
        var data = profile.Encrypt("media.example", "must-not-log-this-value");
        Throws<CryptographicException>(() => decoder.Decrypt(data, "other.example", 24));
        data[^1] ^= 1;
        try { decoder.Decrypt(data, "media.example", 24); throw new InvalidOperationException("A corrupted cookie was authenticated."); }
        catch (CryptographicException ex) { Assert(!ex.ToString().Contains("must-not-log-this-value"), "A failed decryption exposed plaintext."); }
        using (var db = new FixtureDatabase(profile.Database, chromium: true)) { db.Chrome("media.example", "clear", "ok"); db.Chrome("media.example", "broken", encrypted: data); }
        Throws<CryptographicException>(() => BrowserCookieReader.Read(profile.Selection));
    }
    private static void ChromiumUnsupported()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory, writeKey: false);
        using (var db = new FixtureDatabase(profile.Database, chromium: true)) { db.Chrome("media.example", "clear", "ok"); db.Chrome("media.example", "bound", encrypted: "v20application-bound"u8.ToArray()); }
        Throws<NotSupportedException>(() => BrowserCookieReader.Read(profile.Selection));
        using var decoder = new ChromiumCookieCryptography(profile.LocalState);
        Throws<NotSupportedException>(() => decoder.Decrypt("v11unknown"u8.ToArray(), "media.example", 24));
        Throws<CryptographicException>(() => decoder.Decrypt("v10short"u8.ToArray(), "media.example", 24));
    }
    private static void ChromiumWal()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory); using var db = new FixtureDatabase(profile.Database, chromium: true);
        db.Chrome("media.example", "auth", encrypted: profile.Encrypt("media.example", "old"));
        db.Exec("PRAGMA journal_mode=WAL"); db.Exec("PRAGMA wal_autocheckpoint=0"); db.Exec("PRAGMA wal_checkpoint(TRUNCATE)");
        db.Exec("UPDATE cookies SET encrypted_value=X'" + Convert.ToHexString(profile.Encrypt("media.example", "current")) + "'");
        var before = SharedBytes(profile.Database); var wal = SharedBytes(profile.Database + "-wal");
        Assert(BrowserCookieReader.Read(profile.Selection).Cookies.GetCookieHeader(new("https://media.example/")) == "auth=current", "The encrypted WAL update was lost.");
        Assert(before.AsSpan().SequenceEqual(SharedBytes(profile.Database)) && wal.AsSpan().SequenceEqual(SharedBytes(profile.Database + "-wal")), "Chromium import changed database/WAL bytes.");
    }
    private static void ChromiumSchema()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory);
        using (var db = new FixtureDatabase(profile.Database, chromium: true)) { db.Chrome("media.example", "same", "first"); db.Chrome("media.example", "same", "second", secure: 1); }
        Throws<NotSupportedException>(() => BrowserCookieReader.Read(profile.Selection));
        File.Delete(profile.Database);
        using (var db = new FixtureDatabase(profile.Database, chromium: true, version: 25)) db.Chrome("media.example", "new", "value");
        Throws<NotSupportedException>(() => BrowserCookieReader.Read(profile.Selection));
    }
    private static async Task ChromiumHttp()
    {
        using var files = new Files(); using var profile = new ChromeProfile(files.Directory);
        using (var db = new FixtureDatabase(profile.Database, chromium: true)) db.Chrome("127.0.0.1", "browser_auth", encrypted: profile.Encrypt("127.0.0.1", "fixture-auth"));
        File.WriteAllText(files.CookieFile, "127.0.0.1\tFALSE\t/\tFALSE\t0\tbrowser_auth\tfile-auth\n");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var url = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/secure.mp4");
        var server = Serve(); var engine = new NativeEngine();
        for (var index = 0; index < 2; index++)
        {
            var result = await engine.DownloadAsync(new() { Url = url, OutputDirectory = files.Directory, FileName = "chromium-" + index,
                BrowserCookies = index == 0 ? profile.Selection : "unsupported-browser", CookieFile = index == 0 ? null : files.CookieFile, Retries = 0 }, null, null, timeout.Token);
            Assert(result.Succeeded && File.ReadAllText(result.OutputPath!) == "media bytes", "Native cookie-backed media download failed.");
        }
        await server;
        async Task Serve()
        {
            for (var index = 0; index < 2; index++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token); await using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var headers = new StringBuilder(); while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) headers.AppendLine(line);
                var expected = "browser_auth=" + (index == 0 ? "fixture-auth" : "file-auth");
                Assert(headers.ToString().Contains(expected), "The selected browser or file credential did not reach the media request.");
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: 11\r\nConnection: close\r\n\r\nmedia bytes"u8.ToArray(), timeout.Token);
            }
        }
    }
    private static long WindowsMicros(DateTimeOffset value) => (value.UtcTicks - new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks) / 10;
    private sealed class ChromeProfile : IDisposable
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        internal string Folder { get; }
        internal string LocalState { get; }
        internal string Database { get; }
        internal string Selection => "chrome:" + Folder;
        internal ChromeProfile(string directory, bool writeKey = true)
        {
            var root = Path.Combine(directory, "Chromium User Data"); Folder = Path.Combine(root, "Default"); Directory.CreateDirectory(Path.Combine(Folder, "Network"));
            LocalState = Path.Combine(root, "Local State"); Database = Path.Combine(Folder, "Network", "Cookies");
            if (writeKey) File.WriteAllText(LocalState, JsonSerializer.Serialize(new { os_crypt = new { encrypted_key = Convert.ToBase64String("DPAPI"u8.ToArray().Concat(Protect(_key)).ToArray()) }, profile = new { last_used = "Default" } }));
        }
        internal byte[] Encrypt(string host, string value, bool bindHost = true)
        {
            var plain = (bindHost ? SHA256.HashData(Encoding.UTF8.GetBytes(host)) : []).Concat(Encoding.UTF8.GetBytes(value)).ToArray();
            var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[plain.Length]; var tag = new byte[16];
            try { using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, plain, cipher, tag); return "v10"u8.ToArray().Concat(nonce).Concat(cipher).Concat(tag).ToArray(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        public void Dispose() => CryptographicOperations.ZeroMemory(_key);
    }
    private static byte[] Protect(byte[] input)
    {
        var pinned = GCHandle.Alloc(input, GCHandleType.Pinned); WindowsCookieProtection.Blob output = default;
        try
        {
            var data = new WindowsCookieProtection.Blob { Length = (uint)input.Length, Data = pinned.AddrOfPinnedObject() };
            if (!ProtectData(ref data, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)) throw new InvalidOperationException("Fixture DPAPI protection failed: " + Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { pinned.Free(); if (output.Data != IntPtr.Zero) FreeProtected(output.Data); }
    }
    [DllImport("crypt32.dll", EntryPoint = "CryptProtectData", SetLastError = true, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProtectData(ref WindowsCookieProtection.Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out WindowsCookieProtection.Blob output);
    [DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FreeProtected(IntPtr pointer);
}
