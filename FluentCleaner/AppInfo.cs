using System.Reflection;

namespace FluentCleaner;

public static class AppInfo
{
    public const bool IsInsider = false;
    public static bool IsEleven => Environment.OSVersion.Version.Build >= 22000;

    // Returns version as 26.03.01
    public static string VersionString
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver is null ? "0.00.00" : $"{ver.Major:D2}.{ver.Minor:D2}.{ver.Build:D2}";
        }
    }

    //full label e.g. "26.03.01" or "26.03.01 (Insider)"
    public static string DisplayVersion =>
        IsInsider ? $"{VersionString} (Insider)" : VersionString;

    // used as TitleBar.Subtitle; empty string hides the subtitle area
    public static string InsiderSubtitle => IsInsider ? "Insider" : "";

    // Windows 11 is build 22000 and up. The tweak catalogue gates rules on it (OsVersion=10|11),
    // since a good share of them address the Windows 11 shell and do nothing on 10.
    public static bool IsEleven => Environment.OSVersion.Version.Build >= 22000;
}
