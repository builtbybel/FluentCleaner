using FluentCleaner.Models;

namespace FluentCleaner.Services;

// Owns the Custom/ folder: every read, write and rename in it goes through here.
// One custom cleaner is one Winapp2 .ini; switched off, it is renamed to .ini.disabled.
// CustomPage lists and edits the files, the cleaner and SilentRunner load the enabled ones.
public class CustomEntryService
{
    public static readonly string CustomDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Custom");

    private const string On = ".ini", Off = ".ini.disabled";
    private const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    private readonly Winapp2Parser    _parser    = new();
    private readonly DetectionService _detection = new();

    // Enabled entries as ready-to-use CleanerEntry objects. Detection criteria are honoured
    // when present and skipped when the user deliberately left them out.
    public async Task<List<CleanerEntry>> LoadEnabledEntriesAsync()
    {
        var entries = new List<CleanerEntry>();
        foreach (var path in ListFiles().Where(IsEnabled))
        {
            foreach (var ce in await _parser.ParseFileAsync(path))
            {
                ce.IsCustom = true;
                bool hasDetection = ce.DetectFiles.Count > 0 || ce.DetectKeys.Count > 0 || ce.SpecialDetect is not null;
                if (hasDetection && !_detection.IsInstalled(ce)) continue;
                entries.RemoveAll(e => string.Equals(e.Name, ce.Name, Ci));
                entries.Add(ce);
            }
        }
        return entries;
    }

    // Every custom cleaner, on or off, sorted by name. Anything else in the folder
    // (old .ps1 scripts, notes) is left alone.
    public static List<string> ListFiles() =>
        !Directory.Exists(CustomDir) ? [] :
        Directory.EnumerateFiles(CustomDir)
                 .Where(f => f.EndsWith(On, Ci) || f.EndsWith(Off, Ci))
                 .OrderBy(NameOf, StringComparer.OrdinalIgnoreCase)
                 .ToList();

    public static bool IsEnabled(string path) => path.EndsWith(On, Ci);

    // "App.ini" and "App.ini.disabled" are both "App".
    public static string NameOf(string path)
    {
        var file = Path.GetFileName(path);
        return file.Substring(0, file.LastIndexOf(On, Ci));
    }

    // Counts [Section] headers as a quick proxy for the entry count.
    public static int CountEntries(string path)
    {
        try { return File.ReadLines(path).Count(l => l.StartsWith("[") && l.EndsWith("]")); }
        catch { return 0; }
    }

    // Switches a cleaner on or off by renaming it and returns the new path.
    public static string SetEnabled(string path, bool enabled)
    {
        var target = PathFor(NameOf(path), enabled);
        if (!string.Equals(target, path, Ci))
            File.Move(path, target);
        return target;
    }

    // Writes a cleaner. Editing keeps it on or off as it was, and a rename removes the old file
    // only once the new one is written. False when another cleaner already has that name.
    public static bool Save(string? existingPath, string name, string content)
    {
        var target  = PathFor(name, existingPath is null || IsEnabled(existingPath));
        var renamed = !string.Equals(existingPath, target, Ci);
        if (renamed && (File.Exists(PathFor(name, true)) || File.Exists(PathFor(name, false))))
            return false;

        Directory.CreateDirectory(CustomDir);
        File.WriteAllText(target, content);
        if (existingPath is not null && renamed)
            File.Delete(existingPath);
        return true;
    }

    private static string PathFor(string name, bool enabled) => Path.Combine(CustomDir, name + (enabled ? On : Off));
}
