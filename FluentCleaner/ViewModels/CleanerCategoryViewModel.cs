using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using FluentCleaner.Models;
using FluentCleaner.Services;

namespace FluentCleaner.ViewModels;

// Groups cleaner entries under a shared label (e.g. "Google Chrome", "Developer Tools").
// Surfaces live selection counts, 3-state category checkbox, rolled-up size, and bulk actions.
public partial class CleanerCategoryViewModel : ObservableObject
{
    private readonly Action<CleanerCategoryViewModel, bool>? _onBulkSelect;
    private bool _syncingCheckState;

    public string Name { get; }
    public string IconGlyph { get; }
    public string MacroGroup { get; }
    public string AccentColorHex { get; }
    public string IconBackgroundHex { get; }
    public ObservableCollection<CleanerEntryViewModel> Entries { get; } = new();

    [ObservableProperty] public partial bool   IsExpanded { get; set; } = false;
    [ObservableProperty] public partial int    SelectedCount { get; set; }
    [ObservableProperty] public partial int    TotalCount { get; set; }
    [ObservableProperty] public partial long   ScannedBytes { get; set; }
    [ObservableProperty] public partial string CategorySizeText { get; set; } = "";
    [ObservableProperty] public partial bool   HasRunningProcess { get; set; }
    [ObservableProperty] public partial bool?  IsCategoryChecked { get; set; } = false;

    public string SelectionSummary => $"{SelectedCount}/{TotalCount}";
    public bool   HasScannedSize   => ScannedBytes > 0;
    public string HeaderBadgeText  => HasScannedSize
        ? $"{SelectionSummary} · {CategorySizeText}"
        : SelectionSummary;

    partial void OnIsCategoryCheckedChanged(bool? value)
    {
        if (_syncingCheckState) return;
        bool target = value == true;
        if (_onBulkSelect is not null)
        {
            _onBulkSelect(this, target);
        }
        else
        {
            foreach (var e in Entries) e.IsSelected = target;
            RefreshStats();
        }
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HeaderBadgeText));
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HeaderBadgeText));
    }

    partial void OnScannedBytesChanged(long value)
    {
        CategorySizeText = value > 0 ? ScanResult.FormatBytes(value) : "";
        OnPropertyChanged(nameof(HasScannedSize));
        OnPropertyChanged(nameof(HeaderBadgeText));
    }

    public void RefreshStats()
    {
        TotalCount        = Entries.Count;
        SelectedCount     = Entries.Count(e => e.IsSelected);
        ScannedBytes      = Entries.Sum(e => e.ScannedBytes);
        HasRunningProcess = Entries.Any(e => e.IsProcessRunning);

        _syncingCheckState = true;
        IsCategoryChecked = SelectedCount == 0
            ? false
            : (SelectedCount == TotalCount ? true : null);
        _syncingCheckState = false;
    }

    [RelayCommand] private void SelectAll()      => IsCategoryChecked = true;
    [RelayCommand] private void SelectNone()     => IsCategoryChecked = false;
    [RelayCommand] private void SelectDefaults() => Entries.ToList().ForEach(e => e.IsSelected = e.Entry.Default);

    public CleanerCategoryViewModel(
        string name,
        string macroGroup = "Apps",
        string iconGlyph = "\uE74C",
        string accentColorHex = "#8B5CF6",
        string iconBackgroundHex = "#1F8B5CF6",
        Action<CleanerCategoryViewModel, bool>? onBulkSelect = null)
    {
        Name              = name;
        MacroGroup        = macroGroup;
        IconGlyph         = iconGlyph;
        AccentColorHex    = accentColorHex;
        IconBackgroundHex = iconBackgroundHex;
        _onBulkSelect     = onBulkSelect;
    }
}