namespace FluentCleaner.Plugins.Tweaks;

/// <summary>
/// Rule wording from <c>Lang\&lt;language&gt;.ini</c>, so WinRules.ini stays as it came from
/// upstream. Section = rule name, keys Name and Detail; anything missing keeps the English.
/// </summary>
internal static class RuleText
{
    // part -> text, per rule name. Loaded for one language and dropped when it changes.
    private static Dictionary<string, Dictionary<string, string>>? _byRule;
    private static string _loadedFor = "";
    private static readonly object Gate = new();

    /// <summary>Drops the cache so a language file added at runtime is picked up.</summary>
    public static void Forget()
    {
        lock (Gate) { _byRule = null; _loadedFor = ""; }
    }

    /// <summary>
    /// The translated <paramref name="part"/> ("Name" or "Detail") of the rule called
    /// <paramref name="rule"/>, or <paramref name="fallback"/> when there is none.
    /// </summary>
    public static string Get(string part, string rule, string fallback)
    {
        var map = Map();
        return map.TryGetValue(rule, out var parts)
            && parts.TryGetValue(part, out var text)
            && text.Length > 0
                ? text
                : fallback;
    }

    // --- loading -------------------------------------------------------------

    // The scanner asks from a background thread while the list asks from the UI thread, so
    // the load happens once behind a lock rather than twice in parallel.
    private static Dictionary<string, Dictionary<string, string>> Map()
    {
        var language = Language();
        lock (Gate)
        {
            if (_byRule is { } cached && _loadedFor == language) return cached;
            _loadedFor = language;
            return _byRule = Read(language);
        }
    }

    // What the app is actually showing: the user's own choice when they made one, otherwise
    // whatever Windows decided for us.
    private static string Language()
    {
        try
        {
            var chosen = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            if (!string.IsNullOrEmpty(chosen)) return chosen;
        }
        catch { /* fall through to the thread culture */ }

        return System.Globalization.CultureInfo.CurrentUICulture.Name;
    }

    // de-DE.ini, then de.ini. Exact names only, so de-DE.example.ini never counts as German.
    private static Dictionary<string, Dictionary<string, string>> Read(string language)
    {
        var empty = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.Combine(PluginPaths.Dir("Tweaks"), "Lang");
        if (language.Length == 0 || !Directory.Exists(dir)) return empty;

        var candidates = new List<string> { language };
        var dash = language.IndexOf('-');
        if (dash > 0) candidates.Add(language.Substring(0, dash));

        foreach (var candidate in candidates)
        {
            var file = Path.Combine(dir, candidate + ".ini");
            if (File.Exists(file)) return Parse(file);
        }
        return empty;
    }

    // Sections of key=value, same shape as the rule files. A locked or broken file leaves the
    // app in English rather than taking it down: wording is never worth a crash.
    private static Dictionary<string, Dictionary<string, string>> Parse(string file)
    {
        var byRule = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch { return byRule; }

        Dictionary<string, string>? current = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[')
            {
                var name = line.Trim('[', ']').Trim();
                if (name.Length == 0) { current = null; continue; }
                // A repeated heading adds to the first rather than replacing it.
                if (!byRule.TryGetValue(name, out current))
                    byRule[name] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (current is null) continue;
            var split = line.IndexOf('=');
            // Translations are kept whole: no trailing comment is cut off, because a semicolon
            // belongs in a sentence far more often than it starts a note here.
            if (split > 0) current[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
        }

        return byRule;
    }
}
