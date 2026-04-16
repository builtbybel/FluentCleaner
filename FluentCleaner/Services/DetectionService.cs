using FluentCleaner.Models;
using Microsoft.Win32;

namespace FluentCleaner.Services;

// Answers the question: "is this app even installed?"
// Multiple Detect/DetectFile entries use OR logic; one hit is enough.
//
// Note: entries with no Detect/DetectFile/SpecialDetect return false> hidden.
// This is intentional, because winapp2.ini always provides detection criteria,
// even for Windows built-ins (e.g. Detect=HKLM\Software\Microsoft\Windows)
// If a custom database ever omits detection, those entries simply won't show up ;)
public class DetectionService
{
    private readonly PathExpander _expander = new();

    public bool IsInstalled(CleanerEntry entry)
    {
        if (entry.SpecialDetect is not null)
        {
            // If the code is known, trust its result and stop.
            // Unknown code: fall through to Detect/DetectFile below.
            if (TryCheckSpecialDetect(entry.SpecialDetect, out bool result))
                return result;
        }

        foreach (var reg  in entry.DetectKeys)  if (CheckRegistry(reg))  return true;
        foreach (var file in entry.DetectFiles) if (CheckFile(file))     return true;

        return false;
    }

    private static bool CheckRegistry(string regPath)
    {
        try
        {
            var (hive, subKey, valueName) = SplitRegPath(regPath);
            using var key = OpenKey(hive, subKey);
            if (key is null) return false;
            return valueName is null || key.GetValue(valueName) is not null;
        }
        catch { return false; }
    }

    private static (string hive, string subKey, string? valueName) SplitRegPath(string path)
    {
        string regPath    = path;
        string? valueName = null;

        var pipeIdx = path.LastIndexOf('|');
        if (pipeIdx >= 0)
        {
            regPath   = path[..pipeIdx];
            valueName = path[(pipeIdx + 1)..];
        }

        var slashIdx = regPath.IndexOf('\\');
        var hive     = slashIdx >= 0 ? regPath[..slashIdx].ToUpperInvariant() : regPath.ToUpperInvariant();
        var subKey   = slashIdx >= 0 ? regPath[(slashIdx + 1)..] : "";
        return (hive, subKey, valueName);
    }

    private static RegistryKey? OpenKey(string hive, string subKey) =>
        CleaningService.OpenHive(hive)?.OpenSubKey(subKey, writable: false);

    private bool CheckFile(string rawPath)
    {
        try
        {
            var expanded = _expander.ExpandVariables(rawPath);
            if (expanded.Contains('*') || expanded.Contains('?'))
                return _expander.ResolvePaths(rawPath).Count > 0;
            return File.Exists(expanded) || Directory.Exists(expanded);
        }
        catch { return false; }
    }

    // SpecialDetect is just a shorthand https://github.com/MoscaDotTo/Winapp2?tab=readme-ov-file#deletion-routines
    // DET_CHROME beats writing out the full path every time
    // Known code will resolve immediately. Unknown code: caller falls back to Detect/DetectFile
    private bool TryCheckSpecialDetect(string code, out bool result)
    {
        switch (code.ToUpperInvariant())
        {
            case "DET_CHROME":
                result = CheckFile(@"%LocalAppData%\Google\Chrome\User Data"); return true;
            case "DET_FIREFOX":
                result = CheckFile(@"%AppData%\Mozilla\Firefox"); return true;
            case "DET_IE":
                result = CheckRegistry(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\IEXPLORE.EXE"); return true;
            case "DET_THUNDERBIRD":
                result = CheckFile(@"%AppData%\Thunderbird"); return true;
            case "DET_OPERA":
                result = CheckFile(@"%AppData%\Opera Software\Opera Stable"); return true;
            case "DET_EDGE":
                result = CheckFile(@"%LocalAppData%\Microsoft\Edge\User Data"); return true;
            case "DET_WINSTORE":
                // Packages folder exists on every Win10+ machine; Store is available
                result = CheckFile(@"%LocalAppData%\Packages"); return true;
            default:
                result = false; return false;
        }
    }
}
