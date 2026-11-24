using System.Globalization;

namespace FluentCleaner.Services;

//Browser discovery and selective cleanup for the Cookie Manager under Settings > Cookies
//kept separate on purpose; the normal Winapp2 cleaner only calls the small hand-off method
internal static class CookieService
{
    private enum StoreKind { Chromium, Firefox }

    private sealed record CookieStore(string Browser, string DatabasePath, StoreKind Kind);

    internal sealed class DomainScanResult
    {
        public IReadOnlyList<string> Domains { get; init; } = [];
        public int StoresFound { get; init; }
        public int StoresUnavailable { get; init; }
    }

    internal readonly record struct CleanAttempt(
        bool Handled, bool Succeeded, int CookiesRemoved = 0, long BytesFreed = 0);

    private static readonly (string Browser, string Root)[] ChromiumRoots = BuildChromiumRoots();
    private static readonly (string Browser, string Root)[] FirefoxRoots = BuildFirefoxRoots();

    public static DomainScanResult ScanDomains()
    {
        var stores = DiscoverStores();
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unavailable = 0;

        foreach (var store in stores)
        {
            try
            {
                using var database = new WinSqlite(store.DatabasePath);
                var column = store.Kind == StoreKind.Chromium ? "host_key" : "host";
                var table = store.Kind == StoreKind.Chromium ? "cookies" : "moz_cookies";

                foreach (var host in database.QueryStrings($"SELECT DISTINCT {column} FROM {table}"))
                {
                    var domain = NormalizeDomain(host);
                    if (domain.Length > 0) domains.Add(domain);
                }
            }
            catch { unavailable++; }
        }

        return new DomainScanResult
        {
            Domains = domains.OrderBy(domain => domain, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            StoresFound = stores.Count,
            StoresUnavailable = unavailable
        };
    }

    // Scans browser cookies and filters them against the IntelligentDomains knowledge base
    public static IReadOnlyList<string> DetectIntelligentDomains()
    {
        var scan = ScanDomains();
        return scan.Domains
            .Where(IntelligentDomains.IsAuthDomain)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    //one selected website removes that domain and its subdomains from every supported profile,
    //except for anything the keep list protects
    public static int DeleteDomains(IEnumerable<string> domains, IEnumerable<string> domainsToKeep)
    {
        var selectedDomains = NormalizeDomains(domains);
        if (selectedDomains.Length == 0) return 0;
        var keptDomains = NormalizeDomains(domainsToKeep);
        var unavailable = 0;

        foreach (var store in DiscoverStores())
        {
            try
            {
                using var database = new WinSqlite(store.DatabasePath);
                var column = store.Kind == StoreKind.Chromium ? "host_key" : "host";
                var table = store.Kind == StoreKind.Chromium ? "cookies" : "moz_cookies";
                var deleteExpression = BuildDeleteExpression(column, selectedDomains, keptDomains);

                database.Execute("BEGIN IMMEDIATE");
                try
                {
                    database.Execute($"DELETE FROM {table} WHERE {deleteExpression}");
                    database.Execute("COMMIT");
                }
                    database.Execute("COMMIT");
                }
                catch
                {
                    try { database.Execute("ROLLBACK"); } catch { }
                    throw;
                }

                try
                {
                    database.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                    database.Execute("VACUUM");
                }
                catch { }
            }
            catch { unavailable++; }
        }

        return unavailable;
    }

    public static CleanAttempt TryCleanProtectedStore(string path, IEnumerable<string> domainsToKeep)
    {
        //CleaningService calls this for every file; ordinary results leave immediately
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith("Cookies", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("Device Bound Sessions", StringComparison.OrdinalIgnoreCase))
            return default;

        var protectedDomains = NormalizeDomains(domainsToKeep);
        if (protectedDomains.Length == 0) return default;

        var match = MatchStore(path);
        if (match.Store is null) return default;

        //Sidecars belong to the managed store. Deleting one can invalidate a kept login.
        if (!match.IsPrimaryDatabase)
            return new CleanAttempt(Handled: true, Succeeded: true);

        try
        {
            var before = new FileInfo(path).Length;
            using var database = new WinSqlite(path);
            var column = match.Store.Kind == StoreKind.Chromium ? "host_key" : "host";
            var table = match.Store.Kind == StoreKind.Chromium ? "cookies" : "moz_cookies";

            database.Execute("BEGIN IMMEDIATE");
            int removed;
            try
            {
                database.PopulateTempDomainTable("_kept_domains", protectedDomains);
                removed = database.Execute($@"
                    DELETE FROM {table}
                    WHERE lower(ltrim({column}, '.')) NOT IN (SELECT domain FROM _kept_domains)
                      AND (
                          instr(lower(ltrim({column}, '.')), '.') == 0
                          OR substr(lower(ltrim({column}, '.')), instr(lower(ltrim({column}, '.')), '.') + 1) NOT IN (SELECT domain FROM _kept_domains)
                      )
                      AND (
                          instr(substr(lower(ltrim({column}, '.')), instr(lower(ltrim({column}, '.')), '.') + 1), '.') == 0
                          OR substr(substr(lower(ltrim({column}, '.')), instr(lower(ltrim({column}, '.')), '.') + 1), instr(substr(lower(ltrim({column}, '.')), instr(lower(ltrim({column}, '.')), '.') + 1), '.') + 1) NOT IN (SELECT domain FROM _kept_domains)
                      )");
                database.Execute("COMMIT");
            }
            catch
            {
                try { database.Execute("ROLLBACK"); } catch { }
                throw;
            }

            try
            {
                database.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                database.Execute("VACUUM");
            }
            catch { }

            var after = new FileInfo(path).Length;
            return new CleanAttempt(true, true, removed, Math.Max(0, before - after));
        }
        catch
        {
            //A recognized store is protected:so never fall back to deleting the whole database
            return new CleanAttempt(Handled: true, Succeeded: false);
        }
    }

    public static string NormalizeDomain(string? value)
    {
        var domain = value?.Trim() ?? "";
        if (domain.Length == 0) return "";

        if (Uri.TryCreate(domain.Contains("://") ? domain : "https://" + domain,
                UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            domain = uri.Host;

        domain = domain.Trim().TrimStart('*').TrimStart('.').TrimEnd('.').ToLowerInvariant();
        try { return new IdnMapping().GetAscii(domain); }
        catch { return ""; }
    }

    private static string[] NormalizeDomains(IEnumerable<string> domains) => domains
        .Select(NormalizeDomain)
        .Where(domain => domain.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    //a keep entry protects the domain and its subdomains, same rule as the cleaner
    public static bool IsProtected(string domain, HashSet<string> keptDomains)
    {
        var current = domain;
        while (true)
        {
            if (keptDomains.Contains(current)) return true;
            var dot = current.IndexOf('.');
            if (dot < 0) return false;
            current = current.Substring(dot + 1);
        }
    }

    private static string BuildDeleteExpression(string column, string[] selectedDomains, string[] keptDomains)
    {
        var selected = BuildDomainExpression(column, selectedDomains);
        return keptDomains.Length == 0
            ? selected
            : $"({selected}) AND NOT ({BuildDomainExpression(column, keptDomains)})";
    }

    private static string BuildDomainExpression(string column, IEnumerable<string> domains) =>
        string.Join(" OR ", domains.Select(domain =>
        {
            var escaped = domain.Replace("'", "''");
            return $"lower(ltrim({column}, '.')) = '{escaped}' OR lower(ltrim({column}, '.')) LIKE '%.{escaped}'";
        }));

    private static (CookieStore? Store, bool IsPrimaryDatabase) MatchStore(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return default; }

        foreach (var store in DiscoverStores())
        {
            if (string.Equals(fullPath, store.DatabasePath, StringComparison.OrdinalIgnoreCase))
                return (store, true);

            var fileName = Path.GetFileName(fullPath);
            var sameFolder = string.Equals(Path.GetDirectoryName(fullPath),
                Path.GetDirectoryName(store.DatabasePath), StringComparison.OrdinalIgnoreCase);
            if (!sameFolder) continue;

            var databaseName = Path.GetFileName(store.DatabasePath);
            if (fileName.StartsWith(databaseName + "-", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(databaseName + ".", StringComparison.OrdinalIgnoreCase) ||
                store.Kind == StoreKind.Chromium && fileName.StartsWith("Device Bound Sessions", StringComparison.OrdinalIgnoreCase))
                return (store, false);
        }

        return default;
    }

    private static List<CookieStore> DiscoverStores()
    {
        var stores = new List<CookieStore>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (browser, root) in ChromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            AddChromiumProfile(stores, seen, browser, root);
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root))
                    AddChromiumProfile(stores, seen, browser, profile);
            }
            catch { }
        }

        foreach (var (browser, root) in FirefoxRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root))
                    AddStore(stores, seen, browser, Path.Combine(profile, "cookies.sqlite"), StoreKind.Firefox);
            }
            catch { }
        }

        return stores;
    }

    private static void AddChromiumProfile(List<CookieStore> stores, HashSet<string> seen, string browser, string profile)
    {
        AddStore(stores, seen, browser, Path.Combine(profile, "Network", "Cookies"), StoreKind.Chromium);
        AddStore(stores, seen, browser, Path.Combine(profile, "Cookies"), StoreKind.Chromium);
    }

    private static void AddStore(List<CookieStore> stores, HashSet<string> seen, string browser, string path, StoreKind kind)
    {
        if (!File.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (seen.Add(fullPath)) stores.Add(new CookieStore(browser, fullPath, kind));
    }

    private static (string Browser, string Root)[] BuildChromiumRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            ("Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Google Chrome Beta", Path.Combine(local, "Google", "Chrome Beta", "User Data")),
            ("Google Chrome Dev", Path.Combine(local, "Google", "Chrome Dev", "User Data")),
            ("Google Chrome Canary", Path.Combine(local, "Google", "Chrome SxS", "User Data")),
            ("Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Microsoft Edge Beta", Path.Combine(local, "Microsoft", "Edge Beta", "User Data")),
            ("Microsoft Edge Dev", Path.Combine(local, "Microsoft", "Edge Dev", "User Data")),
            ("Microsoft Edge Canary", Path.Combine(local, "Microsoft", "Edge SxS", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Brave Beta", Path.Combine(local, "BraveSoftware", "Brave-Browser-Beta", "User Data")),
            ("Brave Nightly", Path.Combine(local, "BraveSoftware", "Brave-Browser-Nightly", "User Data")),
            ("Chromium", Path.Combine(local, "Chromium", "User Data")),
            ("Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("Opera", Path.Combine(roaming, "Opera Software", "Opera Stable")),
            ("Opera GX", Path.Combine(roaming, "Opera Software", "Opera GX Stable"))
        ];
    }

    private static (string Browser, string Root)[] BuildFirefoxRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            ("Mozilla Firefox", Path.Combine(roaming, "Mozilla", "Firefox", "Profiles")),
            // Firefox from the Microsoft Store keeps its profiles here. Without it the keep list
            // didn't apply there, and Winapp2 deleted that cookies.sqlite whole.
            ("Mozilla Firefox", Path.Combine(local, "Packages", "Mozilla.Firefox_n80bbvh6b1yt2",
                                             "LocalCache", "Roaming", "Mozilla", "Firefox", "Profiles"))
        ];
    }
}
