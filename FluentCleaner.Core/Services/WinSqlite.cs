using System.Runtime.InteropServices;

namespace FluentCleaner.Services;

public sealed class VacuumResult
{
    public bool Success { get; }
    public string Path { get; }
    public long InitialBytes { get; }
    public long FinalBytes { get; }
    public long ReclaimedBytes { get; }
    public string? Error { get; }

    public VacuumResult(bool success, string path, long initialBytes, long finalBytes, long reclaimedBytes, string? error = null)
    {
        Success = success;
        Path = path ?? string.Empty;
        InitialBytes = initialBytes;
        FinalBytes = finalBytes;
        ReclaimedBytes = reclaimedBytes;
        Error = error;
    }

    public override string ToString() =>
        Success
            ? $"{System.IO.Path.GetFileName(Path)}: Reclaimed {ReclaimedBytes:N0} bytes ({InitialBytes:N0} -> {FinalBytes:N0})"
            : $"{System.IO.Path.GetFileName(Path)}: Failed ({Error})";
}

// Wrapper around native SQLite (winsqlite3.dll) bundled with Windows 10/11.
// Provides 64-bit safe database querying and high-performance in-place compaction (VACUUM).
public sealed class WinSqlite : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;

    private IntPtr _database;

    public WinSqlite(string path)
    {
        var result = Native.sqlite3_open16(path, out _database);
        if (result != SqliteOk)
        {
            var message = GetError();
            Dispose();
            throw new InvalidOperationException(message);
        }

        Native.sqlite3_busy_timeout(_database, 250);
    }

    public IReadOnlyList<string> QueryStrings(string sql)
    {
        var values = new List<string>();
        var statement = Prepare(sql);
        try
        {
            int result;
            while ((result = Native.sqlite3_step(statement)) == SqliteRow)
            {
                var value = Native.sqlite3_column_text16(statement, 0);
                if (value != IntPtr.Zero)
                    values.Add(Marshal.PtrToStringUni(value) ?? "");
            }

            if (result != SqliteDone)
                throw new InvalidOperationException(GetError());
        }
        finally { Native.sqlite3_finalize(statement); }

        return values;
    }

    public int Execute(string sql)
    {
        var statement = Prepare(sql);
        try
        {
            int result;
            do { result = Native.sqlite3_step(statement); }
            while (result == SqliteRow);

            if (result != SqliteDone)
                throw new InvalidOperationException(GetError());

            return Native.sqlite3_changes(_database);
        }
        finally { Native.sqlite3_finalize(statement); }
    }

    // Executes PRAGMA wal_checkpoint(TRUNCATE) and VACUUM to defragment pages and shrink file size.
    public static VacuumResult VacuumDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new VacuumResult(false, path ?? string.Empty, 0, 0, 0, "File does not exist.");

        long initialSize;
        try { initialSize = new FileInfo(path).Length; }
        catch (Exception ex) { return new VacuumResult(false, path, 0, 0, 0, ex.Message); }

        try
        {
            using var db = new WinSqlite(path);
            try
            {
                // Truncate the write-ahead log if WAL mode is active
                db.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch
            {
                // WAL checkpoint is safe to ignore if the database uses DELETE or MEMORY journal mode
            }

            // Compact and defragment free pages
            db.Execute("VACUUM;");
        }
        catch (Exception ex)
        {
            return new VacuumResult(false, path, initialSize, initialSize, 0, ex.Message);
        }

        long finalSize;
        try { finalSize = new FileInfo(path).Length; }
        catch { finalSize = initialSize; }

        long reclaimed = Math.Max(0, initialSize - finalSize);
        return new VacuumResult(true, path, initialSize, finalSize, reclaimed);
    }

    public static Task<VacuumResult> VacuumDatabaseAsync(string path, CancellationToken token = default) =>
        Task.Run(() => VacuumDatabase(path), token);

    public void Dispose()
    {
        if (_database == IntPtr.Zero) return;
        Native.sqlite3_close_v2(_database);
        _database = IntPtr.Zero;
    }

    private IntPtr Prepare(string sql)
    {
        var result = Native.sqlite3_prepare16_v2(_database, sql, -1, out var statement, IntPtr.Zero);
        if (result != SqliteOk)
            throw new InvalidOperationException(GetError());
        return statement;
    }

    private string GetError()
    {
        if (_database == IntPtr.Zero) return "The SQLite database could not be opened.";
        var error = Native.sqlite3_errmsg16(_database);
        return error == IntPtr.Zero ? "SQLite operation failed." : Marshal.PtrToStringUni(error) ?? "SQLite operation failed.";
    }

    private static class Native
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int sqlite3_open16(string filename, out IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        internal static extern int sqlite3_prepare16_v2(IntPtr database, string sql, int bytes, out IntPtr statement, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text16(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_changes(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg16(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close_v2(IntPtr database);
    }
}
