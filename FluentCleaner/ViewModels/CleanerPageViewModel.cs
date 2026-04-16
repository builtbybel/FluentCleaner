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

    // Last scan is the hand-off between "Analyze" and "Clean".
    private readonly List<ScanResult> _lastScan = [];
    // Keep the loaded app list around so search can rebuild the left pane cheaply.
    private List<CleanerEntry> _loadedEntries = [];
    // Prevents N disk writes when SelectAll/SelectNone fires per-entry callbacks.
    private bool _suppressSave;

    // --- Observable state ---------------------------------------------------

    [ObservableProperty] private ObservableCollection<CleanerCategoryViewModel> _categories = [];    // left panel: category tree
    [ObservableProperty] private ObservableCollection<ScanResultLine>           _resultLines = [];    // right panel: per-app results after Analyze
    [ObservableProperty] private ObservableCollection<DetailLine>               _detailLines = [];    // right panel: file/registry paths when a result row is open
    [ObservableProperty] private ScanResultLine? _selectedResultLine;                                 // which result row is currently open in detail view
    [ObservableProperty] private string  _searchText    = "";                                         // search box
    [ObservableProperty] private string  _statusText    = "Loading Winapp2.ini...";                   // status bar at the bottom
    [ObservableProperty] private string  _totalSize     = "";                                         // sum of all scan results
    [ObservableProperty] private string? _loadedFilePath;                                             // path of the currently loaded Winapp2.ini
    [ObservableProperty] private bool    _isBusy;                                                     // locked while a scan or clean is running
    [ObservableProperty] private bool    _hasResults;                                                 // true once Analyze has finished and found something
    [ObservableProperty] private int     _detectedCount;                                              // number of installed apps found in the database

    // Derived — no own state, everything computed from the observable properties above

    public bool   IsEmpty       => Categories.Count == 0;                          // left panel is empty (nothing loaded yet)
    public bool   IsNotEmpty    => Categories.Count > 0;                           // left panel has content
    public bool   HasSearchText => !string.IsNullOrWhiteSpace(SearchText);         // search box is filled
    public string DetectedBadge => DetectedCount > 0 ? $"({DetectedCount} apps detected)" : "";
    public bool   IsShowingDetail => SelectedResultLine is not null;               // right panel: detail view showing file paths for one entry
    public bool   IsShowingList   => SelectedResultLine is null;                   // right panel: normal results list after Analyze
    public string SelectedAppName => SelectedResultLine?.AppName ?? "";            // name of the open entry shown in the detail header
    public string SelectedSummary => SelectedResultLine?.Summary ?? "";            // short summary line below the name

    // --- Property change hooks ----------------------------------------------

    // When the user clicks a result row, the right panel flips from summary mode to detail mode.
    partial void OnSelectedResultLineChanged(ScanResultLine? value)
    {
        OnPropertyChanged(nameof(IsShowingDetail));
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(SelectedAppName));
        OnPropertyChanged(nameof(SelectedSummary));
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
    }

    // --- Commands -----------------------------------------------------------

    [RelayCommand] private void SelectAll()   => SetAllSelected(true);
    [RelayCommand] private void SelectNone()  => SetAllSelected(false);
    [RelayCommand] private void ExpandAll()   => SetAllExpanded(true);
    [RelayCommand] private void CollapseAll() => SetAllExpanded(false);

    [RelayCommand] private void SortResultsDesc() => SortResultLinesBySize(descending: true);
    [RelayCommand] private void SortResultsAsc()  => SortResultLinesBySize(descending: false);

    // Back button in the detail pane.
    [RelayCommand] private void ClearDetail() => SelectedResultLine = null;

    // Opens the first file's folder in Explorer.
    [RelayCommand] private void OpenFolder()
    {
        var first = SelectedResultLine?.Result?.FilesToDelete.FirstOrDefault();
        var dir   = first is not null ? Path.GetDirectoryName(first) : null;
        if (dir is not null && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    // Cleans only the entry currently shown in the detail pane.
    [RelayCommand] private async Task CleanSelected()
    {
        var entry = Categories.SelectMany(c => c.Entries)
                              .FirstOrDefault(e => e.Name == SelectedResultLine?.AppName);
        if (entry is not null) await CleanSingleEntryAsync(entry);
    }

    // Small quality-of-life helper for the search box.
    [RelayCommand] private void ClearSearch() => SearchText = "";

    // --- Load ---------------------------------------------------------------

    // Parse Winapp2, keep only installed apps, then build the left pane from that.
    public async Task LoadWinapp2Async(string filePath)
    {
        IsBusy = true;
        StatusText = "Parsing Winapp2.ini...";
        LoadedFilePath = filePath;

        var allEntries       = await _parser.ParseFileAsync(filePath);
        var installedEntries = await Task.Run(() => allEntries.Where(_detection.IsInstalled).ToList());

        _loadedEntries = installedEntries;
        DetectedCount  = installedEntries.Count;
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
        HasResults = _lastScan.Count > 0;
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
        HasResults = false;
        StatusText = skippedBytes > 0
            ? $"Finished — {ScanResult.FormatBytes(freedBytes)} freed · {ScanResult.FormatBytes(skippedBytes)} skipped (files in use)"
            : $"Finished — {removed} items removed · {ScanResult.FormatBytes(freedBytes)} freed.";
        IsBusy       = false;
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
        HasResults = _lastScan.Count > 0;
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
        HasResults = _lastScan.Count > 0;
        StatusText = $"{entryVm.Name}: {removed} items removed · {ScanResult.FormatBytes(freedBytes)} freed.";
        IsBusy     = false;
    }

    // Batch scan for one whole category.
    public async Task AnalyzeCategoryAsync(CleanerCategoryViewModel categoryVm)
    {
        if (IsBusy) return;

        BeginResultsRun($"Scanning {categoryVm.Name}...");
        var progress = new Progress<string>(msg => StatusText = msg);

        foreach (var entryVm in categoryVm.Entries)
            await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false);

        UpdateTotalsFromLastScan();
        HasResults = _lastScan.Count > 0;
        StatusText = $"{categoryVm.Name} scanned - {categoryVm.Entries.Count} entries.";
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

        foreach (var vm in categoryVm.Entries.Where(vm => !_lastScan.Any(r => r.Entry == vm.Entry)))
            await AnalyzeEntryInternalAsync(vm.Entry, progress, keepDetailSelection: false);

        var results               = _lastScan.Where(r => categoryVm.Entries.Any(vm => vm.Entry == r.Entry)).ToList();
        var (removed, freedBytes) = await CleanResultsAsync(results, progress);

        UpdateTotalsFromLastScan();
        HasResults = _lastScan.Count > 0;
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
                entryVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanerEntryViewModel.IsSelected))
                        SaveSelection();
                };

                catVm.Entries.Add(entryVm);
            }
            Categories.Add(catVm);
        }
    }

    // All the little "derived state" toggles live here so we do not forget one.
    private void RefreshCategoryState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNotEmpty));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(DetectedBadge));
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
    }

    // Fresh run, clean slate.
    private void BeginResultsRun(string status)
    {
        IsBusy             = true;
        HasResults         = false;
        SelectedResultLine = null;
        ResultLines.Clear();
        _lastScan.Clear();
        StatusText = status;
    }

    // Snapshot the currently checked entries from the visible list.
    private List<CleanerEntry> GetSelectedEntries() =>
        Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected).Select(e => e.Entry).ToList();

    // Tiny helper for the search status text.
    private int CountVisibleEntries() =>
        Categories.Sum(c => c.Entries.Count);

    // Core scan path used by single-entry, category, and full analyze flows.
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

    // Cleaning assumes there is a scan result. If we do not have one yet, make one.
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
        // Boring? Yes. Easy to render and reason about? Also yes.
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
}

public record ScanResultLine(string AppName, int FileCount, int RegCount, string Size, ScanResult? Result = null)
{
    // Short label shown below the app name in the results list
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
