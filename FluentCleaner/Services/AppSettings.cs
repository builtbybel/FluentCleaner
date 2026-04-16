using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCleaner.Services;

public class AppSettings
{
    public static AppSettings Instance { get; private set; } = Load();

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentCleaner", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // --- Settings -----------------

    public string? CustomWinapp2Path { get; set; }
    public string? Theme { get; set; }
    public HashSet<string> SelectedEntries { get; set; } = [];

    // -------------------------------------------------------------------------

    [JsonIgnore]
    public bool HasCustomPath =>
        !string.IsNullOrWhiteSpace(CustomWinapp2Path) && File.Exists(CustomWinapp2Path);

    // Refreshes Instance from disk; we need to call this before the first window opens
    public static void Reload() => Instance = Load();

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
        catch { return new(); }
    }

    // Strips quotes and expands %env% vars; some handy shit for paths copy-pasted from Explorer
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var result = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}