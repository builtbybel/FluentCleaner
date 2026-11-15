// PluginPaths comes from the parent namespace, FluentCleaner.Plugins.

namespace FluentCleaner.Plugins.Apps;

//decides how safe it is to remove an installed app: AppRules.ini says what is known, and the
//list below is the safety net the ini can never argue its way past.
internal sealed class AppRules
{
    //packages windows itself needs; nothing a rule file says may lower these
    private static readonly string[] Required = {
        "*_cw5n1h2txyewy", "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller",
        "Microsoft.SecHealthUI", "Microsoft.UI.Xaml", "Microsoft.VCLibs",
        "Microsoft.NET.Native", "Microsoft.WindowsAppRuntime", "Microsoft.MicrosoftEdge"
    };
    private readonly List<(string Pattern, AppSafety Safety)> _rules = new();    // what the ini said
    private readonly List<(string Pattern, AppSafety Safety)> _programs = new(); // the same for classic programs

    //reads AppRules.ini out of this plugin's own folder; a missing file leaves every app Personal,
    //which only means nothing is pre-ticked - the Required net above still holds
    public static AppRules Load()
    {
        var path = Path.Combine(PluginPaths.Dir("Apps"), "AppRules.ini");
        return File.Exists(path) ? Parse(File.ReadAllLines(path)) : new AppRules();
    }

    //turns the ini into pattern and safety pairs; Action=apps names packages, Action=programs names text
    internal static AppRules Parse(IEnumerable<string> lines)
    {
        var result = new AppRules();
        string action = "", safety = "";
        var packages = new List<string>();
        var programs = new List<string>();
        void Commit()
        {
            var level = safety.Equals("Protected", StringComparison.OrdinalIgnoreCase) ? AppSafety.Protected
                : safety.Equals("Safe", StringComparison.OrdinalIgnoreCase) ? AppSafety.Candidate : AppSafety.Personal;
            if (action.Equals("apps", StringComparison.OrdinalIgnoreCase))
                foreach (var pattern in packages) result._rules.Add((pattern, level));
            else if (action.Equals("programs", StringComparison.OrdinalIgnoreCase))
                foreach (var pattern in programs) result._programs.Add((pattern, level));
        }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";")) continue;
            if (line.StartsWith("[")) { Commit(); action = safety = ""; packages.Clear(); programs.Clear(); continue; }
            int split = line.IndexOf('=');
            if (split < 0) continue;
            var key = line.Substring(0, split).Trim();
            var value = line.Substring(split + 1).Trim();
            int comment = value.IndexOf(" ;", StringComparison.Ordinal);
            if (comment >= 0) value = value.Substring(0, comment).Trim();
            if (key.Equals("Action", StringComparison.OrdinalIgnoreCase)) action = value;
            else if (key.Equals("Safety", StringComparison.OrdinalIgnoreCase)) safety = value;
            else if (key.Equals("Package", StringComparison.OrdinalIgnoreCase) && value.Length > 0) packages.Add(value);
            else if (key.Equals("Program", StringComparison.OrdinalIgnoreCase) && value.Length > 0) programs.Add(value);
        }
        Commit();
        return result;
    }

    //compares a package family against one ini pattern: a leading * matches the end, otherwise the start
    internal static bool Matches(string family, string pattern) => pattern.StartsWith("*")
        ? pattern.Length > 1 && family.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase)
        : pattern.Length > 0 && family.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);

    //a program rule hits when its text stands in the displayed name or in the publisher
    internal static bool Mentions(InstalledApp app, string pattern) =>
        app.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0
        || app.Publisher.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

    //the safety level of one app: anything windows needs is protected, the rest is what the ini says
    public AppSafety Classify(InstalledApp app)
    {
        if (app.NonRemovable || app.IsFramework || app.IsResourcePackage
            || Required.Any(pattern => Matches(app.Family, pattern))) return AppSafety.Protected;
        //a classic program has no family to compare, so its rules read the name and the publisher
        if (app.IsProgram)
            return _programs.Where(rule => Mentions(app, rule.Pattern)).Select(rule => rule.Safety)
                .DefaultIfEmpty(AppSafety.Personal).Max();
        return _rules.Where(rule => Matches(app.Family, rule.Pattern)).Select(rule => rule.Safety)
            .DefaultIfEmpty(AppSafety.Personal).Max();
    }
}
