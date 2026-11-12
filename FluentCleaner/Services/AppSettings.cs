using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCleaner.Services;

public record CleanHistoryEntry(DateTime Date, long BytesFreed, int ItemsRemoved);

// App-wide settings, persisted as a single settings.json and exposed through the
// static Instance. The Classic app reads/writes the SAME file; keys it owns
// (WindowX/WindowY, …) land in Extra and get written back untouched, so a save
// here never strips them out of the shared file.
public class AppSettings
{
    // set in the static ctor (below), not "= Load()" here: the load path is built
    // from the fields further down, which aren't set yet at this point.
    public static AppSettings Instance { get; private set; } = null!;

    // Portable mode: if settings.json sits next to the exe, use it instead of %AppData%.
    // Drop a settings.json next to FluentCleaner.exe and the app becomes fully portable.
    private static readonly string PortablePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly string RoamingPath  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentCleaner", "settings.json");

    private static readonly string SettingsFile = File.Exists(PortablePath) ? PortablePath : RoamingPath;

    [JsonIgnore]
    public static bool IsPortable => SettingsFile == PortablePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static AppSettings() => Instance = Load();

    // --- persisted settings -----------------------------------------------

    public string? CustomWinapp2Path { get; set; }
    public string? Theme             { get; set; }
    public HashSet<string> SelectedEntries { get; set; } = [];

    // Keeps an intentional empty selection distinct from first-run defaults.
    public bool CleanerSelectionInitialized { get; set; }

    [JsonIgnore]
    public bool HasSavedCleanerSelection =>
        CleanerSelectionInitialized || SelectedEntries.Count > 0;

    // which built-in databases to load on startup
    public bool EnableWinapp2 { get; set; } = true;
    public bool EnableWinapp3 { get; set; } = false;
    public bool EnableWinappx { get; set; } = true;   // AppX/bloatware database for terminal debloater

    // post-clean commands;one per line; global on/off switch
    public bool   PostCleanEnabled  { get; set; } = false;
    public string PostCleanCommands { get; set; } = "";

    // Global file, folder and registry exclusions;uses the same syntax as Winapp2.ini.
    // Examples: PATH|C:\Temp, FILE|C:\Logs|*.log, REG|HKCU\Software\Example
    public bool         GlobalExclusionsEnabled { get; set; } = false;
    public List<string> GlobalExclusions        { get; set; } = [];

    // Website domains protected by the Cookie Manager during browser cleaning.
    public List<string> CookieDomainsToKeep { get; set; } = [];

    // backdrop style;terminal-only tweak, no Settings UI on purpose
    public string Backdrop { get; set; } = "mica";

    // remembered window size;restored on next launch
    public int WindowWidth  { get; set; } = 960;
    public int WindowHeight { get; set; } = 620;

    //Junk growth tracker;logged after every successful clean run
    public bool CleanHistoryEnabled { get; set; } = true;
    public List<CleanHistoryEntry> CleanHistory { get; set; } = [];

    // Provider used for explanations and Custom Cleaner generation.
    public string AiProvider { get; set; } = "Groq";

    // Provider keys stay separate so switching providers never overwrites another key.
    public string? GroqApiKey { get; set; }
    public string? OpenAiApiKey { get; set; }
    public string? AnthropicApiKey { get; set; }

    // Modern and Classic use separate task names and schedules.
    public string ModernSchedulerFrequency { get; set; } = "Daily";
    public string ModernSchedulerTime { get; set; } = "03:00";
    public bool ModernSchedulerShutdownAfter { get; set; }

    // true once the user dismisses the startup donation tip
    public bool DonationDismissed { get; set; } = false;

    // UI language override; "" = follow Windows, "en-US" / "de-DE" = forced
    public string Language { get; set; } = "";

    // Classic writes keys this app doesn't type (WindowX/WindowY). Keep them across
    // a save instead of dropping them from the shared file.
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];


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

    // Export current settings to a file (for backup or sharing)
    public static void ExportTo(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(Instance, JsonOptions));

    // Import settings from a file and replace the current instance
    public static void ImportFrom(string path)
    {
        var json = File.ReadAllText(path);
        var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
        s.CustomWinapp2Path = NormalizePath(s.CustomWinapp2Path);
        Instance = s;
        Instance.Save();
    }

    // strips quotes and expands %env% variables so paths from the JSON always work
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var result = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
