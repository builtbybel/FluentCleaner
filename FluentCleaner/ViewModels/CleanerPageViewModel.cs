using FluentCleaner.Models;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace FluentCleaner.ViewModels;

// Main state holder for the Cleaner page.
// It keeps the page dumb: XAML binds to plain properties, services do the actual work.
public partial class CleanerPageViewModel : ObservableObject
{
    // --- Services & fields --------------------------------------------------
    private readonly Winapp2Parser    _parser    = new();
    private readonly DetectionService _detection = new();
    private readonly CleaningService  _cleaner   = new();

    private readonly List<ScanResult> _lastScan = [];       // hand-off between "Analyze" and "Clean"
    private List<CleanerEntry> _loadedEntries = [];         // kept so search can rebuild the left pane without re-parsing
    private List<string> _lastPaths = [];                    // stored so Refresh can reload without the paths being passed again
    private bool _suppressSave;                             // prevents N disk writes when SelectAll/SelectNone fires per-entry callbacks

    // --- Observable state ---------------------------------------------------
    [ObservableProperty] public partial ObservableCollection<CleanerCategoryViewModel> Categories { get; set; } = [];    // left panel: category tree
    [ObservableProperty] public partial ObservableCollection<ScanResultLine>           ResultLines { get; set; } = [];    // right panel: per-app results after Analyze
    [ObservableProperty] public partial ObservableCollection<DetailLine>               DetailLines { get; set; } = [];    // right panel: file/registry paths when a result row is open
    [ObservableProperty] public partial ScanResultLine?                                SelectedResultLine { get; set; }   // which result row is currently open in detail view
    [ObservableProperty] public partial string  SearchText { get; set; } = "";                                            // search box
    [ObservableProperty] public partial string  StatusText { get; set; } = "Loading Winapp2.ini...";                     // status bar at the bottom
    [ObservableProperty] public partial string  TotalSize  { get; set; } = "";                                           // sum of all scan results
    [ObservableProperty] public partial bool    IsBusy     { get; set; }                                                  // locked while a scan or clean is running

    // Derived; no own state, everything computed from the observable properties above
    public bool   IsEmpty       => Categories.Count == 0;                          // left panel is empty (nothing loaded yet)
    public bool   IsNotEmpty    => Categories.Count > 0;                           // left panel has content
    public bool   HasSearchText => !string.IsNullOrWhiteSpace(SearchText);         // search box is filled
    public bool   IsShowingDetail  => SelectedResultLine is not null;              // right panel: detail view showing file paths for one entry
    public bool   IsShowingList    => SelectedResultLine is null;                  // right panel: normal results list after Analyze
    public string SelectedAppName  => SelectedResultLine?.AppName ?? "";           // name of the open entry shown in the detail header
    public bool   CanRunCleaner    => !IsBusy && Categories.Count > 0;            // bound to IsEnabled on Run Cleaner: Click handler can't disable the button itself

    // --- Property change hooks ----------------------------------------------

    // When the user clicks a result row, the right panel flips from summary mode to detail mode.
    partial void OnSelectedResultLineChanged(ScanResultLine? value)
    {
        OnPropertyChanged(nameof(IsShowingDetail));
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(SelectedAppName));
        RebuildDetailLines(value);
    }

    // Search rebuilds the visible category list from scratch; just simple, no weird state to debug
    partial void OnSearchTextChanged(string value)
    {
        RebuildVisibleCategories();
        RefreshCategoryState();
        StatusText = Categories.Count > 0
            ? $"Showing {CountVisibleEntries()} matching entries."
            : "No matching entries.";
        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunCleaner));
    }

    // --- Commands -----------------------------------------------------------

    [RelayCommand] private void SelectAll()      => SetAllSelected(true);
    [RelayCommand] private void SelectNone()     => SetAllSelected(false);
    [RelayCommand] private void SelectDefaults() => SetAllDefaults();
    [RelayCommand] private void ExpandAll()   => SetAllExpanded(true);
    [RelayCommand] private void CollapseAll() => SetAllExpanded(false);

    [RelayCommand] private void SortResultsDesc() => SortResultLinesBySize(descending: true);
    [RelayCommand] private void SortResultsAsc()  => SortResultLinesBySize(descending: false);

    // Back button in the detail pane.
    [RelayCommand] private void ClearDetail() => SelectedResultLine = null;

    // Cleans only the entry currently shown in the detail pane.
    [RelayCommand] private async Task CleanSelected()
    {
        var entry = Categories.SelectMany(c => c.Entries)
                              .FirstOrDefault(e => e.Name == SelectedResultLine?.AppName);
        if (entry is not null) await CleanSingleEntryAsync(entry);
    }

    // --- Load ---------------------------------------------------------------

    // Parse Winapp2, keep only installed apps, then build the left pane from that.
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await LoadWinapp2Async(_lastPaths);  // reloads from disk
    private bool CanRefresh() => !IsBusy && _lastPaths.Count > 0;

    public async Task LoadWinapp2Async(IList<string> filePaths)
    {
        _lastPaths = [.. filePaths];
        IsBusy     = true;
        StatusText = "Parsing databases...";

        var allEntries = new List<CleanerEntry>();
        foreach (var path in filePaths)
            allEntries.AddRange(await _parser.ParseFileAsync(path));

        // Deduplicate entries that appear in multiple databases (by name)
        allEntries = allEntries.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var installedEntries = await Task.Run(() => allEntries.Where(_detection.IsInstalled).ToList());

        _loadedEntries = installedEntries;
        RebuildVisibleCategories();

        StatusText = $"Analysis ready - {installedEntries.Count} apps found in {allEntries.Count} entries.";
        RefreshCategoryState();
        IsBusy = false;
    }

    // --- Analyze & Clean ----------------------------------------------------

    // Full scan over whatever is currently checked.
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        // Analyze only what is currently checked in the left pane.
        var selected = GetSelectedEntries();
        if (selected.Count == 0) { StatusText = "Nothing selected."; return; }

        BeginResultsRun("Scanning...");

        long totalBytes = 0; int totalFiles = 0; int totalReg = 0;
        var progress = new Progress<string>(msg => StatusText = msg);

        foreach (var entry in selected)
        {
            var result = await AnalyzeEntryInternalAsync(entry, progress, keepDetailSelection: false);
            totalBytes += result.TotalBytes;
            totalFiles += result.FilesToDelete.Count;
            totalReg   += result.RegistryToDelete.Count;
        }

        TotalSize  = ScanResult.FormatBytes(totalBytes);
        StatusText = $"Scan complete - {totalFiles} files, {totalReg} registry items ({TotalSize})";
        IsBusy     = false;
    }

    // Analyze is only useful once we actually have something loaded.
    private bool CanAnalyze() => !IsBusy && Categories.Count > 0;

    // Full clean pass over the last scan results.
    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task RunCleanerAsync()
    {
        // Hitting Clean without a scan first is okay.
        // We quietly do the scan and then continue.
        if (_lastScan.Count == 0)
            await AnalyzeAsync();

        IsBusy             = true;
        SelectedResultLine = null;
        StatusText         = "Cleaning...";

        var scannedBytes              = _lastScan.Sum(r => r.TotalBytes);
        var (removed, freedBytes)     = await CleanResultsAsync(_lastScan.ToList(), new Progress<string>(msg => StatusText = msg));
        var skippedBytes              = scannedBytes - freedBytes;

        _lastScan.Clear();
        ResultLines.Clear();
        ResultLines.Add(new ScanResultLine("Done", removed, 0, "", null));
        ClearAllEntrySizes();

        TotalSize  = "";
        StatusText = skippedBytes > 0
            ? $"Finished — {ScanResult.FormatBytes(freedBytes)} freed · {ScanResult.FormatBytes(skippedBytes)} skipped (files in use)"
            : $"Finished — {removed} items removed · {ScanResult.FormatBytes(freedBytes)} freed.";
        IsBusy = false;

        await RunPostCleanTasksAsync();
    }

    // --- Post-clean tasks -----------------------------------------------------

    // Splits the free-text command box and runs each line via cmd so pipes work
    private static async Task RunPostCleanTasksAsync()
    {
        if (!AppSettings.Instance.PostCleanEnabled)
            return;

        var lines = AppSettings.Instance.PostCleanCommands
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
            return;

        foreach (var line in lines)
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName    = "cmd.exe",
                    Arguments   = $"/c {line}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is not null)
                    await process.WaitForExitAsync();
            }
            catch { /* broken command never crashes the app */ }
        }
    }

    // Same rule as Analyze: no loaded entries, no cleaning.
    private bool CanClean() => !IsBusy && Categories.Count > 0;

    // Quick scan for a single entry from the little context menu, here [...] button
    public async Task AnalyzeSingleEntryAsync(CleanerEntryViewModel entryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;

        var result = await AnalyzeEntryInternalAsync(entryVm.Entry,
            new Progress<string>(msg => StatusText = msg), keepDetailSelection: true);

        StatusText = $"{entryVm.Name}: {result.FilesToDelete.Count} files, {result.RegistryToDelete.Count} registry items";
        UpdateTotalsFromLastScan();
        IsBusy     = false;
    }

    // Clean one entry. If it was never scanned, we scan it on the fly first
    public async Task CleanSingleEntryAsync(CleanerEntryViewModel entryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;

        var result = await EnsureEntryScanAsync(entryVm, new Progress<string>(msg => StatusText = msg));
        if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0)
        {
            StatusText = $"{entryVm.Name}: nothing to clean.";
            IsBusy = false;
            return;
        }

        var (removed, freedBytes) = await _cleaner.CleanAsync(result, new Progress<string>(msg => StatusText = msg));
        RemoveScanResult(result);
        entryVm.SizeText = "";

        UpdateTotalsFromLastScan();
        StatusText = $"{entryVm.Name}: {removed} items removed · {ScanResult.FormatBytes(freedBytes)} freed.";
        IsBusy     = false;
    }

    // Batch scan for one whole category.
    public async Task AnalyzeCategoryAsync(CleanerCategoryViewModel categoryVm)
    {
        if (IsBusy) return;

        BeginResultsRun($"Scanning {categoryVm.Name}...");
        var progress = new Progress<string>(msg => StatusText = msg);

        var selected = categoryVm.Entries.Where(e => e.IsSelected).ToList();
        foreach (var entryVm in selected)
            await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false);

        UpdateTotalsFromLastScan();
        StatusText = $"{categoryVm.Name} scanned - {selected.Count} entries.";
        IsBusy     = false;
    }

    // Batch clean for one category. Missing scans are generated first so the user can stay lazy.
    public async Task CleanCategoryAsync(CleanerCategoryViewModel categoryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;
        StatusText         = $"Cleaning {categoryVm.Name}...";
        var progress       = new Progress<string>(msg => StatusText = msg);

        var selected = categoryVm.Entries.Where(e => e.IsSelected).ToList();
        foreach (var vm in selected.Where(vm => !_lastScan.Any(r => r.Entry == vm.Entry)))
            await AnalyzeEntryInternalAsync(vm.Entry, progress, keepDetailSelection: false);

        var results               = _lastScan.Where(r => selected.Any(vm => vm.Entry == r.Entry)).ToList();
        var (removed, freedBytes) = await CleanResultsAsync(results, progress);

        UpdateTotalsFromLastScan();
        StatusText = $"{categoryVm.Name}: {removed} items removed · {ScanResult.FormatBytes(freedBytes)} freed.";
        IsBusy     = false;
    }

    // --- Warnings -----------------------------------------------------------

    // Warnings for the main "Run Cleaner" button.
    public IReadOnlyList<string> GetWarningsForSelectedEntries() =>
        BuildWarnings(Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected));

    // Warnings for a single entry clean action.
    public IReadOnlyList<string> GetWarningsForEntry(CleanerEntryViewModel entryVm) =>
        BuildWarnings([entryVm]);

    // Warnings for a category clean action.
    public IReadOnlyList<string> GetWarningsForCategory(CleanerCategoryViewModel categoryVm) =>
        BuildWarnings(categoryVm.Entries);

    // Collect warning text once and dedupe it so the dialog does not spam duplicates.
    private static IReadOnlyList<string> BuildWarnings(IEnumerable<CleanerEntryViewModel> entries) =>
        entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Warning))
            .Select(e => $"{e.Name}{Environment.NewLine}{e.Warning}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // --- Private helpers ----------------------------------------------------

    private void SortResultLinesBySize(bool descending = true)
    {
        var sorted = descending
            ? ResultLines.OrderByDescending(l => l.Result?.TotalBytes ?? 0).ToList()
            : ResultLines.OrderBy(l => l.Result?.TotalBytes ?? 0).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int from = ResultLines.IndexOf(sorted[i]);
            if (from != i) ResultLines.Move(from, i);
        }
    }

    // Search just rebuilds the category list from the original loaded entries.
    // That sounds brute-force, but at this scale it is simple and fast enough
    private void RebuildVisibleCategories()
    {
        var visible = string.IsNullOrWhiteSpace(SearchText)
            ? _loadedEntries
            : _loadedEntries.Where(e => e.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        RebuildCategories(visible);
    }

    // Turn a flat entry list into grouped view models for the left pane.
    private void RebuildCategories(List<CleanerEntry> entries)
    {
        Categories.Clear();

        var groups = entries
            .Select(e => new { Entry = e, Category = CategoryResolver.TryMapLangSecRef(e) })
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key.Order)
            .ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase);

        var saved = AppSettings.Instance.SelectedEntries; //read HashSet from JSON

        foreach (var group in groups)
        {
            var catVm = new CleanerCategoryViewModel(group.Key.Name);
            foreach (var item in group.OrderBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entryVm = new CleanerEntryViewModel(item.Entry);

                // User's saved selection takes priority.
                // On first launch (nothing saved yet), fall back to Default=True/False from the Winapp2.ini
                entryVm.IsSelected = saved.Count > 0
                    ? saved.Contains(item.Entry.Name)
                    : item.Entry.Default;

                // Auto-save whenever the user toggles a single checkbox.
                // Important: pass the changed row so search results don't overwrite
                // the full saved selection with only the currently visible entries.
                entryVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanerEntryViewModel.IsSelected))
                        SaveSelection(entryVm);
                };

                catVm.Entries.Add(entryVm);
            }
            Categories.Add(catVm);
        }
    }

    // All the little "derived state" toggles live here so we do not forget one
    private void RefreshCategoryState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNotEmpty));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(CanRunCleaner));
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
    }

    // Resets all result state before a new scan or clean run
    private void BeginResultsRun(string status)
    {
        IsBusy             = true;
        SelectedResultLine = null;
        ResultLines.Clear();
        _lastScan.Clear();
        StatusText = status;
    }

    // Snapshot the currently checked entries from the visible list
    private List<CleanerEntry> GetSelectedEntries() =>
        Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected).Select(e => e.Entry).ToList();

    // Tiny helper for the search status text.
    private int CountVisibleEntries() =>
        Categories.Sum(c => c.Entries.Count);

    // Central scan method;called by single-entry, category, and full analyze flows.
    // !keepDetailSelection!: if true, opens the detail view for this entry after scanning.
    private async Task<ScanResult> AnalyzeEntryInternalAsync(CleanerEntry entry, IProgress<string> progress, bool keepDetailSelection)
    {
        // Re-analyzing an entry should replace the old result, not stack duplicates forever.
        RemoveScanResult(entry);

        var result = await _cleaner.AnalyzeAsync(entry, progress);
        _lastScan.Add(result);
        UpdateEntrySize(entry, result.FormattedSize);

        ScanResultLine? line = null;
        if (result.FilesToDelete.Count > 0 || result.RegistryToDelete.Count > 0)
        {
            line = new ScanResultLine(entry.Name, result.FilesToDelete.Count, result.RegistryToDelete.Count, result.FormattedSize, result);
            ResultLines.Add(line);
        }

        if (keepDetailSelection && line is not null)
            SelectedResultLine = line;

        return result;
    }

    // "Clean" without prior "Analyze"; scans on the fly before deleting.
    // Already have a result? Use it directly.
    private async Task<ScanResult> EnsureEntryScanAsync(CleanerEntryViewModel entryVm, IProgress<string> progress)
    {
        var existing = _lastScan.FirstOrDefault(r => r.Entry == entryVm.Entry);
        if (existing is not null) return existing;

        StatusText = $"Scanning {entryVm.Name}...";
        return await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false);
    }

    // Shared clean loop so the single-entry and category flows do not drift apart over time.
    private async Task<(int count, long bytes)> CleanResultsAsync(List<ScanResult> results, IProgress<string> progress)
    {
        int  count = 0;
        long bytes = 0;
        foreach (var result in results)
        {
            var (c, b) = await _cleaner.CleanAsync(result, progress);
            count += c;
            bytes += b;
            RemoveScanResult(result);
            UpdateEntrySize(result.Entry, "");
        }
        return (count, bytes);
    }

    // Convenience overload when all we have is the entry object.
    private void RemoveScanResult(CleanerEntry entry)
    {
        var existing = _lastScan.FirstOrDefault(r => r.Entry == entry);
        if (existing is not null) RemoveScanResult(existing);
    }

    // Remove a stale scan result from both backing stores; memory and visible results list.
    private void RemoveScanResult(ScanResult result)
    {
        _lastScan.Remove(result);
        var line = ResultLines.FirstOrDefault(l => l.Result == result);
        if (line is not null) ResultLines.Remove(line);
    }

    // Keep the little size label next to a checkbox in sync with the latest scan.
    private void UpdateEntrySize(CleanerEntry entry, string sizeText)
    {
        var vm = Categories.SelectMany(c => c.Entries).FirstOrDefault(e => e.Entry == entry);
        if (vm is not null) vm.SizeText = sizeText;
    }

    // After a full clean pass, all per-entry size badges are stale anyway.
    private void ClearAllEntrySizes()
    {
        foreach (var entry in Categories.SelectMany(c => c.Entries))
            entry.SizeText = "";
    }

    // Recompute the big total on the right from the current in-memory scan list.
    private void UpdateTotalsFromLastScan()
    {
        TotalSize = _lastScan.Count > 0
            ? ScanResult.FormatBytes(_lastScan.Sum(r => r.TotalBytes))
            : "";
    }

    // Build the detail panel rows from a selected result line.
    private void RebuildDetailLines(ScanResultLine? line)
    {
        DetailLines.Clear();
        if (line?.Result is not { } result) return;

        // The detail panel is just a flattened "header + rows" list.
        // Boring? Yeah, easy to render and reason about? Also yes
        AddDetailGroup("Files",    result.FilesToDelete);
        AddDetailGroup("Registry", result.RegistryToDelete.Select(r => r.ToString()));
    }

    // The detail panel is just header rows plus plain text rows.
    private void AddDetailGroup(string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        DetailLines.Add(new DetailLine($"{title} ({list.Count})", IsHeader: true));
        foreach (var line in list)
            DetailLines.Add(new DetailLine(line, IsHeader: false));
    }

    private void SetAllSelected(bool value)
    {
        _suppressSave = true;
        foreach (var entry in Categories.SelectMany(c => c.Entries))
            entry.IsSelected = value;
        _suppressSave = false;
        SaveSelection();
    }

    // Restores every entry to its Default=True/False value from the database.
    // Uses the same suppress-save pattern as SetAllSelected to avoid N disk writes.
    private void SetAllDefaults()
    {
        _suppressSave = true;
        foreach (var entry in Categories.SelectMany(c => c.Entries))
            entry.IsSelected = entry.Entry.Default;
        _suppressSave = false;
        SaveSelection();
    }

    private void SetAllExpanded(bool value)
    {
        foreach (var cat in Categories)
            cat.IsExpanded = value;
    }

    private void SaveSelection()
    {
        if (_suppressSave) return;
        AppSettings.Instance.SelectedEntries = Categories
            .SelectMany(c => c.Entries)
            .Where(e => e.IsSelected)
            .Select(e => e.Name)
            .ToHashSet();
        AppSettings.Instance.Save();
    }

    // Single-entry save, in think this is the only correct way to persist a checkbox toggle
    //
    // Search decouples whats *visible* (Categories) from whats *saved* (AppSettings).
    // During a search, Categories only holds the matching subset, so writing back
    // Categories.SelectMany(...) would silently drop every entry not in the results.
    //
    // Instead we read here the full persisted set, apply exactly one add/remove, write it back.
    // The visible list is never used as the source of truth, only the one changed row is.
    private void SaveSelection(CleanerEntryViewModel entry)
    {
        if (_suppressSave) return;

        // No custom selection saved yet > seed from Winapp2 defaults so the first
        // manual toggle doesn't wipe every entry that was on by default
        var selected = AppSettings.Instance.SelectedEntries.Count > 0
            ? AppSettings.Instance.SelectedEntries.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _loadedEntries.Where(e => e.Default).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (entry.IsSelected) selected.Add(entry.Name);
        else                  selected.Remove(entry.Name);

        AppSettings.Instance.SelectedEntries = selected;
        AppSettings.Instance.Save();
    }
}

public record ScanResultLine(string AppName, int FileCount, int RegCount, string Size, ScanResult? Result = null)
{
    // "3988 files | 2 registry"
    public string CountSummary
    {
        get
        {
            var parts = new List<string>();
            if (FileCount > 0) parts.Add($"{FileCount} files");
            if (RegCount  > 0) parts.Add($"{RegCount} registry");
            return string.Join(" · ", parts);
        }
    }

    // Compact single-line used in the detail panel header
    public string Summary => FileCount > 0 || RegCount > 0
        ? $"{FileCount} file(s), {RegCount} registry item(s)  {Size}"
        : "Cleaning complete.";
}

public record DetailLine(string Text, bool IsHeader)
{
    public bool IsNotHeader => !IsHeader;
}
