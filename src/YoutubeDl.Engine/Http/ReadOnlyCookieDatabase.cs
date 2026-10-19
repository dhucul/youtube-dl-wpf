using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace YoutubeDl.Engine.Http;

/// <summary>Reads a consistent SQLite snapshot using the SQLite library serviced with Windows.</summary>
internal sealed class ReadOnlyCookieDatabase : IDisposable
{
    private IntPtr _database;
    private readonly CancellationToken _cancellation;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly CookieSqlite.Progress _progress;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal ReadOnlyCookieDatabase(string path, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native browser database import currently requires Windows.");
        ct.ThrowIfCancellationRequested(); _cancellation = ct;
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The selected browser profile has no cookies database.", path);
        if (file.Length is < 100 or > 256L * 1024 * 1024 || new FileInfo(path + "-wal") is { Exists: true, Length: > 256L * 1024 * 1024 })
            throw new InvalidDataException("The browser cookie database is empty or exceeds its size limit.");
        _progress = _ => ct.IsCancellationRequested || _elapsed.Elapsed > TimeSpan.FromSeconds(10) ? 1 : 0;
        try
        {
            Check(CookieSqlite.Open(Path.GetFullPath(path), out _database, 0x1 | 0x10000 | 0x40000, IntPtr.Zero)); // readonly, serialized, private cache
            if (CookieSqlite.ReadOnly(_database, "main") != 1) throw new InvalidDataException("The browser database was not opened read-only.");
            Check(CookieSqlite.BusyTimeout(_database, 500));
            CookieSqlite.Limit(_database, 0, 2 * 1024 * 1024); // SQLITE_LIMIT_LENGTH
            CookieSqlite.Limit(_database, 1, 65536); // SQLITE_LIMIT_SQL_LENGTH
            CookieSqlite.ProgressHandler(_database, 1000, _progress, IntPtr.Zero);
            Read("PRAGMA query_only=ON", _ => { }, 0);
            Read("PRAGMA trusted_schema=OFF", _ => { }, 0);
            Read("BEGIN", _ => { }, 0);
        }
        catch { Dispose(); throw; }
    }
    internal void Read(string sql, Action<Row> consume, int maximumRows)
    {
        if (_database == IntPtr.Zero) throw new ObjectDisposedException(nameof(ReadOnlyCookieDatabase));
        _cancellation.ThrowIfCancellationRequested();
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(CookieSqlite.Prepare(_database, sql, -1, out statement, IntPtr.Zero));
            var row = new Row(statement); var rows = 0;
            while (true)
            {
                _cancellation.ThrowIfCancellationRequested();
                var result = CookieSqlite.Step(statement);
                if (result == 101) break;
                if (result != 100) { Check(result); throw new InvalidDataException("The browser database returned an unexpected query result."); }
                if (++rows > maximumRows) throw new InvalidDataException("The browser cookie database exceeds its row-count limit.");
                consume(row);
            }
        }
        finally { if (statement != IntPtr.Zero) CookieSqlite.Finalize(statement); }
    }
    private void Check(int result)
    {
        if (result == 0) return;
        _cancellation.ThrowIfCancellationRequested();
        if (_elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Reading browser cookies exceeded the time limit.");
        if ((result & 255) is 5 or 6) throw new IOException("The browser cookie database is busy. Close that browser and try again.");
        // Database diagnostics can contain user-controlled content. Do not include them in authentication logs.
        throw new InvalidDataException("The browser cookie database could not be read (SQLite code " + (result & 255) + ").");
    }
    public void Dispose()
    {
        if (_database != IntPtr.Zero) { CookieSqlite.Close(_database); _database = IntPtr.Zero; }
        GC.KeepAlive(_progress);
    }
    internal sealed class Row(IntPtr statement)
    {
        internal string Text(int column, int maximumBytes = 16384)
        {
            if (CookieSqlite.ColumnType(statement, column) != 3) throw new InvalidDataException("A browser cookie field is not text.");
            var pointer = CookieSqlite.ColumnText(statement, column);
            var count = CookieSqlite.ColumnBytes(statement, column);
            if (count < 0 || count > maximumBytes || pointer == IntPtr.Zero) throw new InvalidDataException("A browser cookie field exceeds its size limit.");
            var bytes = new byte[count];
            try
            {
                Marshal.Copy(pointer, bytes, 0, count);
                try { return StrictUtf8.GetString(bytes); }
                catch (DecoderFallbackException) { throw new InvalidDataException("A browser cookie field is not valid UTF-8."); }
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        internal long Integer(int column)
        {
            if (CookieSqlite.ColumnType(statement, column) != 1) throw new InvalidDataException("A browser cookie field is not an integer.");
            return CookieSqlite.ColumnInt64(statement, column);
        }
        internal byte[] Blob(int column, int maximumBytes = 32768)
        {
            var type = CookieSqlite.ColumnType(statement, column);
            if (type == 3 && CookieSqlite.ColumnBytes(statement, column) == 0) return [];
            if (type != 4) throw new InvalidDataException("A browser cookie field is not binary data.");
            var pointer = CookieSqlite.ColumnBlob(statement, column); var count = CookieSqlite.ColumnBytes(statement, column);
            if (count < 0 || count > maximumBytes || count > 0 && pointer == IntPtr.Zero) throw new InvalidDataException("A browser cookie field exceeds its size limit.");
            var result = new byte[count]; if (count > 0) Marshal.Copy(pointer, result, 0, count); return result;
        }
    }
}

internal static class CookieSqlite
{
    private const string Library = "winsqlite3.dll";
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate int Progress(IntPtr state);
    [DllImport(Library, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
    [DllImport(Library, EntryPoint = "sqlite3_close_v2", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Close(IntPtr database);
    [DllImport(Library, EntryPoint = "sqlite3_db_readonly", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ReadOnly(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema);
    [DllImport(Library, EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int BusyTimeout(IntPtr database, int milliseconds);
    [DllImport(Library, EntryPoint = "sqlite3_limit", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Limit(IntPtr database, int category, int limit);
    [DllImport(Library, EntryPoint = "sqlite3_progress_handler", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void ProgressHandler(IntPtr database, int instructions, Progress callback, IntPtr state);
    [DllImport(Library, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Prepare(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport(Library, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Step(IntPtr statement);
    [DllImport(Library, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int Finalize(IntPtr statement);
    [DllImport(Library, EntryPoint = "sqlite3_column_type", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ColumnType(IntPtr statement, int column);
    [DllImport(Library, EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr ColumnText(IntPtr statement, int column);
    [DllImport(Library, EntryPoint = "sqlite3_column_blob", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr ColumnBlob(IntPtr statement, int column);
    [DllImport(Library, EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ColumnBytes(IntPtr statement, int column);
    [DllImport(Library, EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Winapi, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern long ColumnInt64(IntPtr statement, int column);
}
