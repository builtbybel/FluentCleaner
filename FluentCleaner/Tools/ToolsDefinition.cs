namespace FluentCleaner.Tools;

public enum ToolsCategory
{
    All,
    System,
    Privacy,
    Network,
    Apps,
    Debloat
}

public sealed class ScriptMeta
{
    public string Description { get; init; } = "No description available.";
    public List<string> Options { get; init; } = [];
    public ToolsCategory Category { get; init; } = ToolsCategory.All;
    public bool UseConsole { get; init; }
    public bool UseLog { get; init; }
    public bool SupportsInput { get; init; }
    public string InputPlaceholder { get; init; } = "";
    public string PoweredByText { get; init; } = "";
    public string PoweredByUrl { get; init; } = "";
}

public sealed class ToolsDefinition
{
    public ToolsDefinition(string title, string icon, string scriptPath, ScriptMeta meta)
    {
        Title = title;
        Icon = icon;
        ScriptPath = scriptPath;
        Description = meta.Description;
        Options = meta.Options;
        Category = meta.Category;
        UseConsole = meta.UseConsole;
        UseLog = meta.UseLog;
        SupportsInput = meta.SupportsInput;
        InputPlaceholder = meta.InputPlaceholder;
        PoweredByText = meta.PoweredByText;
        PoweredByUrl = meta.PoweredByUrl;
    }

    public string Title { get; }
    public string Icon { get; }
    public string ScriptPath { get; }
    public string Description { get; }
    public IReadOnlyList<string> Options { get; }
    public ToolsCategory Category { get; }
    public bool UseConsole { get; }
    public bool UseLog { get; }
    public bool SupportsInput { get; }
    public string InputPlaceholder { get; }
    public string PoweredByText { get; }
    public string PoweredByUrl { get; }
}
