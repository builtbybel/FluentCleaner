using System.Runtime.InteropServices;

namespace FluentCleaner.Services;

// Tiny wrapper around SQLite already shipped with Windows 10/11.
// This file exists only for the Cookie Manager under Settings > Cookies.
internal sealed class WinSqlite : IDisposable
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

    /* Populates an indexed SQLite TEMP TABLE with domain strings in bounded batches.
       Supports 10,000+ domains without triggering SQLite's SQLITE_LIMIT_EXPR_DEPTH. */
    public void PopulateTempDomainTable(string tableName, IEnumerable<string> domains)
    {
        Execute($"CREATE TEMP TABLE IF NOT EXISTS {tableName} (domain TEXT PRIMARY KEY);");
        Execute($"DELETE FROM {tableName};");

        var domainList = domains as IList<string> ?? domains.ToList();
        if (domainList.Count == 0) return;

        const int batchSize = 400;
        for (int i = 0; i < domainList.Count; i += batchSize)
        {
            var batch = domainList.Skip(i).Take(batchSize);
            var values = string.Join(",", batch.Select(d => $"('{d.Replace("'", "''")}')"));
            Execute($"INSERT OR IGNORE INTO {tableName} (domain) VALUES {values};");
        }
    }

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
