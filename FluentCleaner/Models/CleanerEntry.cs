namespace FluentCleaner.Models;

/* Represents one application entry parsed from Winapp2.ini.
   Holds everything needed to detect whether the app is installed
   and to describe what files and registry keys should be cleaned. */
public class CleanerEntry
{
    public string Name { get; set; } = "";                          // Display name shown in the UI (e.g. "Microsoft Edge").
    public string? Section { get; set; }                            // Optional free-form section name from the ini.
    public int? LangSecRef { get; set; }                            // Category code (e.g. 3025 = Windows) used by CategoryResolver.
    public List<string> DetectKeys { get; set; } = new();          // Registry paths checked to detect installation (OR logic).
    public List<string> DetectFiles { get; set; } = new();         // File/folder paths checked to detect installation (OR logic).
    public string? SpecialDetect { get; set; }                     // Named code for well-known apps (e.g. "DET_CHROME", "DET_EDGE").
    public List<FileKeyEntry> FileKeys { get; set; } = new();      // Files to delete; path pattern, file filter, and recurse flag.
    public List<RegKeyEntry> RegKeys { get; set; } = new();        // Registry keys or values to delete.
    public List<ExcludeKeyEntry> ExcludeKeys { get; set; } = new(); // Paths/files to skip even if they match a FileKey.
    public string? Warning { get; set; }                           // Winapp2 warning shown as tooltip (e.g. "Removes saved passwords").
    public bool Default { get; set; } = true;                      // Whether this entry is checked by default in the UI.
}
