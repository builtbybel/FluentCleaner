using System.Reflection;

namespace FluentCleaner;

public static class AppInfo
{
    // Returns version as 26.03.01
    public static string VersionString
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver is null ? "0.00.00" : $"{ver.Major:D2}.{ver.Minor:D2}.{ver.Build:D2}";
        }
    }
}
