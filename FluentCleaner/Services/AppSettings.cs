using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCleaner.Services;

public class AppSettings
{
    // single shared instance, loaded from disk at startup
    public static AppSettings Instance { get; private set; } = Load();

    // %AppData%\FluentCleaner\settings.json
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentCleaner", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // --- persisted settings -----------------------------------------------

    public string? CustomWinapp2Path { get; set; }
    public string? Theme             { get; set; }
    public HashSet<string> SelectedEntries { get; set; } = [];

    // which built-in databases to load on startup
    public bool EnableWinapp2 { get; set; } = true;
    public bool EnableWinapp3 { get; set; } = false;
    public bool EnableWinappx { get; set; } = true;   // AppX/bloatware database for terminal debloater

    // post-clean commands;one per line; global on/off switch
    public bool   PostCleanEnabled  { get; set; } = false;
    public string PostCleanCommands { get; set; } = "";

    // backdrop style;terminal-only tweak, no Settings UI on purpose
    public string Backdrop { get; set; } = "mica";

    // remembered window size; restored on next launch
    public int WindowWidth  { get; set; } = 960;
    public int WindowHeight { get; set; } = 620;

    // -----------------------------------------------------------------------

    // true only when CustomWinapp2Path points to a file that actually exists
    [JsonIgnore]
    public bool HasCustomPath =>
        !string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path);

    // re-reads settings.json from disk and replaces the current instance;useful shit after external edits, called after the user saves changes in Settings
    public static void Reload() => Instance = Load();

    // yields all active database paths in load order (built-ins first, custom last)
    public IEnumerable<string> ResolveDatabasePaths()
    {
        if (EnableWinapp2)
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
            if (File.Exists(p)) yield return p;
        }
        if (EnableWinapp3)
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Winapp3.ini");
            if (File.Exists(p)) yield return p;
        }
        if (!string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path))
            yield return CustomWinapp2Path;
    }

    // returns the custom path if set and valid, otherwise falls back to the bundled Winapp2.ini
    public string ResolveWinapp2Path()
    {
        if (!string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path))
            return CustomWinapp2Path;
        return Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    private static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new();
            s.CustomWinapp2Path = NormalizePath(s.CustomWinapp2Path);
            return s;
        }
        catch { return new(); }  // corrupted file;just start fresh
    }

    // strips quotes and expands %env% variables so paths from the JSON always work
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var result = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
