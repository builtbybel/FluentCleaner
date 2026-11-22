namespace FluentCleaner.Services;

// Catalog of known SQLite database locations across browsers, Electron apps, and developer tools.
public static class SqliteCompactorTargets
{
    private static readonly PathExpander Expander = new();

    public static IReadOnlyList<string> DiscoverExistingDatabases()
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Chromium-based browsers (Chrome, Edge, Brave, Vivaldi)
        DiscoverChromium(discovered, @"%LocalAppData%\Google\Chrome\User Data");
        DiscoverChromium(discovered, @"%LocalAppData%\Microsoft\Edge\User Data");
        DiscoverChromium(discovered, @"%LocalAppData%\BraveSoftware\Brave-Browser\User Data");
        DiscoverChromium(discovered, @"%LocalAppData%\Vivaldi\User Data");

        // 2. Mozilla Firefox & Gecko-based browsers (Waterfox, Zen)
        DiscoverGecko(discovered, @"%AppData%\Mozilla\Firefox\Profiles");
        DiscoverGecko(discovered, @"%AppData%\Waterfox\Profiles");
        DiscoverGecko(discovered, @"%AppData%\zen\Profiles");

        // 3. Developer & AI Coding IDEs
        DiscoverIdeState(discovered, @"%AppData%\Code");
        DiscoverIdeState(discovered, @"%AppData%\Code - Insiders");
        DiscoverIdeState(discovered, @"%AppData%\Antigravity");
        DiscoverIdeState(discovered, @"%AppData%\Cursor");
        DiscoverIdeState(discovered, @"%AppData%\Windsurf");

        return discovered.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void DiscoverChromium(HashSet<string> discovered, string userDataRaw)
    {
        foreach (var userData in Expander.ResolvePaths(userDataRaw))
        {
            if (!Directory.Exists(userData)) continue;

            // Search Default profile and any "Profile *" subdirectories
            var profileDirs = new List<string>();
            var defaultDir = Path.Combine(userData, "Default");
            if (Directory.Exists(defaultDir)) profileDirs.Add(defaultDir);

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(userData, "Profile *"))
                    profileDirs.Add(dir);
            }
            catch { }

            string[] dbFileNames =
            {
                "History",
                "Cookies",
                "Web Data",
                "Favicons",
                "Top Sites",
                "Shortcuts",
                "Network Action Predictor",
                "Login Data",
                Path.Combine("Network", "Cookies"),
                Path.Combine("Network", "Network Persistent State"),
                Path.Combine("WebStorage", "QuotaManager")
            };

            foreach (var profile in profileDirs)
            {
                foreach (var relPath in dbFileNames)
                {
                    var full = Path.Combine(profile, relPath);
                    if (File.Exists(full))
                        discovered.Add(full);
                }
            }
        }
    }

    private static void DiscoverGecko(HashSet<string> discovered, string profilesDirRaw)
    {
        foreach (var profilesDir in Expander.ResolvePaths(profilesDirRaw))
        {
            if (!Directory.Exists(profilesDir)) continue;

            try
            {
                foreach (var profile in Directory.EnumerateDirectories(profilesDir))
                {
                    try
                    {
                        foreach (var sqliteFile in Directory.EnumerateFiles(profile, "*.sqlite", SearchOption.TopDirectoryOnly))
                        {
                            discovered.Add(sqliteFile);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void DiscoverIdeState(HashSet<string> discovered, string ideRaw)
    {
        foreach (var ideDir in Expander.ResolvePaths(ideRaw))
        {
            if (!Directory.Exists(ideDir)) continue;

            var globalState = Path.Combine(ideDir, "User", "globalStorage", "state.vscdb");
            if (File.Exists(globalState))
                discovered.Add(globalState);

            var workspaceStorage = Path.Combine(ideDir, "User", "workspaceStorage");
            if (Directory.Exists(workspaceStorage))
            {
                try
                {
                    foreach (var wsDir in Directory.EnumerateDirectories(workspaceStorage))
                    {
                        var wsState = Path.Combine(wsDir, "state.vscdb");
                        if (File.Exists(wsState))
                            discovered.Add(wsState);
                    }
                }
                catch { }
            }
        }
    }
}
