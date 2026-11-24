namespace FluentCleaner.Plugins.Tweaks;

/// <summary>
/// Translated rule texts from <c>Lang\&lt;language&gt;.ini</c>, WinRules.ini itself stays english.
/// Section = rule name, keys Name and Detail;missing ones stay english.
/// </summary>
internal static class RuleText
{
    // rule name -> part -> text, for one language
    private static Dictionary<string, Dictionary<string, string>>? _byRule;
    private static string _loadedFor = "";
    private static readonly object Gate = new();

    /// <summary>Clears the cache, so new language files get picked up.</summary>
    public static void Forget()
    {
        lock (Gate) { _byRule = null; _loadedFor = ""; }
    }

    /// <summary>
    /// Translated <paramref name="part"/> ("Name" or "Detail") of <paramref name="rule"/>,
    /// or <paramref name="fallback"/>.
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

    // scanner (background) and list (UI thread) both call this, so lock it
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

    // user's language override, else whatever windows uses
    private static string Language()
    {
        try
        {
            var chosen = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            if (!string.IsNullOrEmpty(chosen)) return chosen;
        }
        catch { /* fall back to the thread culture */ }

        return System.Globalization.CultureInfo.CurrentUICulture.Name;
    }

    // de-DE.ini, then de.ini (exact names, de-DE.example.ini doesn't count)
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

    // same format as the rule files;broken/locked file = stays english, no crash
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
                // same heading twice = merged
                if (!byRule.TryGetValue(name, out current))
                    byRule[name] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (current is null) continue;
            var split = line.IndexOf('=');
            // no comment stripping here, a ; in a translated sentence is normal text
            if (split > 0) current[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
        }

        return byRule;
    }
}
