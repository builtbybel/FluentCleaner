using FluentCleaner.Models;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Security.Principal;

namespace FluentCleaner.ViewModels;

// Main state holder for the Cleaner page.
// It keeps the page dumb: XAML binds to plain properties, services do the actual work.
public partial class CleanerPageViewModel : ObservableObject
{
    // --- Services & fields --------------------------------------------------
    private readonly CleaningService   _cleaner       = new();
    private readonly CustomEntryService _customService = new();

    private readonly List<ScanResult> _lastScan = [];       // hand-off between "Analyze" and "Clean"
    private List<CleanerEntry> _loadedEntries    = [];      // installed main-DB entries + all enabled custom entries (custom skips IsInstalled)
    private List<string> _lastPaths = [];                   // stored so Refresh can reload without the paths being passed again
    private bool _suppressSave;                             // prevents N disk writes when SelectAll/SelectNone fires per-entry callbacks
    private CancellationTokenSource? _cts;                  // lives only during an active scan or clean; null = nothing running

    // --- Observable state ---------------------------------------------------
    [ObservableProperty] public partial ObservableCollection<CleanerCategoryViewModel> Categories { get; set; } = [];    // left panel: category tree
    [ObservableProperty] public partial ObservableCollection<ScanResultLine>           ResultLines { get; set; } = [];    // right panel: per-app results after Analyze
    [ObservableProperty] public partial ObservableCollection<DetailLine>               DetailLines { get; set; } = [];    // right panel: file/registry paths when a result row is open
    [ObservableProperty] public partial ScanResultLine?                                SelectedResultLine { get; set; }   // which result row is currently open in detail view
    [ObservableProperty] public partial string  SearchText { get; set; } = "";                                            // search box
    [ObservableProperty] public partial string  StatusText { get; set; } = ResourceService.Get("St_Loading");                     // run header above the results
    [ObservableProperty] public partial string  TotalSize     { get; set; } = "";                                        // sum of all scan results
    [ObservableProperty] public partial int     CatalogueCount { get; set; }                                             // entries in the databases, before the installed check
    [ObservableProperty] public partial double  ScanProgress  { get; set; }                                               // 0–100; drives the real progress bar
    [ObservableProperty] public partial bool    IsBusy        { get; set; }                                               // locked while a scan or clean is running
    [ObservableProperty] public partial bool    OfferElevation { get; set; }                                              // last clean skipped files and we're not admin: header offers a restart
    [ObservableProperty] public partial bool    HasLockedFiles { get; set; }
    [ObservableProperty] public partial string  UnlockFilesLabel { get; set; } = "Unlock Locked Files";
    public List<LockedFileItem> LastLockedFiles { get; } = [];

    private static readonly bool IsElevated =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public string ProgressLabel => $"{ScanProgress:0}%";                                                                   // text next to progress bar

    // Derived; no own state, everything computed from the observable properties above
    public bool   IsEmpty       => Categories.Count == 0;                          // left panel is empty (nothing loaded yet)
    public bool   IsNotEmpty    => Categories.Count > 0;                           // left panel has content
    public bool   HasSearchText => !string.IsNullOrWhiteSpace(SearchText);         // search box is filled
    public bool   IsShowingDetail  => SelectedResultLine is not null;              // right panel: detail view showing file paths for one entry
    public bool   IsShowingList    => SelectedResultLine is null;                  // right panel: normal results list after Analyze
    public string SelectedAppName  => SelectedResultLine?.AppName ?? "";           // name of the open entry shown in the detail header
    public bool   CanRunCleaner    => !IsBusy && Categories.Count > 0;            // bound to IsEnabled on Run Cleaner: Click handler can't disable the button itself
    public bool   IsNotBusy        => !IsBusy;                                     // flips the Analyze/Cancel button: true =show Analyze, false =show Cancel
    public bool   HasRun           => TotalSize.Length > 0 || ResultLines.Count > 0; // run header: from the first Analyze on, not during the start-up load

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
        if (!string.IsNullOrWhiteSpace(value)) SetAllExpanded(true); //results are useless if they're all collapsed
        RefreshCategoryState();
        OnPropertyChanged(nameof(HasSearchText));   // the footer counts show what the filter left
    }

    // keep the progress/1-100 label in sync with the bar
    partial void OnScanProgressChanged(double value) => OnPropertyChanged(nameof(ProgressLabel));

    // Results only ever change inside a run, so TotalSize and IsBusy cover the header's visibility.
    partial void OnTotalSizeChanged(string value) => OnPropertyChanged(nameof(HasRun));

    partial void OnIsBusyChanged(bool value)
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunCleaner));
        OnPropertyChanged(nameof(IsNotBusy));   // flips the Analyze<>Cancel button pair in the XAML
        OnPropertyChanged(nameof(HasRun));
    }

    // --- Commands -----------------------------------------------------------
    [RelayCommand] private void Cancel() => _cts?.Cancel();  // Abort the active scan or clean; the running async method catches OperationCanceledException and resets IsBusy
    [RelayCommand] private void SelectAll()      => SetAllSelected(true);
    [RelayCommand] private void SelectNone()     => SetAllSelected(false);
    [RelayCommand] private void SelectDefaults() => SetAllDefaults();

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
        StatusText = ResourceService.Get("St_ParsingDatabases");

        // first load uses the preload from App, Refresh or another database reads again
        var preload = _preload;
        _preload = null;
        var catalogue = preload is { } p && p.Paths.SequenceEqual(filePaths, StringComparer.OrdinalIgnoreCase)
            ? await p.Read
            : await ReadCatalogueAsync(filePaths);

        _loadedEntries = catalogue.Installed;
        RebuildVisibleCategories();

        CatalogueCount = catalogue.Total;
        StatusText = ResourceService.Fmt("St_AnalysisReady", _loadedEntries.Count, catalogue.Total);
        RefreshCategoryState();
        IsBusy = false;
        _ready.TrySetResult();
    }

    // --- FluentSeeds (the rest lives in FluentSeedDialog) --------------------

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => _ready.Task;                                   // first catalogue load is in
    public IReadOnlyList<CleanerEntry> LoadedEntries => _loadedEntries;

    // replaces the whole selection (saved + visible), one write
    public void ApplySelection(IEnumerable<string> names)
    {
        var set = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppSettings.Instance.SelectedEntries = set;
        AppSettings.Instance.CleanerSelectionInitialized = true;
        AppSettings.Instance.Save();

        _suppressSave = true;
        foreach (var entry in Categories.SelectMany(c => c.Entries))
            entry.IsSelected = set.Contains(entry.Name);
        _suppressSave = false;
    }

    // --- Catalogue read -----------------------------------------------------

    // installed entries + total count in the databases
    public sealed record Catalogue(List<CleanerEntry> Installed, int Total);

    // Started by App right after the window shows, handed to the first page load.
    private static (List<string> Paths, Task<Catalogue> Read)? _preload;

    // database paths from settings, else the bundled Winapp2.ini
    public static List<string> DatabasePaths()
    {
        var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
        if (paths.Count == 0) paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));
        return paths;
    }

    // reads Winapp2 in the background while the first tab gets built (list was 260 ms late before)
    public static void StartPreload()
    {
        var paths = DatabasePaths();
        _preload = (paths, ReadCatalogueAsync(paths));
    }

    // parse, dedupe, installed check, custom entries;no UI, so it can run before the page exists
    private static Task<Catalogue> ReadCatalogueAsync(IList<string> paths) => Task.Run(async () =>
    {
        var parser = new Winapp2Parser();
        var all = new List<CleanerEntry>();
        foreach (var path in paths)
            all.AddRange(await parser.ParseFileAsync(path));

        // Deduplicate entries that appear in multiple databases (by name)
        all = all.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // parallel, it's mostly waiting on disk/registry. new detector each time, no stale cache
        var detection = new DetectionService();
        var installed = all.AsParallel().AsOrdered().Where(detection.IsInstalled).ToList();

        // Layer the user's custom entries on top (skip IsInstalled)
        installed.RemoveAll(e => e.IsCustom);
        installed.AddRange(await new CustomEntryService().LoadEnabledEntriesAsync());

        return new Catalogue(installed, all.Count);
    });

    // --- Custom Entries ------------------------------------------------------------
    // Re-reads only the Custom/folder;skips the full Winapp2 reload.
    // CleanerPage calls this on every visit so changes from CustomPage show up immediately
    public async Task RefreshCustomEntriesAsync()
    {
        if (IsBusy) return;
        _loadedEntries.RemoveAll(e => e.IsCustom);
        _loadedEntries.AddRange(await _customService.LoadEnabledEntriesAsync());
        RebuildVisibleCategories();
        RefreshCategoryState();
    }

    // --- Analyze & Clean ----------------------------------------------------
    //  Same two operations (Analyze / Clean) exist at three scopes:
    //
    //    All entries   >>  AnalyzeAsync()             RunCleanerAsync()       << toolbar buttons
    //    Single entry  >>  AnalyzeSingleEntryAsync()  CleanSingleEntryAsync() << row context menu
    //    Category      >>  AnalyzeCategoryAsync()     CleanCategoryAsync()    << category context menu
    //
    //  All six ultimately route through:
    //    AnalyzeEntryInternalAsync()  — one entry at a time, builds the ScanResult
    //    EnsureEntryScanAsync()       — scans on demand if not yet done
    //    CleanResultsAsync()          — shared delete loop

    // --- All entries --------------------------------------------------------

    // Full scan over whatever is currently checked.
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        // Analyze only what is currently checked in the left pane.
        var selected = GetSelectedEntries();
        if (selected.Count == 0) { StatusText = ResourceService.Get("St_NothingSelected"); return; }

        BeginResultsRun(ResourceService.Get("St_Scanning"));

        using var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            long totalBytes = 0; int totalFiles = 0; int totalReg = 0; int done = 0;
            int total = selected.Count;
            var progress = new Progress<string>(msg =>
                StatusText = msg);             //live percent + entry name

            foreach (var entry in selected)
            {
                var result = await AnalyzeEntryInternalAsync(entry, progress, keepDetailSelection: false, cts.Token);
                totalBytes += result.TotalBytes;
                totalFiles += result.FilesToDelete.Count;
                totalReg   += result.RegistryToDelete.Count;
                TotalSize    = ScanResult.FormatBytes(totalBytes);                      //live size counter
                ScanProgress = ++done * 100.0 / total;                                  //progress per entry
            }
            SortResultLinesBySize(descending: true);  //biggest entries first
            StatusText = ResourceService.Fmt("St_ScanComplete", totalFiles, totalReg, TotalSize);
        }
        catch (OperationCanceledException)
        {
            StatusText = ResourceService.Get("St_ScanCancelled");
            TotalSize  = "";
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
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

        // If analyze was cancelled (or found nothing), bail out before we start deleting.
        if (_lastScan.Count == 0) return;

        IsBusy             = true;
        SelectedResultLine = null;
        ScanProgress       = 0;                                                     // reset from Analyze's 100%
        StatusText         = ResourceService.Get("St_Cleaning");

        using var cts = new CancellationTokenSource();
        _cts = cts;

        long freedBytes = 0;
        int  removed    = 0;
        try
        {
            var scannedBytes          = _lastScan.Sum(r => r.TotalBytes);
            var cleanedLines          = ResultLines.ToList();   // the clean loop removes each row as it goes
            var freedPerEntry         = new Dictionary<ScanResult, long>();
            LastLockedFiles.Clear();
            HasLockedFiles            = false;
            (removed, freedBytes)     = await CleanResultsAsync(_lastScan.ToList(), new Progress<string>(msg => StatusText = msg), cts.Token,
                                                                (result, bytes) => freedPerEntry[result] = bytes);
            HasLockedFiles            = LastLockedFiles.Count > 0;
            if (HasLockedFiles)
                UnlockFilesLabel      = $"Unlock Files ({LastLockedFiles.Count:N0} · {ScanResult.FormatBytes(LastLockedFiles.Sum(f => f.SizeBytes))})";
            var skippedBytes          = scannedBytes - freedBytes;

            // list stays after cleaning (like CCleaner), each row shows what it really freed.
            // Contents stays empty, the engine counts files/registry/cookies as one number
            foreach (var line in cleanedLines)
                ResultLines.Add(line with
                {
                    FileCount = 0, RegCount = 0,
                    Size = ScanResult.FormatBytes(line.Result is { } r ? freedPerEntry.GetValueOrDefault(r) : 0)
                });
            _lastScan.Clear();
            ClearAllEntrySizes();

            TotalSize      = "";
            OfferElevation = skippedBytes > 0 && !IsElevated;   // admin rights might get the rest
            StatusText = skippedBytes > 0
                ? ResourceService.Fmt("St_CleanDone", ScanResult.FormatBytes(freedBytes), ScanResult.FormatBytes(skippedBytes))
                : ResourceService.Fmt("St_CleanDoneSimple", removed, ScanResult.FormatBytes(freedBytes));
        }
        catch (OperationCanceledException)
        {
            //Partial clean is fine;whatever got deleted is gone.
            //just leaving the remaining results in the list so the user can see what was not cleaned yet
            UpdateTotalsFromLastScan();
            StatusText = freedBytes > 0
                ? ResourceService.Fmt("St_CleanCancelledBytes", ScanResult.FormatBytes(freedBytes))
                : ResourceService.Get("St_CleanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }

        // Log the clean run in HISTORY so the user can track their junk growth over time if they want
        if (AppSettings.Instance.CleanHistoryEnabled && freedBytes > 0)
        {
            var history = AppSettings.Instance.CleanHistory;
            history.Add(new CleanHistoryEntry(DateTime.Now, freedBytes, removed));
            while (history.Count > 50) history.RemoveAt(0);             // cap at 50; oldest out
            AppSettings.Instance.Save();
        }

        // After the clean run, if the user configured any post-clean commands
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

    // --- Single entry -------------------------------------------------------

    // Quick scan for a single entry from the little context menu, here [...] button
    public async Task AnalyzeSingleEntryAsync(CleanerEntryViewModel entryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;

        using var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var result = await AnalyzeEntryInternalAsync(entryVm.Entry,
                new Progress<string>(msg => StatusText = msg), keepDetailSelection: true, cts.Token);

            StatusText = ResourceService.Fmt("St_EntryScanned", entryVm.Name, result.FilesToDelete.Count, result.RegistryToDelete.Count);
            UpdateTotalsFromLastScan();
        }
        catch (OperationCanceledException)
        {
            StatusText = ResourceService.Get("St_ScanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

    // Clean one entry. If it was never scanned, we scan it on the fly first
    public async Task CleanSingleEntryAsync(CleanerEntryViewModel entryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;

        using var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var result = await EnsureEntryScanAsync(entryVm, new Progress<string>(msg => StatusText = msg));

            if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0)
            {
                StatusText = ResourceService.Fmt("St_EntryNothingToClean", entryVm.Name);
                return;
            }

            LastLockedFiles.Clear();
            HasLockedFiles = false;
            if (result.LockedFiles.Count > 0)
            {
                foreach (var (fp, sz) in result.LockedFiles)
                    LastLockedFiles.Add(new LockedFileItem(fp, sz, result.Entry.Name, "Locked at scan"));
            }

            var outcome = await _cleaner.CleanDetailedAsync(result, new Progress<string>(msg => StatusText = msg), cts.Token);
            var (removed, freedBytes) = (outcome.RemovedCount, outcome.FreedBytes);
            if (outcome.LockedFiles.Count > 0)
                LastLockedFiles.AddRange(outcome.LockedFiles);

            HasLockedFiles = LastLockedFiles.Count > 0;
            if (HasLockedFiles)
                UnlockFilesLabel = $"Unlock Files ({LastLockedFiles.Count:N0} · {ScanResult.FormatBytes(LastLockedFiles.Sum(f => f.SizeBytes))})";

            RemoveScanResult(result);
            entryVm.SizeText = "";

            UpdateTotalsFromLastScan();
            StatusText = ResourceService.Fmt("St_EntryCleanDone", entryVm.Name, removed, ScanResult.FormatBytes(freedBytes));
        }
        catch (OperationCanceledException)
        {
            UpdateTotalsFromLastScan();
            StatusText = ResourceService.Get("St_CleanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

    // --- Category -----------------------------------------------------------

    // Batch scan for one whole category.
    public async Task AnalyzeCategoryAsync(CleanerCategoryViewModel categoryVm)
    {
        if (IsBusy) return;

        BeginResultsRun(ResourceService.Fmt("St_CategoryScanning", categoryVm.Name));

        using var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            var selected = categoryVm.Entries.Where(e => e.IsSelected).ToList();
            int done = 0, total = selected.Count;
            var progress = new Progress<string>(msg =>
                StatusText = msg);

            foreach (var entryVm in selected)
            {
                await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false, cts.Token);
                ScanProgress = ++done * 100.0 / total;
            }

            UpdateTotalsFromLastScan();
            StatusText = ResourceService.Fmt("St_CategoryScanned", categoryVm.Name, selected.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = ResourceService.Get("St_ScanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

    // Batch clean for one category. Missing scans are generated first so the user can stay lazy.
    public async Task CleanCategoryAsync(CleanerCategoryViewModel categoryVm)
    {
        if (IsBusy) return;

        IsBusy             = true;
        SelectedResultLine = null;
        StatusText         = ResourceService.Fmt("St_CategoryCleaning", categoryVm.Name);
        var progress       = new Progress<string>(msg => StatusText = msg);

        var selected = categoryVm.Entries.Where(e => e.IsSelected).ToList();
        foreach (var vm in selected.Where(vm => !_lastScan.Any(r => r.Entry == vm.Entry)))
            await AnalyzeEntryInternalAsync(vm.Entry, progress, keepDetailSelection: false);

        LastLockedFiles.Clear();
        HasLockedFiles = false;
        var results               = _lastScan.Where(r => selected.Any(vm => vm.Entry == r.Entry)).ToList();
        var (removed, freedBytes) = await CleanResultsAsync(results, progress);
        HasLockedFiles            = LastLockedFiles.Count > 0;
        if (HasLockedFiles)
            UnlockFilesLabel      = $"Unlock Files ({LastLockedFiles.Count:N0} · {ScanResult.FormatBytes(LastLockedFiles.Sum(f => f.SizeBytes))})";

        UpdateTotalsFromLastScan();
        StatusText = ResourceService.Fmt("St_CategoryCleanDone", categoryVm.Name, removed, ScanResult.FormatBytes(freedBytes));
        IsBusy     = false;
    }

    // --- Warnings -----------------------------------------------------------

    // Warnings for the main "Run Cleaner" button.
    public IReadOnlyList<string> GetWarningsForSelectedEntries() =>
        BuildWarnings(Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected));

    // Warnings for a single entry clean action.
    public IReadOnlyList<string> GetWarningsForEntry(CleanerEntryViewModel entryVm) =>
        BuildWarnings(new[] { entryVm });

    // Warnings for a category clean action (selected entries only).
    public IReadOnlyList<string> GetWarningsForCategory(CleanerCategoryViewModel categoryVm) =>
        BuildWarnings(categoryVm.Entries.Where(e => e.IsSelected));

    // Collect warning text once and dedupe it so the dialog does not spam duplicates.
    private static IReadOnlyList<string> BuildWarnings(IEnumerable<CleanerEntryViewModel> entries) =>
        entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Warning))
            .Select(e => $"{e.Name}{Environment.NewLine}{e.Warning}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // --- Private helpers ----------------------------------------------------

    // Also called by the size column header in CleanerPage.
    public void SortResultLinesBySize(bool descending = true)
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

        var settings = AppSettings.Instance;
        var saved = settings.SelectedEntries;

        foreach (var group in groups)
        {
            var catVm = new CleanerCategoryViewModel(group.Key.Name);
            foreach (var item in group.OrderBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entryVm = new CleanerEntryViewModel(item.Entry);

                entryVm.IsSelected = settings.HasSavedCleanerSelection
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
        TotalSize    = "0 B";                                                           // visible immediately so the user sees the counter start
        OfferElevation = false;                                                         // a new run starts without the last clean's offer
        ScanProgress = 0;                                                               // reset progress bar to zero
        StatusText   = status;
    }

    // Snapshot the currently checked entries from the visible list
    private List<CleanerEntry> GetSelectedEntries() =>
        Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected).Select(e => e.Entry).ToList();

    // --- Engine (shared by all three scopes above) --------------------------

    // Central scan method; called by single-entry, category, and full analyze flows.
    // keepDetailSelection: if true, opens the detail view for this entry after scanning.
    private async Task<ScanResult> AnalyzeEntryInternalAsync(CleanerEntry entry, IProgress<string> progress, bool keepDetailSelection, CancellationToken token = default)
    {
        RemoveScanResult(entry);        // Re-analyzing an entry should replace the old result, not stack duplicates forever

        var result = await _cleaner.AnalyzeAsync(entry, progress, token);
        _lastScan.Add(result);
        UpdateEntrySize(entry, result.TotalBytes > 0 ? result.FormattedSize : "");   // no "0 B" noise in the tree

        ScanResultLine? line = null;
        if (result.FilesToDelete.Count > 0 || result.RegistryToDelete.Count > 0)
        {
            line = new ScanResultLine(entry.Name, result.FilesToDelete.Count, result.RegistryToDelete.Count,
                                      result.FormattedSize, result);
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

        StatusText = ResourceService.Fmt("St_EntryScanningProgress", entryVm.Name);
        return await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false);
    }

    public void RecordAdditionalLocksmithClean(int removed, long freedBytes, IReadOnlyList<LockedFileItem>? remaining = null)
    {
        if (remaining is not null)
        {
            LastLockedFiles.Clear();
            LastLockedFiles.AddRange(remaining.Where(f => File.Exists(f.FilePath)));
        }
        else
        {
            LastLockedFiles.RemoveAll(f => !File.Exists(f.FilePath));
        }

        HasLockedFiles = LastLockedFiles.Count > 0;
        if (HasLockedFiles)
            UnlockFilesLabel = $"Unlock Files ({LastLockedFiles.Count:N0} · {ScanResult.FormatBytes(LastLockedFiles.Sum(f => f.SizeBytes))})";

        if (freedBytes > 0)
            StatusText = $"File Locksmith freed an additional {ScanResult.FormatBytes(freedBytes)} ({removed:N0} files).";
    }

    // Shared clean loop; used by single-entry, category, and full RunCleaner flows.
    // onCleaned reports what each entry actually freed; only Run Cleaner listens.
    private async Task<(int count, long bytes)> CleanResultsAsync(List<ScanResult> results, IProgress<string> progress, CancellationToken token = default,
                                                                  Action<ScanResult, long>? onCleaned = null)
    {
        int  count = 0;
        long bytes = 0;
        int  done  = 0;
        int  total = results.Count;
        foreach (var result in results)
        {
            var outcome = await _cleaner.CleanDetailedAsync(result, progress, token);
            var (c, b) = (outcome.RemovedCount, outcome.FreedBytes);
            if (outcome.LockedFiles.Count > 0)
                LastLockedFiles.AddRange(outcome.LockedFiles);
            count += c;
            bytes += b;
            onCleaned?.Invoke(result, b);
            ScanProgress = ++done * 100.0 / total;                                  //live clean progress
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

    // detail rows for the open result, built as a list and set in one go.
    // adding row by row = one notification per path, 526,000 temp files froze the window
    private void RebuildDetailLines(ScanResultLine? line)
    {
        var rows = new List<DetailLine>();
        if (line?.Result is { } result)
        {
            AddDetailGroup(rows, ResourceService.Get("SuffixFiles"),    result.FilesToDelete);
            AddDetailGroup(rows, ResourceService.Get("SuffixRegistry"), result.RegistryToDelete.Select(r => r.ToString()));
        }
        DetailLines = new ObservableCollection<DetailLine>(rows);
    }

    // max paths shown per group;way more and the ListView draws rows over other elements (broken at 200,000).
    // display only, cleaning still deletes everything from the scan result
    private const int MaxDetailRows = 10_000;

    // The detail panel is just header rows plus plain text rows.
    private static void AddDetailGroup(List<DetailLine> rows, string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        //header wants a capital;the raw suffix is lowercase for the "3988 files" line
        if (title.Length > 0) title = char.ToUpper(title[0]) + title[1..];
        rows.Add(new DetailLine($"{title} ({list.Count})", IsHeader: true));
        foreach (var line in list.Take(MaxDetailRows))
            rows.Add(new DetailLine(line, IsHeader: false));

        // header style, not clickable, no exclude menu
        if (list.Count > MaxDetailRows)
            rows.Add(new DetailLine(ResourceService.Fmt("St_DetailMore", list.Count - MaxDetailRows), IsHeader: true));
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
        AppSettings.Instance.CleanerSelectionInitialized = true;
        AppSettings.Instance.Save();
    }

    // Single-entry save, in think this is the only correct way to persist a checkbox toggle
    // Search decouples whats **visible* (Categories) from whats *saved* (AppSettings).
    // During a search, Categories only holds the matching subset, so writing back
    // Categories.SelectMany(...) would silently drop every entry not in the results.
    // Instead we read here the full persisted set, apply exactly one add/remove, write it back.
    // The visible list is never used as the source of truth, only the one changed row is.
    private void SaveSelection(CleanerEntryViewModel entry)
    {
        if (_suppressSave) return;

        // Seed from defaults only until the user saves a selection for the first time.
        var selected = AppSettings.Instance.HasSavedCleanerSelection
            ? AppSettings.Instance.SelectedEntries.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _loadedEntries.Where(e => e.Default).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (entry.IsSelected) selected.Add(entry.Name);
        else                  selected.Remove(entry.Name);

        AppSettings.Instance.SelectedEntries = selected;
        AppSettings.Instance.CleanerSelectionInitialized = true;
        AppSettings.Instance.Save();
    }
}

public record ScanResultLine(string AppName, int FileCount, int RegCount, string Size, ScanResult? Result = null)
{
    // Its category's icon, so the list shows at a glance where the space comes from.
    private string Category => Result is null ? "" : CategoryResolver.TryMapLangSecRef(Result.Entry).Name;
    public string Glyph => CleanerCategoryViewModel.GlyphFor(Category);
    public Microsoft.UI.Xaml.Media.SolidColorBrush IconBrush => CleanerCategoryViewModel.BrushFor(Category);
    public bool HasLockedFiles => Result is { LockedFiles.Count: > 0 };
    public string LockedLabel => Result is { LockedFiles.Count: > 0 }
        ? $"{Result.LockedFiles.Count:N0} locked ({ScanResult.FormatBytes(Result.LockedBytes)})"
        : "";

    // "3988 files | 2 registry"
    public string CountSummary
    {
        get
        {
            var parts = new List<string>();
            if (FileCount > 0) parts.Add($"{FileCount} {ResourceService.Get("SuffixFiles")}");
            if (RegCount  > 0) parts.Add($"{RegCount} {ResourceService.Get("SuffixRegistry")}");
            return string.Join(" · ", parts);
        }
    }

    // Compact single-line used in the detail panel header
    public string Summary => FileCount > 0 || RegCount > 0
        ? $"{FileCount} {ResourceService.Get("SuffixFilesSingular")}, {RegCount} {ResourceService.Get("SuffixRegistryItems")}  {Size}"
        : ResourceService.Get("LabelCleaningComplete");
}

public record DetailLine(string Text, bool IsHeader)
{
    public bool IsNotHeader => !IsHeader;
}
