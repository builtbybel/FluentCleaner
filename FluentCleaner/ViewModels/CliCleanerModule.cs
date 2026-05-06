using FluentCleaner.Models;
using FluentCleaner.Services;
using System.Collections.ObjectModel;

namespace FluentCleaner.ViewModels;

//Self-contained CLI module for winapp2 clean/analyze/scan/list/categories commands.
// CliViewModel owns one instance and forwards all cleaner-related commands here
public class CliCleanerModule
{
    private readonly Winapp2Parser    _parser    = new();
    private readonly DetectionService _detection = new();
    private readonly CleaningService  _cleaner   = new();

    private List<CleanerEntry> _entries = [];   // loaded on InitAsync, used for all commands

    // --- Init -------------------------------------------------------------------

    // Loads every enabled database, merges and deduplicates entries, filters to installed apps.
    // Returns (databaseCount, entryCount) so CliViewModel can format the startup message.
    public async Task<(int Databases, int Entries)> InitAsync()
    {
        // Old single-database terminal loading. I kept this here as reference because
        // the Cleaner page now supports with v26.04.30 multiple enabled databases.
        // var path = AppSettings.Instance.ResolveWinapp2Path();
        // var all = await _parser.ParseFileAsync(path);

        // Match the Cleaner page: load every enabled database, merge entries,
        // then deduplicate by name so Winapp2/Winapp3/custom overlaps stay sane.
        var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
        var all   = new List<CleanerEntry>();
        foreach (var path in paths)
            all.AddRange(await _parser.ParseFileAsync(path));

        all      = all.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _entries = await Task.Run(() => all.Where(_detection.IsInstalled).ToList());

        return (paths.Count, _entries.Count);
    }

    // --- Autocomplete -----------------------------------------------------------

    // Entry names that contain the query; used for clean/analyze/scan/list suggestions
    public IEnumerable<string> GetEntrySuggestions(string query) =>
        _entries.Select(e => e.Name)
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

    // Category names that contain the query; used for "clean/analyze/scan category <name>"
    public IEnumerable<string> GetCategorySuggestions(string query) =>
        CategoryNames().Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

    // --- Command dispatch -------------------------------------------------------

    public async Task ExecuteAsync(string verb, string arg, ObservableCollection<string> output, Action<bool> setBusy)
    {
        switch (verb)
        {
            case "clean":      await RunCleanAsync(arg, output, setBusy);   break;
            case "analyze":
            case "scan":       await RunAnalyzeAsync(arg, output, setBusy); break;
            case "list":       RunList(arg, output);                        break;
            case "categories": RunCategories(output);                       break;
        }
    }

    // --- Commands ---------------------------------------------------------------

    // Scans and deletes;single entry, whole category, or everything
    private async Task RunCleanAsync(string name, ObservableCollection<string> output, Action<bool> setBusy)
    {
        var entries = ResolveEntries(name);
        if (entries.Count == 0) { output.Add($"  No entries matching '{name}'."); return; }

        setBusy(true);
        int totalCount = 0; long totalBytes = 0;

        foreach (var entry in entries)
        {
            var result = await _cleaner.AnalyzeAsync(entry);
            if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0) continue;
            var (count, bytes) = await _cleaner.CleanAsync(result);
            totalCount += count;
            totalBytes += bytes;
            output.Add($"  {entry.Name}: {count} items · {ScanResult.FormatBytes(bytes)}");
        }

        output.Add(totalCount > 0
            ? $"  Done — {totalCount} items removed · {ScanResult.FormatBytes(totalBytes)} freed."
            : "  Nothing to clean.");
        setBusy(false);
    }

    // Scans only, nothing gets deleted;single entry, whole category, or everything
    private async Task RunAnalyzeAsync(string name, ObservableCollection<string> output, Action<bool> setBusy)
    {
        var entries = ResolveEntries(name);
        if (entries.Count == 0) { output.Add($"  No entries matching '{name}'."); return; }

        setBusy(true);
        long totalBytes = 0;

        foreach (var entry in entries)
        {
            var result = await _cleaner.AnalyzeAsync(entry);
            if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0) continue;
            totalBytes += result.TotalBytes;
            output.Add($"  {entry.Name}: {result.FilesToDelete.Count} files · {result.RegistryToDelete.Count} registry · {result.FormattedSize}");
        }

        output.Add(totalBytes > 0
            ? $"  Total: {ScanResult.FormatBytes(totalBytes)} found."
            : "  Nothing found.");
        setBusy(false);
    }

    // Dumps all (or filtered) entry names
    private void RunList(string filter, ObservableCollection<string> output)
    {
        var list = string.IsNullOrWhiteSpace(filter)
            ? _entries
            : _entries.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var e in list)
            output.Add($"  {e.Name}");
        output.Add($"  — {list.Count} entries.");
    }

    private void RunCategories(ObservableCollection<string> output)
    {
        var cats = CategoryNames();
        foreach (var c in cats) output.Add($"  {c}");
        output.Add($"  — {cats.Count} categories.");
    }

    // --- Helpers ----------------------------------------------------------------

    // Resolves "all", "selected", "category <name>", or a single entry name to a list of entries
    private List<CleanerEntry> ResolveEntries(string name)
    {
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            return _entries;

        if (name.Equals("selected", StringComparison.OrdinalIgnoreCase))
        {
            var saved = AppSettings.Instance.SelectedEntries;
            return _entries
                .Where(e => saved.Count > 0 ? saved.Contains(e.Name) : e.Default)
                .ToList();
        }

        if (name.StartsWith("category ", StringComparison.OrdinalIgnoreCase))
        {
            var catName = name["category ".Length..].Trim();
            return _entries
                .Where(e => CategoryResolver.TryMapLangSecRef(e).Name
                    .Contains(catName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var single = FindEntry(name);
        return single is not null ? [single] : [];
    }

    // Distinct category names from the loaded entries, sorted alphabetically
    private List<string> CategoryNames() =>
        _entries.Select(e => CategoryResolver.TryMapLangSecRef(e).Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

    // Exact match wins, falls back to first partial match
    private CleanerEntry? FindEntry(string name) =>
        _entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? _entries.FirstOrDefault(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
}
