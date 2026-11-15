// PluginPaths and RuleText come from the parent namespace, FluentCleaner.Plugins.

namespace FluentCleaner.Plugins.Tweaks;

//the only class that knows the tweak ini format. every registry tweak stands in those files,
//so a new tweak is a few lines of text and never a new build.
internal static class TweakDatabase
{
    private static List<Tweak>? _all;                   // cached until a catalogue or plugin change

    public static List<Tweak> All => _all ??= Read();   // every tweak, by category and then by name

    //back to disk on the next read: rule packs and language files alike
    public static void Forget()
    {
        _all = null;
        RuleText.Forget();
    }

    // --- reading -------------------------------------------------------------

    //reads every tweak ini of the rules folder; a later file that repeats a section replaces it,
    //which is how a plugin corrects a tweak instead of only adding one
    private static List<Tweak> Read()
    {
        var byName = new Dictionary<string, Tweak>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Dir)) return new List<Tweak>();

        foreach (var file in Files())
            foreach (var tweak in Parse(file))
                byName[tweak.Name] = tweak;

        return byName.Values.OrderBy(tweak => tweak.Category).ThenBy(tweak => tweak.Name).ToList();
    }

    //this plugin's own folder beside the exe; the translations sit in Lang\ underneath it,
    //which this never reads, because GetFiles does not descend
    private static string Dir => PluginPaths.Dir("Tweaks");

    //winrules.ini first, then every other ini beside it in name order. no file has to be
    //excluded by name any more: the app uninstaller keeps AppRules.ini in its own folder
    private static IEnumerable<string> Files()
    {
        var own = Path.Combine(Dir, "WinRules.ini");
        if (File.Exists(own)) yield return own;
        foreach (var file in Directory.GetFiles(Dir, "*.ini").OrderBy(name => name))
            if (!file.Equals(own, StringComparison.OrdinalIgnoreCase)) yield return file;
    }

    // --- one file ------------------------------------------------------------

    //reads one file into tweaks: each section is collected as key and value and built at the next
    //heading, so the last line of the file still closes the last tweak
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

    //builds one tweak out of a section; null when it names no value at all, which is a half
    //written section and not something the user should be offered
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

        //path and valuename hold the first value, path2 the second, and the numbers end where a
        //valuename is missing; a later value that names no path repeats the path of the first
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

    //cuts a trailing comment off a value; only a space in front of the semicolon makes it one
    private static string Clean(string value)
    {
        var text = value.Trim();
        var comment = text.IndexOf(" ;", StringComparison.Ordinal);
        return comment < 0 ? text : text.Substring(0, comment).Trim();
    }

    //reads the lines of a file; a locked or unreadable one is a missing plugin, not a crash
    private static string[] SafeLines(string file)
    {
        try { return File.ReadAllLines(file); }
        catch { return Array.Empty<string>(); }
    }
}
