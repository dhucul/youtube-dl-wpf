using System.Net;
using System.Text;
using YoutubeDl.Engine;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Http;

internal static partial class CookieImportChecks
{
    internal static IEnumerable<(string Name, Func<Task> Test)> All => Common.Concat(OperatingSystem.IsWindows() ? Windows.Concat(Chromium) : []);
    private static IEnumerable<(string Name, Func<Task> Test)> Common =>
    [
        ("Cookie-file import preserves exact host, domain, secure, path and HttpOnly scope", () => Sync(Scopes)),
        ("Cookie-file import discards expired entries and preserves session and empty values", () => Sync(Lifetimes)),
        ("Cookie imports retain more than the default twenty cookies per host", () => Sync(Capacity)),
        ("Malformed or missing cookie files fail without logging authentication values", InvalidFiles),
        ("Browser-cookie selections preserve Windows profile paths and explicit container choices", () => Sync(Selections)),
        ("Firefox profile selection respects installation defaults, names and explicit directories", () => Sync(Profiles)),
        ("Canceled cookie imports do not open their source files", CanceledFiles)
    ];
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static void Scopes()
    {
        using var files = new Files();
        File.WriteAllText(files.CookieFile, "# Netscape HTTP Cookie File\n" +
            "media.example\tFALSE\t/\tFALSE\t0\thost\thost-value\n" +
            ".media.example\tTRUE\t/private\tFALSE\t0\tdomain\tdomain-value\n" +
            "#HttpOnly_media.example\tFALSE\t/\tTRUE\t4102444800\tsecure\tsecure-value\n");
        var jar = NetscapeCookieFile.Load(files.CookieFile);
        var root = jar.GetCookies(new("https://media.example/private/video"));
        Assert(root.Count == 3 && root["secure"]!.HttpOnly && root["secure"]!.Secure, "Cookie scope or HttpOnly flags were lost.");
        Assert(jar.GetCookies(new("https://sub.media.example/private/video")).Count == 1 && jar.GetCookieHeader(new("https://sub.media.example/private/video")) == "domain=domain-value",
            "A host-only cookie leaked into a child domain.");
        Assert(jar.GetCookies(new("http://media.example/public")).Count == 1 && jar.GetCookies(new("https://other.example/")).Count == 0,
            "Secure/path/domain restrictions were broadened.");
    }
    private static void Lifetimes()
    {
        using var files = new Files();
        File.WriteAllText(files.CookieFile, "media.example\tFALSE\t/\tFALSE\t1\texpired\told\n" +
            "media.example\tFALSE\t/\tFALSE\t0\tsession\tsession-value\nmedia.example\tFALSE\t/\tFALSE\t4102444800\tempty\t\n");
        var cookies = NetscapeCookieFile.Load(files.CookieFile).GetCookies(new("https://media.example/"));
        Assert(cookies.Count == 2 && cookies["expired"] is null && cookies["session"]!.Expires == DateTime.MinValue && cookies["empty"]!.Value == "", "Expired credentials were revived or session/empty values changed.");
    }
    private static void Capacity()
    {
        using var files = new Files();
        File.WriteAllLines(files.CookieFile, Enumerable.Range(0, 400).Select(index => "media.example\tFALSE\t/\tFALSE\t0\tc" + index + "\tv" + index));
        var cookies = NetscapeCookieFile.Load(files.CookieFile).GetCookies(new("https://media.example/"));
        Assert(cookies.Count == 400 && cookies["c0"]!.Value == "v0" && cookies["c399"]!.Value == "v399", "The default CookieContainer limits silently evicted imported credentials.");
    }
    private static async Task InvalidFiles()
    {
        using var files = new Files();
        await Expect<FileNotFoundException>(() => Task.Run(() => NetscapeCookieFile.Load(files.CookieFile)));
        foreach (var line in new[] { "media.example\tFALSE\t/\tFALSE\t0\tsecret-auth-value", "media.example\tFALSE\t/\tFALSE\t9223372036854775807\tname\tsecret-auth-value", "https://media.example\tFALSE\t/\tFALSE\t0\tname\tsecret-auth-value" })
        {
            File.WriteAllText(files.CookieFile, line);
            try { NetscapeCookieFile.Load(files.CookieFile); throw new InvalidOperationException("A malformed cookie file was accepted."); }
            catch (InvalidDataException ex) { Assert(!ex.ToString().Contains("secret-auth-value", StringComparison.Ordinal), "Cookie values leaked into diagnostics."); }
        }
        await Expect<FileNotFoundException>(() => new NativeEngine().InspectAsync(new() { Url = new("http://127.0.0.1:9/watch"), OutputDirectory = files.Directory,
            CookieFile = files.CookieFile + ".missing", BrowserCookies = "chrome" }, default));
    }
    private static void Selections()
    {
        Assert(BrowserCookieSelection.Parse(" NONE ") is null, "Disabled browser cookies were not normalized.");
        var selected = BrowserCookieSelection.Parse("firefox:C:\\Profiles\\Selected User::Work")!;
        Assert(selected.Profile == "C:\\Profiles\\Selected User" && selected.Container == "Work", "Windows drive/path or container choice was split incorrectly.");
        Assert(BrowserCookieSelection.Parse("firefox::none") is { Profile: null, Container: "none" }, "An explicit default container required a profile path.");
        foreach (var browser in new[] { "safari", "firefox+keyring", "chrome::Work", "unknown" }) Throws<NotSupportedException>(() => BrowserCookieSelection.Parse(browser));
        foreach (var source in new[] { "firefox:", "firefox::", "firefox::1::2", "firefox:\0profile" }) Throws<ArgumentException>(() => BrowserCookieSelection.Parse(source));
    }
    private static void Profiles()
    {
        using var files = new Files();
        var first = Path.Combine(files.Directory, "Profiles", "first.default"); var second = Path.Combine(files.Directory, "Profiles", "second.custom");
        System.IO.Directory.CreateDirectory(first); System.IO.Directory.CreateDirectory(second);
        var firstDb = Path.Combine(first, "cookies.sqlite"); var secondDb = Path.Combine(second, "cookies.sqlite");
        File.WriteAllText(firstDb, "fixture"); File.WriteAllText(secondDb, "fixture");
        File.SetLastWriteTimeUtc(firstDb, DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(files.Directory, "profiles.ini"), "[Profile0]\nName=Personal\nIsRelative=1\nPath=Profiles/first.default\nDefault=1\n[Profile1]\nName=Work\nIsRelative=1\nPath=Profiles/second.custom\n");
        Assert(FirefoxProfiles.Resolve(null, files.Directory) == firstDb && FirefoxProfiles.Resolve("Work", files.Directory) == secondDb, "Profile defaults or explicit names were ignored.");
        File.WriteAllText(Path.Combine(files.Directory, "installs.ini"), "[Installation]\nDefault=Profiles/second.custom\nLocked=1\n");
        Assert(FirefoxProfiles.Resolve(null, files.Directory) == secondDb && FirefoxProfiles.Resolve(first, files.Directory) == firstDb && FirefoxProfiles.Resolve(firstDb, files.Directory) == firstDb,
            "Installation defaults or explicit paths were ignored.");
        File.AppendAllText(Path.Combine(files.Directory, "profiles.ini"), "[Profile2]\nName=Work\nIsRelative=1\nPath=Profiles/first.default\n");
        Throws<InvalidDataException>(() => FirefoxProfiles.Resolve("Work", files.Directory));
        Throws<FileNotFoundException>(() => FirefoxProfiles.Resolve("Missing", files.Directory));
    }
    private static async Task CanceledFiles()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Expect<OperationCanceledException>(() => Task.Run(() => NetscapeCookieFile.Load("missing-file", stop.Token)));
        await Expect<OperationCanceledException>(() => Task.Run(() => BrowserCookieReader.Read("firefox:missing-profile", stop.Token)));
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task Expect<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Files : IDisposable
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
        internal string Directory { get; } = Path.Combine(Root, "cookies-" + Guid.NewGuid().ToString("N"));
        internal string CookieFile => Path.Combine(Directory, "cookies.txt");
        internal string Database => Path.Combine(Directory, "cookies.sqlite");
        internal string Selection => "firefox:" + Directory;
        internal Files() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() { var path = Path.GetFullPath(Directory); if (!path.StartsWith(Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped its root."); System.IO.Directory.Delete(path, true); }
    }
}
