using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

internal static partial class CookieImportChecks
{
    private static IEnumerable<(string Name, Func<Task> Test)> Windows =>
    [
        ("Firefox cookie database import preserves live values, flags and host isolation", () => Sync(FirefoxRows)),
        ("Firefox cookie import reads committed WAL changes without changing database bytes", () => Sync(FirefoxWal)),
        ("Firefox cookie containers remain separate and partitioned credentials are never broadened", () => Sync(FirefoxContainers)),
        ("Invalid Firefox databases fail without heuristic credential scanning", () => Sync(FirefoxInvalid)),
        ("Locked Firefox cookie databases fail promptly and remain unchanged", () => Sync(FirefoxLocked)),
        ("SQLite cookie reads honor cancellation and release their read transaction", () => Sync(FirefoxCancellation)),
        ("Oversized Firefox cookie sets fail without returning a truncated jar", () => Sync(FirefoxLimit)),
        ("Native HTTP discovery and transfer use selected Firefox cookies and report import limitations", FirefoxHttp)
    ];
    private static void FirefoxRows()
    {
        using var files = new Files();
        using (var database = new FixtureDatabase(files.Database))
        {
            database.Insert("media.example", "host", "host-value");
            database.Insert(".media.example", "domain", "domain-value", path: "/private");
            database.Insert("media.example", "secure", "secure-value", secure: 1, httpOnly: 1);
            database.Insert("media.example", "expired", "expired-value", expiry: 1);
            database.Insert("media.example", "empty", "");
            database.Insert("media.example", "bad", "secret\0value");
            database.Exec("BEGIN");
            for (var index = 0; index < 400; index++) database.Insert("bulk.example", "n" + index, "v" + index);
            database.Exec("COMMIT");
        }
        var before = SHA256.HashData(File.ReadAllBytes(files.Database));
        var result = BrowserCookieReader.Read(files.Selection);
        var root = result.Cookies.GetCookies(new("https://media.example/private/watch"));
        Assert(result.ImportedCount == 404 && result.ExpiredCount == 1 && root.Count == 4 && root["secure"]!.Secure && root["secure"]!.HttpOnly && root["empty"]!.Value == "", "Firefox cookie data or flags changed.");
        Assert(result.Cookies.GetCookieHeader(new("https://sub.media.example/private/watch")) == "domain=domain-value" && result.Cookies.GetCookies(new("http://media.example/")).Count == 2, "Firefox cookie scope was broadened.");
        Assert(result.Cookies.GetCookies(new("https://bulk.example/")).Count == 400 && result.Warnings.Count == 1 && !string.Join(" ", result.Warnings).Contains("secret"), "Credentials were evicted or leaked into diagnostics.");
        Assert(before.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(files.Database))), "Cookie import changed the source database.");
    }
    private static void FirefoxWal()
    {
        using var files = new Files(); using var database = new FixtureDatabase(files.Database);
        database.Insert("media.example", "session", "old-value");
        database.Exec("PRAGMA journal_mode=WAL"); database.Exec("PRAGMA wal_autocheckpoint=0"); database.Exec("PRAGMA wal_checkpoint(TRUNCATE)");
        database.Exec("UPDATE moz_cookies SET value='committed-wal-value'");
        var before = SharedBytes(files.Database); var wal = SharedBytes(files.Database + "-wal");
        Assert(wal.Length > 0, "The WAL fixture did not retain an uncheckpointed update.");
        var result = BrowserCookieReader.Read(files.Selection);
        Assert(result.Cookies.GetCookies(new("https://media.example/"))["session"]!.Value == "committed-wal-value", "A stale base-database copy lost the WAL update.");
        Assert(before.AsSpan().SequenceEqual(SharedBytes(files.Database)) && wal.AsSpan().SequenceEqual(SharedBytes(files.Database + "-wal")), "Cookie import modified persistent database or WAL bytes.");
    }
    private static byte[] SharedBytes(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    private static void FirefoxContainers()
    {
        using var files = new Files();
        using (var database = new FixtureDatabase(files.Database))
        {
            database.Insert("media.example", "account", "default-account");
            database.Insert("media.example", "account", "work-account", attributes: "^userContextId=2");
            database.Insert("media.example", "partition", "partition-secret", attributes: "^partitionKey=%28https%2Ctop.example%29");
            database.Insert("media.example", "private", "private-secret", attributes: "^privateBrowsingId=1");
            database.Insert("media.example", "isolated", "first-party-secret", attributes: "^firstPartyDomain=top.example");
            database.Insert("media.example", "chips", "chips-secret", partitioned: 1);
        }
        File.WriteAllText(Path.Combine(files.Directory, "containers.json"), "{\"identities\":[{\"userContextId\":2,\"name\":\"Work\",\"public\":true}]}");
        var normal = BrowserCookieReader.Read(files.Selection); var work = BrowserCookieReader.Read(files.Selection + "::Work");
        Assert(normal.ImportedCount == 1 && normal.Cookies.GetCookieHeader(new("https://media.example/")) == "account=default-account" && normal.Warnings.Single().Contains("4 partitioned"), "Firefox default/container/partition identities were mixed.");
        Assert(work.ImportedCount == 1 && work.Cookies.GetCookieHeader(new("https://media.example/")) == "account=work-account" && work.Warnings.Count == 0, "The selected container did not retain its own account.");
        Assert(BrowserCookieReader.Read(files.Selection + "::2").Cookies.GetCookieHeader(new("https://media.example/")) == "account=work-account", "Numeric container selection changed identity.");
        Throws<NotSupportedException>(() => BrowserCookieReader.Read(files.Selection + "::all"));
        Throws<InvalidDataException>(() => BrowserCookieReader.Read(files.Selection + "::missing"));
    }
    private static void FirefoxInvalid()
    {
        using var files = new Files();
        File.WriteAllText(files.Database, new string('x', 200) + "moz_cookies\0media.example\0session\0secret-value");
        try { BrowserCookieReader.Read(files.Selection); throw new InvalidOperationException("A corrupt database was scanned heuristically."); }
        catch (InvalidDataException ex) { Assert(!ex.ToString().Contains("secret-value"), "Corrupt cookie bytes appeared in diagnostics."); }
        File.Delete(files.Database);
        using (var database = new FixtureDatabase(files.Database)) database.Exec("DROP TABLE moz_cookies");
        Throws<InvalidDataException>(() => BrowserCookieReader.Read(files.Selection));
        File.Delete(files.Database);
        using (var database = new FixtureDatabase(files.Database)) { database.Exec("DROP TABLE moz_cookies"); database.Exec("CREATE TABLE moz_cookies (name TEXT)"); }
        Throws<NotSupportedException>(() => BrowserCookieReader.Read(files.Selection));
    }
    private static void FirefoxLocked()
    {
        using var files = new Files(); using var database = new FixtureDatabase(files.Database);
        database.Insert("media.example", "session", "locked-value"); database.Exec("BEGIN EXCLUSIVE");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Throws<IOException>(() => BrowserCookieReader.Read(files.Selection));
        Assert(timer.Elapsed < TimeSpan.FromSeconds(3), "A locked browser database waited indefinitely.");
        database.Exec("ROLLBACK");
        Assert(BrowserCookieReader.Read(files.Selection).ImportedCount == 1, "A failed read damaged the cookie database.");
    }
    private static void FirefoxCancellation()
    {
        using var files = new Files(); using var source = new FixtureDatabase(files.Database);
        source.Insert("media.example", "session", "value"); using var stop = new CancellationTokenSource();
        using (var database = new ReadOnlyCookieDatabase(files.Database, stop.Token))
            Throws<OperationCanceledException>(() => database.Read("SELECT name FROM moz_cookies", _ => stop.Cancel(), 100));
        source.Exec("BEGIN EXCLUSIVE"); source.Exec("ROLLBACK");
    }
    private static void FirefoxLimit()
    {
        using var files = new Files();
        using (var database = new FixtureDatabase(files.Database))
            database.Exec("WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n<50001) INSERT INTO moz_cookies(host,name,value,path,expiry,isSecure,isHttpOnly,originAttributes) SELECT 'media.example','cookie'||n,'value','/',4102444800,0,0,'' FROM numbers");
        Throws<InvalidDataException>(() => BrowserCookieReader.Read(files.Selection));
    }
    private static async Task FirefoxHttp()
    {
        using var files = new Files();
        using (var database = new FixtureDatabase(files.Database))
        {
            database.Insert("127.0.0.1", "auth", "fixture-auth");
            database.Insert("127.0.0.1", "isolated", "must-not-send", attributes: "^partitionKey=top.example");
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/watch");
        var headers = new List<string>();
        var server = Serve();
        var engine = new NativeEngine(); var request = new NativeDownloadRequest { Url = address, OutputDirectory = files.Directory, FileName = "result", BrowserCookies = files.Selection, Retries = 0 };
        var info = await engine.InspectAsync(request, timeout.Token);
        var logs = new List<string>(); var result = await engine.DownloadAsync(request, null, logs.Add, timeout.Token);
        await server;
        Assert(info.Warnings.Any(warning => warning.Contains("partitioned")) && logs.Any(line => line.Contains("partitioned")) && result.Succeeded && File.ReadAllText(result.OutputPath!) == "media bytes", "Cookie imports did not reach the native workflow or its diagnostics.");
        Assert(headers.Count == 3 && headers.All(header => header.Contains("auth=fixture-auth") && !header.Contains("must-not-send")) && headers[2].Contains("rotated=latest"), "Discovery and transfer did not share correctly scoped imported/updated cookies.");
        async Task Serve()
        {
            for (var index = 0; index < 3; index++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token); await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var header = new StringBuilder();
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) header.AppendLine(line);
                headers.Add(header.ToString());
                var media = header.ToString().StartsWith("GET /video.mp4 ", StringComparison.Ordinal);
                var body = media ? "media bytes" : "<html><title>Cookie video</title><video src=\"/video.mp4\"></video></html>";
                var response = "HTTP/1.1 200 OK\r\nContent-Type: " + (media ? "video/mp4" : "text/html") + "\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n" +
                    (media ? "" : "Set-Cookie: rotated=latest; Path=/; HttpOnly\r\n") + "\r\n" + body;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), timeout.Token);
            }
        }
    }
    private sealed class FixtureDatabase : IDisposable
    {
        private IntPtr _database;
        internal FixtureDatabase(string path, bool chromium = false, int version = 24)
        {
            Check(CookieSqlite.Open(path, out _database, 0x2 | 0x4 | 0x10000, IntPtr.Zero));
            if (chromium)
            {
                Exec("CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT)"); Exec("INSERT INTO meta VALUES('version','" + version + "')");
                Exec("CREATE TABLE cookies (host_key TEXT,name TEXT,value TEXT,encrypted_value BLOB,path TEXT,expires_utc INTEGER,is_secure INTEGER,is_httponly INTEGER,has_expires INTEGER,is_persistent INTEGER,top_frame_site_key TEXT,last_access_utc INTEGER DEFAULT 0)");
            }
            else Exec("CREATE TABLE moz_cookies (id INTEGER PRIMARY KEY,host TEXT,name TEXT,value TEXT,path TEXT,expiry INTEGER,isSecure INTEGER,isHttpOnly INTEGER,originAttributes TEXT,lastAccessed INTEGER DEFAULT 0,isPartitionedAttributeSet INTEGER DEFAULT 0)");
        }
        internal void Chrome(string host, string name, string value = "", byte[]? encrypted = null, string path = "/", long expires = 0, int secure = 0, int httpOnly = 0, string partition = "") =>
            Exec("INSERT INTO cookies(host_key,name,value,encrypted_value,path,expires_utc,is_secure,is_httponly,has_expires,is_persistent,top_frame_site_key) VALUES(" +
                string.Join(',', Text(host), Text(name), Text(value), "X'" + Convert.ToHexString(encrypted ?? []) + "'", Text(path), expires.ToString(System.Globalization.CultureInfo.InvariantCulture), secure.ToString(), httpOnly.ToString(), expires == 0 ? "0" : "1", expires == 0 ? "0" : "1", Text(partition)) + ")");
        internal void Insert(string host, string name, string value, string path = "/", long expiry = 4102444800, int secure = 0, int httpOnly = 0, string attributes = "", int partitioned = 0) =>
            Exec("INSERT INTO moz_cookies(host,name,value,path,expiry,isSecure,isHttpOnly,originAttributes,isPartitionedAttributeSet) VALUES(" + string.Join(',', Text(host), Text(name), Text(value), Text(path), expiry.ToString(System.Globalization.CultureInfo.InvariantCulture), secure.ToString(), httpOnly.ToString(), Text(attributes), partitioned.ToString()) + ")");
        private static string Text(string value) => "CAST(X'" + Convert.ToHexString(Encoding.UTF8.GetBytes(value)) + "' AS TEXT)";
        internal void Exec(string sql)
        {
            IntPtr statement = IntPtr.Zero;
            try
            {
                Check(CookieSqlite.Prepare(_database, sql, -1, out statement, IntPtr.Zero));
                while (true) { var status = CookieSqlite.Step(statement); if (status == 101) break; if (status != 100) Check(status); }
            }
            finally { if (statement != IntPtr.Zero) CookieSqlite.Finalize(statement); }
        }
        private static void Check(int code) { if (code != 0) throw new InvalidOperationException("SQLite fixture error: " + code); }
        public void Dispose() { if (_database != IntPtr.Zero) { CookieSqlite.Close(_database); _database = IntPtr.Zero; } }
    }
}
