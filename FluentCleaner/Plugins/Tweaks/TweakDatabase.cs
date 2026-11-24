// PluginPaths and RuleText come from the parent namespace, FluentCleaner.Plugins.

namespace FluentCleaner.Plugins.Tweaks;

//reads the tweak ini files;new tweaks = just edit the ini, no new build
internal static class TweakDatabase
{
    private static List<Tweak>? _all;                   // cached until Forget()

    public static List<Tweak> All => _all ??= Read();   // sorted by category, then name

    //reload rules and language files on next access
    public static void Forget()
    {
        _all = null;
        RuleText.Forget();
    }

    // --- reading -------------------------------------------------------------

    //reads all ini files in the folder;same section name later = replaces the earlier one
    private static List<Tweak> Read()
    {
        var byName = new Dictionary<string, Tweak>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Dir)) return new List<Tweak>();

        foreach (var file in Files())
            foreach (var tweak in Parse(file))
                byName[tweak.Name] = tweak;

        return byName.Values.OrderBy(tweak => tweak.Category).ThenBy(tweak => tweak.Name).ToList();
    }

    //Plugins\Tweaks next to the exe;Lang\ below is not read here (GetFiles isn't recursive)
    private static string Dir => PluginPaths.Dir("Tweaks");

    //WinRules.ini first, then the other ini files by name.
    //AppRules.ini is in the Apps folder, so nothing to exclude here
    private static IEnumerable<string> Files()
    {
        var own = Path.Combine(Dir, "WinRules.ini");
        if (File.Exists(own)) yield return own;
        foreach (var file in Directory.GetFiles(Dir, "*.ini").OrderBy(name => name))
            if (!file.Equals(own, StringComparison.OrdinalIgnoreCase)) yield return file;
    }

    // --- one file ------------------------------------------------------------

    //collects key=value per section, builds the tweak at the next [heading] or at the end
    private static List<Tweak> Parse(string file)
    {
        var tweaks = new List<Tweak>();
        var section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = "";

        void Close() { if (name.Length > 0 && Build(name, section) is Tweak tweak) tweaks.Add(tweak); }

        foreach (var raw in SafeLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[')
            {
                Close();
                name = line.Trim('[', ']').Trim();
                section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var split = line.IndexOf('=');
            if (split > 0) section[line.Substring(0, split).Trim()] = Clean(line.Substring(split + 1));
        }

        Close();
        return tweaks;
    }

    //section -> tweak;null if it has no value at all (half written section)
    private static Tweak? Build(string name, Dictionary<string, string> section)
    {
        string Get(string key, string fallback = "") =>
            section.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

        var tweak = new Tweak
        {
            Name = name,
            Category = Get("Category"),
            Description = Get("Description"),
            Restart = Get("Restart"),
            OsVersion = Get("OsVersion"),
            Requires = Get("RequiresKey"),
            Group = Get("Group"),
            Recommended = Get("DefaultSelected").Equals("true", StringComparison.OrdinalIgnoreCase)
        };

        //Path/ValueName, Path2/ValueName2...;stops at the first missing ValueName.
        //no PathN = same path as the first one
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? "" : number.ToString();
            var first = tweak.Entries.FirstOrDefault();
            var valueName = Get("ValueName" + suffix);
            var path = Get("Path" + suffix, first?.Path ?? "");
            if (valueName.Length == 0 || path.Length == 0) break;
            tweak.Entries.Add(new Entry
            {
                Path = path,
                ValueName = valueName,
                ValueType = Get("ValueType" + suffix, first?.ValueType ?? "DWORD"),
                Recommended = Get("RecommendedValue" + suffix),
                Default = Get("DefaultValue" + suffix)
            });
        }
        return tweak.Entries.Count > 0 ? tweak : null;
    }

    //cuts off a trailing " ;comment" (needs the space before ;)
    private static string Clean(string value)
    {
        var text = value.Trim();
        var comment = text.IndexOf(" ;", StringComparison.Ordinal);
        return comment < 0 ? text : text.Substring(0, comment).Trim();
    }

    //locked/unreadable file = skipped, no crash
    private static string[] SafeLines(string file)
    {
        try { return File.ReadAllLines(file); }
        catch { return Array.Empty<string>(); }
    }
}
