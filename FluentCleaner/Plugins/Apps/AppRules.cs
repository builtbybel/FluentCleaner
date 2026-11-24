// PluginPaths comes from the parent namespace, FluentCleaner.Plugins.

namespace FluentCleaner.Plugins.Apps;

//how safe is it to remove an app? AppRules.ini + a hardcoded list the ini can't override
internal sealed class AppRules
{
    //windows needs these;always protected, whatever the ini says
    private static readonly string[] Required = {
        "*_cw5n1h2txyewy", "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller",
        "Microsoft.SecHealthUI", "Microsoft.UI.Xaml", "Microsoft.VCLibs",
        "Microsoft.NET.Native", "Microsoft.WindowsAppRuntime", "Microsoft.MicrosoftEdge"
    };
    private readonly List<(string Pattern, AppSafety Safety)> _rules = new();    // store apps from the ini
    private readonly List<(string Pattern, AppSafety Safety)> _programs = new(); // classic programs from the ini

    //no AppRules.ini = all Personal (nothing pre-ticked), Required still applies
    public static AppRules Load()
    {
        var path = Path.Combine(PluginPaths.Dir("Apps"), "AppRules.ini");
        return File.Exists(path) ? Parse(File.ReadAllLines(path)) : new AppRules();
    }

    //Action=apps -> Package= patterns, Action=programs -> Program= text
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

    //*xyz = ends with, xyz = starts with
    internal static bool Matches(string family, string pattern) => pattern.StartsWith("*")
        ? pattern.Length > 1 && family.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase)
        : pattern.Length > 0 && family.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);

    //program rule: text in name or publisher
    internal static bool Mentions(InstalledApp app, string pattern) =>
        app.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0
        || app.Publisher.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

    //windows stuff = protected, rest from the ini
    public AppSafety Classify(InstalledApp app)
    {
        if (app.NonRemovable || app.IsFramework || app.IsResourcePackage
            || Required.Any(pattern => Matches(app.Family, pattern))) return AppSafety.Protected;
        //programs have no family name, match by name/publisher
        if (app.IsProgram)
            return _programs.Where(rule => Mentions(app, rule.Pattern)).Select(rule => rule.Safety)
                .DefaultIfEmpty(AppSafety.Personal).Max();
        return _rules.Where(rule => Matches(app.Family, rule.Pattern)).Select(rule => rule.Safety)
            .DefaultIfEmpty(AppSafety.Personal).Max();
    }
}
