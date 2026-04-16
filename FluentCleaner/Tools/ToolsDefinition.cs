using System.Reflection;

namespace FluentCleaner.Tools;

// Temporary container filled by ReadMetadataFromScript; immediately unpacked into ToolsDefinition.
// Only lives inside the parsing call; nothing outside the Tools layer needs to reference it.
public record ScriptMeta
{
    public string       Description      { get; init; } = "No description available.";
    public List<string> Options          { get; init; } = new();
    public ToolsCategory Category        { get; init; } = ToolsCategory.All;
    public bool         UseConsole       { get; init; } // # Host: console
    public bool         UseLog           { get; init; } // # Host: log
    public bool         SupportsInput    { get; init; } // # Input: true
    public string       InputPlaceholder { get; init; } = "";
    public string       PoweredByText    { get; init; } = "";
    public string       PoweredByUrl     { get; init; } = "";
}

// One loaded extension; built from the script file path + its parsed header comments.
// so the ListView binds to Title; everything else drives the detail pane.
public class ToolsDefinition
{
    public string        Title            { get; }                          // filename without extension
    public string        Description      { get; }                          // # Description:
    public string        Icon             { get; }                          // emoji from PickIconForScript
    public string        ScriptPath       { get; }                          // absolute path to .ps1
    public ToolsCategory Category         { get; set; } = ToolsCategory.All; // # Category:
    public List<string>  Options          { get; } = new();                 // # Options: semicolon-separated
    public bool          UseConsole       { get; set; }                     // # Host: console
    public bool          UseLog           { get; set; }                     // # Host: log
    public bool          SupportsInput    { get; set; }                     // # Input: true
    public string        InputPlaceholder { get; set; } = "";               // # InputPlaceholder:
    public string        PoweredByText    { get; set; } = "";               // # PoweredBy:
    public string        PoweredByUrl     { get; set; } = "";               // # PoweredUrl:

    public ToolsDefinition(string title, string icon, string scriptPath, ScriptMeta meta)
    {
        Title            = title;
        Description      = meta.Description;
        Icon             = icon;
        ScriptPath       = scriptPath;
        Category         = meta.Category;
        UseConsole       = meta.UseConsole;
        UseLog           = meta.UseLog;
        SupportsInput    = meta.SupportsInput;
        InputPlaceholder = meta.InputPlaceholder;
        PoweredByText    = meta.PoweredByText;
        PoweredByUrl     = meta.PoweredByUrl;
        Options.AddRange(meta.Options);
    }

    public override string ToString() => Title;
}
