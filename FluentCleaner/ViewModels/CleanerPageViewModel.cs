using FluentCleaner.Models;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Text;

namespace FluentCleaner.ViewModels;

// Main state holder for the Cleaner page.
// Keeps the page thin: XAML binds to observable state and commands, services do the work.
public partial class CleanerPageViewModel : ObservableObject
{
    // --- Services & fields --------------------------------------------------
    private readonly Winapp2Parser      _parser        = new();
    private readonly DetectionService   _detection     = new();
    private readonly CleaningService    _cleaner       = new();
    private readonly CustomEntryService _customService = new();

    private readonly List<ScanResult> _lastScan = [];       // hand-off between "Analyze" and "Clean"
    private List<CleanerEntry> _loadedEntries    = [];      // installed main-DB entries + all enabled custom entries
    private List<string> _lastPaths = [];                   // stored so Refresh can reload without paths re-passed
    private bool _suppressSave;                             // prevents N disk writes during bulk selection updates
    private bool _suppressMacroToggle;                      // prevents re-entrant loops when syncing macro-card switches
    private CancellationTokenSource? _cts;                  // lives only during an active scan or clean
    private string? _lastAddedExclusionRule;                // supports 1-click Undo Exclusion from InfoBar
    private static readonly bool IsElevated =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public DispatcherQueue? UiDispatcher { get; set; }

    // --- Observable state ---------------------------------------------------
    [ObservableProperty] public partial ObservableCollection<CleanerCategoryViewModel> Categories { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<ScanResultLine>           ResultLines { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<DetailLine>               DetailLines { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<MacroCategoryCardViewModel> MacroCategories { get; set; } = [];
    [ObservableProperty] public partial ScanResultLine?                                SelectedResultLine { get; set; }
    [ObservableProperty] public partial string  SearchText { get; set; } = "";
    [ObservableProperty] public partial string  FilterMode { get; set; } = "All"; // All | Selected | HasSize | InUse
    [ObservableProperty] public partial string  StatusText { get; set; } = ResourceService.Get("St_Loading");
    [ObservableProperty] public partial string  TotalSize  { get; set; } = "";
    [ObservableProperty] public partial long    TotalBytesScanned { get; set; }
    [ObservableProperty] public partial double  ScanProgress { get; set; }
    [ObservableProperty] public partial bool    IsBusy { get; set; }
    [ObservableProperty] public partial bool    IsHeroExpanded { get; set; } = AppSettings.Instance.CleanerHeroExpanded;
    [ObservableProperty] public partial bool    IsRuleSourceExpanded { get; set; } = true;
    [ObservableProperty] public partial int     TotalInstalledRules { get; set; }
    [ObservableProperty] public partial int     TotalDatabaseRules { get; set; }

    // Live 4-segment breakdown bytes for StorageDonutControl
    [ObservableProperty] public partial long WindowsBytes  { get; set; }
    [ObservableProperty] public partial long BrowsersBytes { get; set; }
    [ObservableProperty] public partial long DevAiBytes    { get; set; }
    [ObservableProperty] public partial long AppsBytes     { get; set; }

    // Real system drive telemetry (System.IO.DriveInfo)
    [ObservableProperty] public partial string SystemDriveLabel       { get; set; } = "System Drive";
    [ObservableProperty] public partial string SystemDriveUsageText   { get; set; } = "";
    [ObservableProperty] public partial double SystemDriveUsedPercent { get; set; }

    // Dismissible action feedback InfoBar (Clean outcome / Undo Exclusion)
    [ObservableProperty] public partial bool            BannerOpen        { get; set; }
    [ObservableProperty] public partial InfoBarSeverity BannerSeverity    { get; set; } = InfoBarSeverity.Informational;
    [ObservableProperty] public partial string          BannerTitle       { get; set; } = "";
    [ObservableProperty] public partial string          BannerMessage     { get; set; } = "";
    [ObservableProperty] public partial bool            CanUndoExclusion  { get; set; }
    [ObservableProperty] public partial bool            CanOfferElevation { get; set; }

    public string ProgressLabel => $"{ScanProgress:0}%";

    // --- Derived UI state ---------------------------------------------------
    public bool   IsEmpty           => Categories.Count == 0;
    public bool   IsNotEmpty        => Categories.Count > 0;
    public bool   HasSearchText     => !string.IsNullOrWhiteSpace(SearchText);
    public bool   IsShowingDetail   => SelectedResultLine is not null;
    public bool   IsShowingList     => SelectedResultLine is null && ResultLines.Count > 0;
    public bool   IsShowingReady    => SelectedResultLine is null && ResultLines.Count == 0;
    public bool   CanExportReport   => _lastScan.Count > 0;
    public string SelectedAppName   => SelectedResultLine?.AppName ?? "";
    public bool   CanRunCleaner     => !IsBusy && Categories.Count > 0;
    public bool   IsNotBusy         => !IsBusy;

    // Filter pill active states
    public bool IsFilterAll      => FilterMode == "All";
    public bool IsFilterSelected => FilterMode == "Selected";
    public bool IsFilterHasSize  => FilterMode == "HasSize";
    public bool IsFilterInUse    => FilterMode == "InUse";
    public bool IsFilterDanger   => FilterMode == "Danger";

    // Hero summary display
    public string HeroSizeDisplay => TotalBytesScanned > 0
        ? TotalSize
        : (IsBusy ? ProgressLabel : "Ready");

    public string HeroSubtitle => TotalBytesScanned > 0
        ? $"{_lastScan.Sum(r => r.FilesToDelete.Count + r.RegistryToDelete.Count):N0} items reclaimable"
        : (TotalInstalledRules > 0 ? $"{TotalInstalledRules} installed rules" : "Loading rules…");

    public string CleanButtonLabel => TotalBytesScanned > 0
        ? $"Clean {TotalSize}"
        : ResourceService.Get("BtnRunCleaner.Content") is var lbl && !string.IsNullOrWhiteSpace(lbl) && lbl != "BtnRunCleaner.Content"
            ? lbl
            : "Run Cleaner";

    public string LastCleanSummary
    {
        get
        {
            var history = AppSettings.Instance.CleanHistory;
            if (history.Count == 0) return "No cleans recorded yet";
            var last = history[^1];
            var age  = DateTime.Now - last.Date;
            string when = age.TotalMinutes < 2  ? "just now"
                        : age.TotalHours   < 1  ? $"{(int)age.TotalMinutes}m ago"
                        : age.TotalDays    < 1  ? $"{(int)age.TotalHours}h ago"
                        : age.TotalDays    < 7  ? $"{(int)age.TotalDays}d ago"
                        : last.Date.ToString("MMM d");
            return $"Last cleaned {when} · {ScanResult.FormatBytes(last.BytesFreed)}";
        }
    }

    public string LifetimeReclaimedText
    {
        get
        {
            var total = AppSettings.Instance.CleanHistory.Sum(h => h.BytesFreed);
            return total > 0 ? $"Lifetime reclaimed: {ScanResult.FormatBytes(total)}" : "Lifetime reclaimed: 0 B";
        }
    }

    public string ProtectedCookiesText => $"{AppSettings.Instance.CookieDomainsToKeep.Count} cookies protected";
    public string ExclusionsBadgeText  => AppSettings.Instance.GlobalExclusionsEnabled
        ? $"{AppSettings.Instance.GlobalExclusions.Count} exclusions active"
        : "Exclusions off";
    public string DatabaseBadgeText    => AppSettings.Instance.EnableWinapp3
        ? "Winapp2 + Winapp3 Active"
        : "Winapp2.ini Active";

    public string SelectedCountSummary
    {
        get
        {
            int sel = Categories.Sum(c => c.SelectedCount);
            int tot = Categories.Sum(c => c.TotalCount);
            return $"{sel}/{tot} selected";
        }
    }

    // Inspector metadata for the currently open result row
    public CleanerEntryViewModel? SelectedEntryVm =>
        SelectedResultLine is null
            ? null
            : Categories.SelectMany(c => c.Entries)
                        .FirstOrDefault(e => string.Equals(e.Name, SelectedResultLine.AppName, StringComparison.OrdinalIgnoreCase));

    public string SelectedEntryCategoryName => SelectedEntryVm?.CategoryName ?? SelectedResultLine?.CategoryName ?? "";
    public string SelectedEntrySizeText     => SelectedResultLine?.Size ?? "";
    public string SelectedEntryCountSummary => SelectedResultLine?.CountSummary ?? "";
    public bool   SelectedEntryHasWarning   => SelectedEntryVm?.HasWarning ?? false;
    public bool   SelectedEntryIsSafe       => !SelectedEntryHasWarning;
    public string SelectedEntryWarningText  => SelectedEntryVm?.Warning ?? "";
    public string SelectedEntryRawText      => SelectedEntryVm?.RawSourceText ?? "";
    public bool   SelectedEntryIsInUse         => SelectedEntryVm?.IsProcessRunning ?? false;
    public string SelectedEntryInUseText       => SelectedEntryVm?.RunningWarningText ?? "";
    public ImageSource? SelectedEntryAppIcon   => SelectedResultLine?.AppIconBitmap;
    public bool   SelectedEntryHasAppIcon      => SelectedResultLine?.HasAppIcon ?? false;
    public string SelectedEntryIconGlyph       => SelectedResultLine?.IconGlyph ?? "\uE71D";
    public string SelectedEntryAccentHex       => SelectedResultLine?.AccentColorHex ?? "#38BDF8";
    public string SelectedEntryIconBgHex       => SelectedResultLine?.IconBackgroundHex ?? "#2238BDF8";

    // --- Constructor --------------------------------------------------------

    public CleanerPageViewModel()
    {
        InitializeMacroCards();
        RefreshSystemDriveInfo();
        ResultLines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsShowingList));
            OnPropertyChanged(nameof(IsShowingReady));
            OnPropertyChanged(nameof(CanExportReport));
        };
    }

    private void InitializeMacroCards()
    {
        MacroCategories =
        [
            new("Windows",  "Windows & System", "\uE770", "#0078D4", SetMacroGroupSelected),
            new("Browsers", "Browsers",         "\uE774", "#38BDF8", SetMacroGroupSelected),
            new("DevAi",    "Developer & AI",   "\uE943", "#10B981", SetMacroGroupSelected),
            new("Apps",     "Apps & Utilities", "\uE74C", "#8B5CF6", SetMacroGroupSelected),
        ];
    }

    public void RefreshSystemDriveInfo()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0) return;

            long used = drive.TotalSize - drive.AvailableFreeSpace;
            SystemDriveLabel       = $"Local Disk ({root.TrimEnd('\\')})";
            SystemDriveUsageText   = $"{ScanResult.FormatBytes(drive.AvailableFreeSpace)} free of {ScanResult.FormatBytes(drive.TotalSize)}";
            SystemDriveUsedPercent = Math.Clamp(used * 100.0 / drive.TotalSize, 0, 100);
        }
        catch { }
    }

    public void ShowBanner(InfoBarSeverity severity, string title, string message, string? undoExclusionRule = null, bool offerElevation = false)
    {
        _lastAddedExclusionRule = undoExclusionRule;
        CanUndoExclusion        = !string.IsNullOrWhiteSpace(undoExclusionRule);
        CanOfferElevation       = offerElevation && !IsElevated;
        BannerSeverity          = severity;
        BannerTitle             = title;
        BannerMessage           = message;
        BannerOpen              = true;
    }

    [RelayCommand]
    private void UndoLastExclusion()
    {
        if (string.IsNullOrWhiteSpace(_lastAddedExclusionRule)) return;

        var settings = AppSettings.Instance;
        settings.GlobalExclusions.RemoveAll(r => string.Equals(r, _lastAddedExclusionRule, StringComparison.OrdinalIgnoreCase));
        settings.Save();

        StatusText              = $"Removed exclusion: {_lastAddedExclusionRule}";
        _lastAddedExclusionRule = null;
        CanUndoExclusion        = false;
        BannerOpen              = false;
        RefreshFooterMetadata();
    }

    // --- Audit Report Export (Markdown & CSV) -------------------------------

    public string BuildAuditReport(bool csvFormat)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        if (csvFormat)
        {
            sb.AppendLine("Timestamp,Category,Rule,ItemType,PathOrKey,EntryBytes");
            foreach (var r in _lastScan.OrderByDescending(x => x.TotalBytes))
            {
                var cat = CategoryResolver.TryMapLangSecRef(r.Entry).Name;
                foreach (var f in r.FilesToDelete)
                    sb.AppendLine($"\"{now:O}\",\"{EscapeCsv(cat)}\",\"{EscapeCsv(r.Entry.Name)}\",\"File\",\"{EscapeCsv(f)}\",{r.TotalBytes}");
                foreach (var reg in r.RegistryToDelete)
                    sb.AppendLine($"\"{now:O}\",\"{EscapeCsv(cat)}\",\"{EscapeCsv(r.Entry.Name)}\",\"Registry\",\"{EscapeCsv(reg.ToString())}\",{r.TotalBytes}");
            }
            return sb.ToString();
        }

        sb.AppendLine("# FluentCleaner Storage Audit Report");
        sb.AppendLine($"- **Generated:** {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **System Drive:** {SystemDriveLabel} ({SystemDriveUsageText})");
        sb.AppendLine($"- **Total Reclaimable:** {ScanResult.FormatBytes(_lastScan.Sum(r => r.TotalBytes))}");
        sb.AppendLine($"- **Matched Rules:** {_lastScan.Count} | **Files:** {_lastScan.Sum(r => r.FilesToDelete.Count):N0} | **Registry Keys:** {_lastScan.Sum(r => r.RegistryToDelete.Count):N0}");
        sb.AppendLine();
        sb.AppendLine("| Category | Rule | Files | Registry | Size |");
        sb.AppendLine("|---|---|---:|---:|---:|");
        foreach (var r in _lastScan.OrderByDescending(x => x.TotalBytes))
        {
            var cat = CategoryResolver.TryMapLangSecRef(r.Entry).Name;
            sb.AppendLine($"| {cat} | {r.Entry.Name} | {r.FilesToDelete.Count:N0} | {r.RegistryToDelete.Count:N0} | {r.FormattedSize} |");
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string s) => s.Replace("\"", "\"\"");

    // --- Property change hooks ----------------------------------------------

    partial void OnSelectedResultLineChanged(ScanResultLine? value)
    {
        OnPropertyChanged(nameof(IsShowingDetail));
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(IsShowingReady));
        OnPropertyChanged(nameof(SelectedAppName));
        OnPropertyChanged(nameof(SelectedEntryVm));
        OnPropertyChanged(nameof(SelectedEntryCategoryName));
        OnPropertyChanged(nameof(SelectedEntrySizeText));
        OnPropertyChanged(nameof(SelectedEntryCountSummary));
        OnPropertyChanged(nameof(SelectedEntryHasWarning));
        OnPropertyChanged(nameof(SelectedEntryIsSafe));
        OnPropertyChanged(nameof(SelectedEntryWarningText));
        OnPropertyChanged(nameof(SelectedEntryRawText));
        OnPropertyChanged(nameof(SelectedEntryIsInUse));
        OnPropertyChanged(nameof(SelectedEntryInUseText));
        OnPropertyChanged(nameof(SelectedEntryAppIcon));
        OnPropertyChanged(nameof(SelectedEntryHasAppIcon));
        OnPropertyChanged(nameof(SelectedEntryIconGlyph));
        OnPropertyChanged(nameof(SelectedEntryAccentHex));
        OnPropertyChanged(nameof(SelectedEntryIconBgHex));
        RebuildDetailLines(value);
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildVisibleCategories();
        if (!string.IsNullOrWhiteSpace(value)) SetAllExpanded(true);
        RefreshCategoryState();
        StatusText = Categories.Count > 0
            ? ResourceService.Fmt("St_SearchFound", CountVisibleEntries())
            : ResourceService.Get("St_SearchNone");
        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnFilterModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterSelected));
        OnPropertyChanged(nameof(IsFilterHasSize));
        OnPropertyChanged(nameof(IsFilterInUse));
        OnPropertyChanged(nameof(IsFilterDanger));
        RebuildVisibleCategories();
        if (value != "All") SetAllExpanded(true);
        RefreshCategoryState();
    }

    partial void OnIsHeroExpandedChanged(bool value)
    {
        AppSettings.Instance.CleanerHeroExpanded = value;
        AppSettings.Instance.Save();
    }

    partial void OnScanProgressChanged(double value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(HeroSizeDisplay));
    }

    partial void OnTotalSizeChanged(string value)
    {
        OnPropertyChanged(nameof(HeroSizeDisplay));
        OnPropertyChanged(nameof(CleanButtonLabel));
    }

    partial void OnTotalBytesScannedChanged(long value)
    {
        OnPropertyChanged(nameof(HeroSizeDisplay));
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(CleanButtonLabel));
    }

    partial void OnIsBusyChanged(bool value)
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunCleaner));
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(HeroSizeDisplay));
    }

    // --- Commands -----------------------------------------------------------
    [RelayCommand] private void Cancel()               => _cts?.Cancel();
    [RelayCommand] private void SelectAll()            => SetAllSelected(true);
    [RelayCommand] private void SelectNone()           => SetAllSelected(false);
    [RelayCommand] private void SelectDefaults()       => SetAllDefaults();
    [RelayCommand] private void ExpandAll()            => SetAllExpanded(true);
    [RelayCommand] private void CollapseAll()          => SetAllExpanded(false);
    [RelayCommand] private void SortResultsDesc()      => SortResultLinesBySize(descending: true);
    [RelayCommand] private void SortResultsAsc()       => SortResultLinesBySize(descending: false);
    [RelayCommand] private void ClearDetail()          => SelectedResultLine = null;
    [RelayCommand] private void ToggleHero()           => IsHeroExpanded = !IsHeroExpanded;
    [RelayCommand] private void ToggleRuleSource()     => IsRuleSourceExpanded = !IsRuleSourceExpanded;
    [RelayCommand] private void SetFilter(string mode) => FilterMode = mode;

    [RelayCommand]
    private async Task CleanSelected()
    {
        var entry = SelectedEntryVm;
        if (entry is not null) await CleanSingleEntryAsync(entry);
    }

    // --- Load ---------------------------------------------------------------
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await LoadWinapp2Async(_lastPaths);
    private bool CanRefresh() => !IsBusy && _lastPaths.Count > 0;

    public async Task LoadWinapp2Async(IList<string> filePaths)
    {
        _lastPaths = [.. filePaths];
        IsBusy     = true;
        StatusText = ResourceService.Get("St_ParsingDatabases");

        var allEntries = new List<CleanerEntry>();
        foreach (var path in filePaths)
            allEntries.AddRange(await _parser.ParseFileAsync(path));

        allEntries = allEntries.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        TotalDatabaseRules = allEntries.Count;

        _loadedEntries = await Task.Run(() => allEntries.Where(_detection.IsInstalled).ToList());

        _loadedEntries.RemoveAll(e => e.IsCustom);
        _loadedEntries.AddRange(await _customService.LoadEnabledEntriesAsync());
        TotalInstalledRules = _loadedEntries.Count;

        RebuildVisibleCategories();
        RefreshFooterMetadata();
        RefreshSystemDriveInfo();

        StatusText = ResourceService.Fmt("St_AnalysisReady", _loadedEntries.Count, allEntries.Count);
        RefreshCategoryState();
        IsBusy = false;
    }

    public async Task RefreshCustomEntriesAsync()
    {
        if (IsBusy) return;
        _loadedEntries.RemoveAll(e => e.IsCustom);
        _loadedEntries.AddRange(await _customService.LoadEnabledEntriesAsync());
        TotalInstalledRules = _loadedEntries.Count;
        AppIconResolverService.InvalidateMissCache();
        RebuildVisibleCategories();
        RefreshFooterMetadata();
        RefreshCategoryState();
    }

    public void RefreshFooterMetadata()
    {
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(LastCleanSummary));
        OnPropertyChanged(nameof(LifetimeReclaimedText));
        OnPropertyChanged(nameof(ProtectedCookiesText));
        OnPropertyChanged(nameof(ExclusionsBadgeText));
        OnPropertyChanged(nameof(DatabaseBadgeText));
    }

    // --- Analyze & Clean ----------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        var selected = GetSelectedEntries();
        if (selected.Count == 0) { StatusText = ResourceService.Get("St_NothingSelected"); return; }

        BeginResultsRun(ResourceService.Get("St_Scanning"));

        using var cts = new CancellationTokenSource();
        _cts = cts;

        try
        {
            long totalBytes = 0; int totalFiles = 0; int totalReg = 0; int done = 0;
            int total = selected.Count;
            var progress = new Progress<string>(msg => StatusText = msg);

            foreach (var entry in selected)
            {
                var result = await AnalyzeEntryInternalAsync(entry, progress, keepDetailSelection: false, cts.Token);
                totalBytes += result.TotalBytes;
                totalFiles += result.FilesToDelete.Count;
                totalReg   += result.RegistryToDelete.Count;
                TotalBytesScanned = totalBytes;
                TotalSize         = ScanResult.FormatBytes(totalBytes);
                ScanProgress      = ++done * 100.0 / total;
            }
            SortResultLinesBySize(descending: true);
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Fmt("St_ScanComplete", totalFiles, totalReg, TotalSize);
            OnPropertyChanged(nameof(CanExportReport));
        }
        catch (OperationCanceledException)
        {
            SortResultLinesBySize(descending: true);
            RefreshAllCategoryAndMacroStats();
            StatusText        = ResourceService.Get("St_ScanCancelled");
            TotalSize         = "";
            TotalBytesScanned = 0;
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

    private bool CanAnalyze() => !IsBusy && Categories.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task RunCleanerAsync()
    {
        if (_lastScan.Count == 0)
            await AnalyzeAsync();

        if (_lastScan.Count == 0) return;

        IsBusy             = true;
        SelectedResultLine = null;
        ScanProgress       = 0;
        StatusText         = ResourceService.Get("St_Cleaning");

        using var cts = new CancellationTokenSource();
        _cts = cts;

        long freedBytes = 0;
        int  removed    = 0;
        try
        {
            var scannedBytes      = _lastScan.Sum(r => r.TotalBytes);
            var freedBreakdown    = new List<(CleanerEntry Entry, int Files, int Regs, int RequestedItems, int CleanedCount, long Bytes)>();
            (removed, freedBytes) = await CleanResultsWithBreakdownAsync(_lastScan.ToList(), freedBreakdown, new Progress<string>(msg => StatusText = msg), cts.Token);
            var skippedBytes      = Math.Max(0, scannedBytes - freedBytes);
            var requestedItems    = freedBreakdown.Sum(x => x.RequestedItems);
            var skippedItems      = Math.Max(0, requestedItems - removed);
            var hasSkipped        = skippedBytes > 0 || skippedItems > 0;

            _lastScan.Clear();
            ResultLines.Clear();
            foreach (var item in freedBreakdown.Where(x => x.CleanedCount > 0 || x.Bytes > 0).OrderByDescending(x => x.Bytes).ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                var catName = CategoryResolver.TryMapLangSecRef(item.Entry).Name;
                var macro   = ResolveMacroGroup(catName);
                var st      = ResolveCategoryStyling(catName, macro);
                var bmp     = TryGetCategoryBitmap(catName, st.AppIconSource);
                int freedFiles = Math.Min(item.Files, item.CleanedCount);
                int freedRegs  = Math.Max(0, Math.Min(item.Regs, item.CleanedCount - freedFiles));
                ResultLines.Add(new ScanResultLine(
                    item.Entry.Name,
                    freedFiles,
                    freedRegs,
                    $"Freed {ScanResult.FormatBytes(item.Bytes)}",
                    null,
                    catName,
                    bmp,
                    st.Glyph,
                    st.AccentHex,
                    st.BackgroundHex,
                    isFreedSummary: true,
                    sortBytes: item.Bytes));
            }
            ClearAllEntrySizes();

            TotalSize         = "";
            TotalBytesScanned = 0;
            RefreshAllCategoryAndMacroStats();
            RefreshSystemDriveInfo();

            StatusText = skippedBytes > 0
                ? ResourceService.Fmt("St_CleanDone", ScanResult.FormatBytes(freedBytes), ScanResult.FormatBytes(skippedBytes))
                : ResourceService.Fmt("St_CleanDoneSimple", removed, ScanResult.FormatBytes(freedBytes));

            var skippedNote = skippedBytes > 0
                ? $" ({ScanResult.FormatBytes(skippedBytes)} skipped due to locks or permissions.)"
                : skippedItems > 0
                    ? $" ({skippedItems:N0} items skipped due to locks or permissions.)"
                    : "";

            ShowBanner(
                hasSkipped && !IsElevated ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                "Clean Complete",
                $"Removed {removed:N0} items and reclaimed {ScanResult.FormatBytes(freedBytes)}.{skippedNote}",
                offerElevation: hasSkipped);
        }
        catch (OperationCanceledException)
        {
            UpdateTotalsFromLastScan();
            RefreshAllCategoryAndMacroStats();
            StatusText = freedBytes > 0
                ? ResourceService.Fmt("St_CleanCancelledBytes", ScanResult.FormatBytes(freedBytes))
                : ResourceService.Get("St_CleanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }

        if (AppSettings.Instance.CleanHistoryEnabled && freedBytes > 0)
        {
            var history = AppSettings.Instance.CleanHistory;
            history.Add(new CleanHistoryEntry(DateTime.Now, freedBytes, removed));
            while (history.Count > 50) history.RemoveAt(0);
            AppSettings.Instance.Save();
            RefreshFooterMetadata();
        }

        await RunPostCleanTasksAsync();
    }

    private static async Task RunPostCleanTasksAsync()
    {
        if (!AppSettings.Instance.PostCleanEnabled) return;

        var lines = AppSettings.Instance.PostCleanCommands
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0) return;

        foreach (var line in lines)
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "cmd.exe",
                    Arguments       = $"/c {line}",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });

                if (process is not null)
                    await process.WaitForExitAsync();
            }
            catch { }
        }
    }

    private bool CanClean() => !IsBusy && Categories.Count > 0;

    // --- Single entry -------------------------------------------------------

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

            SortResultLinesBySize(descending: true);
            UpdateTotalsFromLastScan();
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Fmt("St_EntryScanned", entryVm.Name, result.FilesToDelete.Count, result.RegistryToDelete.Count);
        }
        catch (OperationCanceledException)
        {
            SortResultLinesBySize(descending: true);
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Get("St_ScanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

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

            var (removed, freedBytes) = await _cleaner.CleanAsync(result, new Progress<string>(msg => StatusText = msg), cts.Token);
            RemoveScanResult(result);
            entryVm.SizeText     = "";
            entryVm.ScannedBytes = 0;

            UpdateTotalsFromLastScan();
            RefreshAllCategoryAndMacroStats();
            RefreshSystemDriveInfo();
            StatusText = ResourceService.Fmt("St_EntryCleanDone", entryVm.Name, removed, ScanResult.FormatBytes(freedBytes));
            ShowBanner(InfoBarSeverity.Success, entryVm.Name, $"Cleaned {removed:N0} items · Freed {ScanResult.FormatBytes(freedBytes)}.");
        }
        catch (OperationCanceledException)
        {
            UpdateTotalsFromLastScan();
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Get("St_CleanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

    // --- Category -----------------------------------------------------------

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
            var progress = new Progress<string>(msg => StatusText = msg);

            foreach (var entryVm in selected)
            {
                await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false, cts.Token);
                ScanProgress = total > 0 ? ++done * 100.0 / total : 100;
            }

            SortResultLinesBySize(descending: true);
            UpdateTotalsFromLastScan();
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Fmt("St_CategoryScanned", categoryVm.Name, selected.Count);
        }
        catch (OperationCanceledException)
        {
            SortResultLinesBySize(descending: true);
            RefreshAllCategoryAndMacroStats();
            StatusText = ResourceService.Get("St_ScanCancelled");
        }
        finally
        {
            _cts   = null;
            IsBusy = false;
        }
    }

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

        var results               = _lastScan.Where(r => selected.Any(vm => vm.Entry == r.Entry)).ToList();
        var (removed, freedBytes) = await CleanResultsAsync(results, progress);

        UpdateTotalsFromLastScan();
        RefreshAllCategoryAndMacroStats();
        RefreshSystemDriveInfo();
        StatusText = ResourceService.Fmt("St_CategoryCleanDone", categoryVm.Name, removed, ScanResult.FormatBytes(freedBytes));
        ShowBanner(InfoBarSeverity.Success, categoryVm.Name, $"Cleaned {removed:N0} items · Freed {ScanResult.FormatBytes(freedBytes)}.");
        IsBusy     = false;
    }

    // --- Warnings -----------------------------------------------------------

    public IReadOnlyList<string> GetWarningsForSelectedEntries() =>
        BuildWarnings(Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected));

    public IReadOnlyList<string> GetWarningsForEntry(CleanerEntryViewModel entryVm) =>
        BuildWarnings([entryVm]);

    public IReadOnlyList<string> GetWarningsForCategory(CleanerCategoryViewModel categoryVm) =>
        BuildWarnings(categoryVm.Entries.Where(e => e.IsSelected));

    private static IReadOnlyList<string> BuildWarnings(IEnumerable<CleanerEntryViewModel> entries) =>
        entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Warning))
            .Select(e => $"{e.Name}{Environment.NewLine}{e.Warning}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // --- Macro-category classification & icons ------------------------------

    private static string NormalizeMacroLookupKey(string categoryName) =>
        categoryName.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase)
            ? categoryName["Dangerous ".Length..]
            : categoryName;

    private static string ResolveMacroGroup(string categoryName) => NormalizeMacroLookupKey(categoryName) switch
    {
        "Windows" or "Microsoft Store" => "Windows",
        "Google Chrome" or "Microsoft Edge" or "Firefox" or "Opera" or "Opera GX"
            or "Brave" or "Vivaldi" or "Arc Browser" or "Safari" or "Internet"
            or "Avast Secure Browser" or "AVG Secure Browser"
            or "Norton Private Browser" or "Avira Secure Browser" => "Browsers",
        "Developer Tools" or "Developer & AI" => "DevAi",
        _ => "Apps"
    };

    private static string ResolveCategoryIcon(string macroGroup) => macroGroup switch
    {
        "Windows"  => "\uE770",
        "Browsers" => "\uE774",
        "DevAi"    => "\uE943",
        _          => "\uE74C"
    };

    private static CategoryStyle ResolveCategoryStyling(string categoryName, string macroGroup) =>
        CategoryStylingResolver.Resolve(categoryName, macroGroup);

    private void SetMacroGroupSelected(string macroKey, bool isSelected)
    {
        if (_suppressMacroToggle) return;

        _suppressSave = true;
        foreach (var cat in Categories.Where(c => c.MacroGroup == macroKey))
        {
            foreach (var entry in cat.Entries)
                entry.IsSelected = isSelected;
            cat.RefreshStats();
        }
        _suppressSave = false;
        SaveSelection();
        RefreshAllCategoryAndMacroStats();
    }

    private void SetCategoryBulkSelected(CleanerCategoryViewModel cat, bool isSelected)
    {
        _suppressSave = true;
        foreach (var entry in cat.Entries)
            entry.IsSelected = isSelected;
        _suppressSave = false;
        cat.RefreshStats();
        SaveSelection();
        RefreshMacroCardsOnly();
        OnPropertyChanged(nameof(SelectedCountSummary));
    }

    // --- Private helpers ----------------------------------------------------

    private void SortResultLinesBySize(bool descending = true)
    {
        var sorted = descending
            ? ResultLines.OrderByDescending(l => l.Result?.TotalBytes ?? l.SortBytes)
                         .ThenBy(l => l.AppName, StringComparer.OrdinalIgnoreCase)
                         .ToList()
            : ResultLines.OrderBy(l => l.Result?.TotalBytes ?? l.SortBytes)
                         .ThenBy(l => l.AppName, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int from = ResultLines.IndexOf(sorted[i]);
            if (from != i) ResultLines.Move(from, i);
        }
    }

    private void RebuildVisibleCategories()
    {
        var query = SearchText.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? _loadedEntries
            : _loadedEntries.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                CategoryResolver.TryMapLangSecRef(e).Name.Contains(query, StringComparison.OrdinalIgnoreCase))
              .ToList();

        RebuildCategories(visible);
    }

    private void RebuildCategories(List<CleanerEntry> entries)
    {
        var expandedNames = Categories.Where(c => c.IsExpanded)
                                      .Select(c => c.Name)
                                      .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scanLookup = new Dictionary<string, ScanResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _lastScan)
            scanLookup[r.Entry.Name] = r;

        Categories.Clear();

        var groups = entries
            .Select(e => new { Entry = e, Category = CategoryResolver.TryMapLangSecRef(e) })
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase);

        var settings = AppSettings.Instance;
        var saved    = settings.SelectedEntries;

        foreach (var group in groups)
        {
            bool isDangerousCat = group.Key.Name.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase);
            if (FilterMode == "Danger" && !isDangerousCat) continue;

            var macroGroup  = ResolveMacroGroup(group.Key.Name);
            var style       = ResolveCategoryStyling(group.Key.Name, macroGroup);
            var initialIcon = AppIconResolverService.TryGetBundledBitmap(group.Key.Name, style.AppIconSource)
                           ?? AppIconResolverService.TryGetCached(group.Key.Name);

            var catVm = new CleanerCategoryViewModel(
                group.Key.Name,
                macroGroup,
                style.Glyph,
                style.AccentHex,
                style.BackgroundHex,
                initialIcon,
                SetCategoryBulkSelected)
            {
                IsExpanded = expandedNames.Contains(group.Key.Name)
            };

            var groupEntries = group.Select(x => x.Entry).ToList();
            if (catVm.AppIconBitmap is null && UiDispatcher is { } dq)
            {
                string resolvedCatName = group.Key.Name;
                _ = AppIconResolverService.TryResolveDynamicAppIconAsync(
                    resolvedCatName,
                    groupEntries,
                    dq,
                    img =>
                    {
                        catVm.AppIconBitmap = img;
                        UpdateResultLineIcons(resolvedCatName, img);
                    });
            }

            foreach (var item in group.OrderBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entryVm = new CleanerEntryViewModel(item.Entry, group.Key.Name)
                {
                    IsSelected = settings.HasSavedCleanerSelection
                        ? saved.Contains(item.Entry.Name)
                        : (!isDangerousCat && item.Entry.Default)
                };

                if (scanLookup.TryGetValue(item.Entry.Name, out var existingScan))
                {
                    entryVm.ScannedBytes = existingScan.TotalBytes;
                    entryVm.SizeText     = existingScan.FormattedSize;
                }

                bool include = FilterMode switch
                {
                    "Selected" => entryVm.IsSelected,
                    "HasSize"  => entryVm.ScannedBytes > 0,
                    "Danger"   => isDangerousCat,
                    _          => true
                };
                if (!include) continue;

                entryVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanerEntryViewModel.IsSelected))
                    {
                        SaveSelection(entryVm);
                        catVm.RefreshStats();
                        RefreshMacroCardsOnly();
                        OnPropertyChanged(nameof(SelectedCountSummary));
                    }
                };

                catVm.Entries.Add(entryVm);
            }

            if (catVm.Entries.Count > 0)
            {
                catVm.RefreshStats();
                Categories.Add(catVm);
            }
        }

        RefreshMacroCardsOnly();
        _ = RefreshRunningProcessesAsync();
    }

    public async Task RefreshRunningProcessesAsync()
    {
        try
        {
            var snapshot = await ProcessDetectionService.GetRunningProcessesAsync();
            foreach (var category in Categories.ToList())
            {
                foreach (var entryVm in category.Entries.ToList())
                {
                    bool isRunning = ProcessDetectionService.IsProcessRunning(entryVm.Entry, out var procName, snapshot);
                    entryVm.IsProcessRunning   = isRunning;
                    entryVm.RunningProcessName = procName;
                    entryVm.RunningWarningText = isRunning
                        ? ResourceService.Fmt("St_ProcessRunningWarning", procName)
                        : string.Empty;

                    if (FilterMode == "InUse" && !isRunning)
                        category.Entries.Remove(entryVm);
                }

                category.RefreshStats();
                if (FilterMode == "InUse" && category.Entries.Count == 0)
                    Categories.Remove(category);
            }
            RefreshCategoryState();
        }
        catch { }
    }

    private void RefreshAllCategoryAndMacroStats()
    {
        foreach (var cat in Categories)
            cat.RefreshStats();

        RefreshMacroCardsOnly();
        OnPropertyChanged(nameof(SelectedCountSummary));
    }

    private void RefreshMacroCardsOnly()
    {
        _suppressMacroToggle = true;
        long grandTotal = Math.Max(1, Categories.Sum(c => c.ScannedBytes));

        long winBytes = 0, brwBytes = 0, devBytes = 0, appBytes = 0;

        foreach (var card in MacroCategories)
        {
            var matchingCats = Categories.Where(c => c.MacroGroup == card.Key).ToList();
            int totalEntries = matchingCats.Sum(c => c.TotalCount);
            int selEntries   = matchingCats.Sum(c => c.SelectedCount);
            long bytes       = matchingCats.Sum(c => c.ScannedBytes);

            switch (card.Key)
            {
                case "Windows":  winBytes = bytes; break;
                case "Browsers": brwBytes = bytes; break;
                case "DevAi":    devBytes = bytes; break;
                default:         appBytes = bytes; break;
            }

            card.IsEnabled    = selEntries > 0;
            card.CountSummary = $"{selEntries}/{totalEntries} rules";
            card.SizeText     = bytes > 0 ? ScanResult.FormatBytes(bytes) : "0 B";
            card.SharePercent = bytes > 0 ? Math.Clamp(bytes * 100.0 / grandTotal, 4.0, 100.0) : 0.0;
        }

        WindowsBytes  = winBytes;
        BrowsersBytes = brwBytes;
        DevAiBytes    = devBytes;
        AppsBytes     = appBytes;

        _suppressMacroToggle = false;
    }

    private void RefreshCategoryState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNotEmpty));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(CanRunCleaner));
        OnPropertyChanged(nameof(SelectedCountSummary));
        AnalyzeCommand.NotifyCanExecuteChanged();
        RunCleanerCommand.NotifyCanExecuteChanged();
    }

    private void BeginResultsRun(string status)
    {
        IsBusy             = true;
        BannerOpen         = false;
        SelectedResultLine = null;
        ResultLines.Clear();
        _lastScan.Clear();
        ClearAllEntrySizes();
        TotalBytesScanned = 0;
        WindowsBytes = BrowsersBytes = DevAiBytes = AppsBytes = 0;
        TotalSize         = "0 B";
        ScanProgress      = 0;
        StatusText        = status;
    }

    private List<CleanerEntry> GetSelectedEntries() =>
        Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected).Select(e => e.Entry).ToList();

    private int CountVisibleEntries() =>
        Categories.Sum(c => c.Entries.Count);

    // --- Engine -------------------------------------------------------------

    private ImageSource? TryGetCategoryBitmap(string categoryName, string? bundledIconSource) =>
        AppIconResolverService.TryGetCached(categoryName)
        ?? Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase))?.AppIconBitmap
        ?? AppIconResolverService.TryGetBundledBitmap(categoryName, bundledIconSource);

    private void UpdateResultLineIcons(string categoryName, ImageSource img)
    {
        foreach (var line in ResultLines)
        {
            if (string.Equals(line.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase)
                && line.AppIconBitmap is null)
            {
                line.AppIconBitmap = img;
            }
        }
        if (SelectedResultLine is { } selected
            && string.Equals(selected.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(SelectedEntryAppIcon));
            OnPropertyChanged(nameof(SelectedEntryHasAppIcon));
        }
    }

    private async Task<ScanResult> AnalyzeEntryInternalAsync(CleanerEntry entry, IProgress<string> progress, bool keepDetailSelection, CancellationToken token = default)
    {
        RemoveScanResult(entry);

        var result = await _cleaner.AnalyzeAsync(entry, progress, token);
        _lastScan.Add(result);
        UpdateEntrySize(entry, result.TotalBytes, result.FormattedSize);

        ScanResultLine? line = null;
        if (result.FilesToDelete.Count > 0 || result.RegistryToDelete.Count > 0)
        {
            var catName = CategoryResolver.TryMapLangSecRef(entry).Name;
            var macro   = ResolveMacroGroup(catName);
            var st      = ResolveCategoryStyling(catName, macro);
            var bmp     = TryGetCategoryBitmap(catName, st.AppIconSource);
            line = new ScanResultLine(
                entry.Name,
                result.FilesToDelete.Count,
                result.RegistryToDelete.Count,
                result.FormattedSize,
                result,
                catName,
                bmp,
                st.Glyph,
                st.AccentHex,
                st.BackgroundHex,
                sortBytes: result.TotalBytes);
            ResultLines.Add(line);
        }

        if (keepDetailSelection && line is not null)
            SelectedResultLine = line;

        return result;
    }

    private async Task<ScanResult> EnsureEntryScanAsync(CleanerEntryViewModel entryVm, IProgress<string> progress)
    {
        var existing = _lastScan.FirstOrDefault(r => r.Entry == entryVm.Entry);
        if (existing is not null) return existing;

        StatusText = ResourceService.Fmt("St_EntryScanningProgress", entryVm.Name);
        return await AnalyzeEntryInternalAsync(entryVm.Entry, progress, keepDetailSelection: false);
    }

    private async Task<(int count, long bytes)> CleanResultsWithBreakdownAsync(
        List<ScanResult> results,
        List<(CleanerEntry Entry, int Files, int Regs, int RequestedItems, int CleanedCount, long Bytes)> breakdown,
        IProgress<string> progress,
        CancellationToken token = default)
    {
        int  count = 0;
        long bytes = 0;
        int  done  = 0;
        int  total = results.Count;
        foreach (var result in results)
        {
            int files = result.FilesToDelete.Count;
            int regs  = result.RegistryToDelete.Count;
            var (c, b) = await _cleaner.CleanAsync(result, progress, token);
            count += c;
            bytes += b;
            breakdown.Add((result.Entry, files, regs, files + regs, c, b));
            ScanProgress = total > 0 ? ++done * 100.0 / total : 100;
            RemoveScanResult(result);
            UpdateEntrySize(result.Entry, 0, "");
        }
        return (count, bytes);
    }

    private async Task<(int count, long bytes)> CleanResultsAsync(List<ScanResult> results, IProgress<string> progress, CancellationToken token = default)
    {
        int  count = 0;
        long bytes = 0;
        int  done  = 0;
        int  total = results.Count;
        foreach (var result in results)
        {
            var (c, b) = await _cleaner.CleanAsync(result, progress, token);
            count += c;
            bytes += b;
            ScanProgress = total > 0 ? ++done * 100.0 / total : 100;
            RemoveScanResult(result);
            UpdateEntrySize(result.Entry, 0, "");
        }
        return (count, bytes);
    }

    private void RemoveScanResult(CleanerEntry entry)
    {
        var existing = _lastScan.FirstOrDefault(r => r.Entry == entry);
        if (existing is not null) RemoveScanResult(existing);
    }

    private void RemoveScanResult(ScanResult result)
    {
        _lastScan.Remove(result);
        var line = ResultLines.FirstOrDefault(l => l.Result == result);
        if (line is not null) ResultLines.Remove(line);
    }

    private void UpdateEntrySize(CleanerEntry entry, long bytes, string sizeText)
    {
        var vm = Categories.SelectMany(c => c.Entries).FirstOrDefault(e => e.Entry == entry);
        if (vm is not null)
        {
            vm.ScannedBytes = bytes;
            vm.SizeText     = sizeText;
        }
    }

    private void ClearAllEntrySizes()
    {
        foreach (var entry in Categories.SelectMany(c => c.Entries))
        {
            entry.ScannedBytes = 0;
            entry.SizeText     = "";
        }
    }

    private void UpdateTotalsFromLastScan()
    {
        var sum = _lastScan.Sum(r => r.TotalBytes);
        TotalBytesScanned = sum;
        TotalSize         = _lastScan.Count > 0 ? ScanResult.FormatBytes(sum) : "";
        OnPropertyChanged(nameof(CanExportReport));
    }

    private void RebuildDetailLines(ScanResultLine? line)
    {
        DetailLines.Clear();
        if (line?.Result is not { } result) return;

        AddDetailGroup(ResourceService.Get("SuffixFiles"),    result.FilesToDelete, isRegistry: false);
        AddDetailGroup(ResourceService.Get("SuffixRegistry"), result.RegistryToDelete.Select(r => r.ToString()), isRegistry: true);
    }

    private void AddDetailGroup(string title, IEnumerable<string> lines, bool isRegistry)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        if (title.Length > 0) title = char.ToUpper(title[0]) + title[1..];
        DetailLines.Add(new DetailLine($"{title} ({list.Count})", IsHeader: true, IsRegistry: isRegistry));
        foreach (var line in list)
            DetailLines.Add(new DetailLine(line, IsHeader: false, IsRegistry: isRegistry));
    }

    private void SetAllSelected(bool value)
    {
        _suppressSave = true;
        foreach (var cat in Categories)
        {
            foreach (var entry in cat.Entries)
                entry.IsSelected = value;
            cat.RefreshStats();
        }
        _suppressSave = false;
        SaveSelection();
        RefreshMacroCardsOnly();
        OnPropertyChanged(nameof(SelectedCountSummary));
    }

    private static bool IsDangerousName(CleanerEntry entry) =>
        CategoryResolver.TryMapLangSecRef(entry).Name.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase);

    private void SetAllDefaults()
    {
        _suppressSave = true;
        foreach (var cat in Categories)
        {
            foreach (var entry in cat.Entries)
                entry.IsSelected = !IsDangerousName(entry.Entry) && entry.Entry.Default;
            cat.RefreshStats();
        }
        _suppressSave = false;
        SaveSelection();
        RefreshMacroCardsOnly();
        OnPropertyChanged(nameof(SelectedCountSummary));
    }

    private void SetAllExpanded(bool value)
    {
        foreach (var cat in Categories)
            cat.IsExpanded = value;
    }

    private void SaveSelection()
    {
        if (_suppressSave) return;
        var visibleEntries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Categories.SelectMany(c => c.Entries))
            visibleEntries[entry.Name] = entry.IsSelected;

        var baseline = AppSettings.Instance.HasSavedCleanerSelection
            ? AppSettings.Instance.SelectedEntries
            : _loadedEntries.Where(e => e.Default && !IsDangerousName(e)).Select(e => e.Name);

        var hiddenSaved = baseline.Where(name => !visibleEntries.ContainsKey(name));
        AppSettings.Instance.SelectedEntries = visibleEntries
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .Concat(hiddenSaved)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppSettings.Instance.CleanerSelectionInitialized = true;
        AppSettings.Instance.Save();
    }

    private void SaveSelection(CleanerEntryViewModel entry)
    {
        if (_suppressSave) return;

        var selected = AppSettings.Instance.HasSavedCleanerSelection
            ? AppSettings.Instance.SelectedEntries.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _loadedEntries.Where(e => e.Default && !IsDangerousName(e)).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (entry.IsSelected) selected.Add(entry.Name);
        else                  selected.Remove(entry.Name);

        AppSettings.Instance.SelectedEntries = selected;
        AppSettings.Instance.CleanerSelectionInitialized = true;
        AppSettings.Instance.Save();
    }

    // --- Compatibility with FluentSeed & Upstream Extensions ---
    public Task Ready => Task.CompletedTask;
    public IReadOnlyList<CleanerEntry> LoadedEntries => _loadedEntries;

    public static List<string> DatabasePaths()
    {
        var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
        if (paths.Count == 0) paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));
        return paths;
    }

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
}

public partial class MacroCategoryCardViewModel : ObservableObject
{
    private readonly Action<string, bool> _onToggled;

    public string Key           { get; }
    public string Title         { get; }
    public string IconGlyph     { get; }
    public string AccentHex     { get; }
    public string BackgroundHex => AccentHex switch
    {
        "#0078D4" => "#1F0078D4",
        "#38BDF8" => "#1F0284C7",
        "#10B981" => "#1F10B981",
        "#8B5CF6" => "#1F8B5CF6",
        _         => "#1A888888"
    };

    [ObservableProperty] public partial bool   IsEnabled    { get; set; } = true;
    [ObservableProperty] public partial string SizeText     { get; set; } = "0 B";
    [ObservableProperty] public partial string CountSummary { get; set; } = "0/0 rules";
    [ObservableProperty] public partial double SharePercent { get; set; }

    partial void OnIsEnabledChanged(bool value) => _onToggled(Key, value);

    public MacroCategoryCardViewModel(string key, string title, string iconGlyph, string accentHex, Action<string, bool> onToggled)
    {
        Key        = key;
        Title      = title;
        IconGlyph  = iconGlyph;
        AccentHex  = accentHex;
        _onToggled = onToggled;
    }
}

public partial class ScanResultLine : ObservableObject
{
    public string AppName { get; }
    public int FileCount { get; }
    public int RegCount { get; }
    public string Size { get; }
    public ScanResult? Result { get; }
    public string CategoryName { get; }
    public string IconGlyph { get; }
    public string AccentColorHex { get; }
    public string IconBackgroundHex { get; }
    public bool IsFreedSummary { get; }
    public long SortBytes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppIcon))]
    public partial ImageSource? AppIconBitmap { get; set; }

    public bool HasAppIcon => AppIconBitmap is not null;
    public bool ShowChevron => Result is not null;

    public ScanResultLine(
        string appName,
        int fileCount,
        int regCount,
        string size,
        ScanResult? result = null,
        string categoryName = "",
        ImageSource? appIconBitmap = null,
        string iconGlyph = "\uE71D",
        string accentColorHex = "#38BDF8",
        string iconBackgroundHex = "#2238BDF8",
        bool isFreedSummary = false,
        long sortBytes = 0)
    {
        AppName           = appName;
        FileCount         = fileCount;
        RegCount          = regCount;
        Size              = size;
        Result            = result;
        CategoryName      = categoryName;
        AppIconBitmap     = appIconBitmap;
        IconGlyph         = iconGlyph;
        AccentColorHex    = accentColorHex;
        IconBackgroundHex = iconBackgroundHex;
        IsFreedSummary    = isFreedSummary;
        SortBytes         = sortBytes;
    }

    public string CountSummary
    {
        get
        {
            var parts = new List<string>();
            if (FileCount > 0) parts.Add($"{FileCount:N0} {ResourceService.Get("SuffixFiles")}");
            if (RegCount  > 0) parts.Add($"{RegCount:N0} {ResourceService.Get("SuffixRegistry")}");
            return string.Join(" · ", parts);
        }
    }

    public string Summary => FileCount > 0 || RegCount > 0
        ? $"{FileCount:N0} {ResourceService.Get("SuffixFilesSingular")}, {RegCount:N0} {ResourceService.Get("SuffixRegistryItems")}  {Size}"
        : ResourceService.Get("LabelCleaningComplete");
}

public record DetailLine(string Text, bool IsHeader, bool IsRegistry = false)
{
    public bool   IsNotHeader     => !IsHeader;
    public bool   CanExcludeFile  => !IsHeader && !IsRegistry;
    public string IconGlyph       => IsRegistry ? "\uE74C" : "\uE8A5";
}
