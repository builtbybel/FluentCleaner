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
    public string? AppIconSource { get; }
    public bool HasAppIcon => !string.IsNullOrEmpty(AppIconSource);
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

    public string DisplayName
    {
        get
        {
            var key = "Cat_" + Name.Replace(" ", "");
            var text = ResourceService.Get(key);
            return text == key ? Name : text;
        }
    }

    public string AnalyzeText => ResourceService.Fmt("MenuAnalyzeNamed", DisplayName);
    public string CleanText   => ResourceService.Fmt("MenuCleanNamed", DisplayName);

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

    [RelayCommand] private void Toggle()        => IsExpanded = !IsExpanded;
    [RelayCommand] private void SelectAll()      => IsCategoryChecked = true;
    [RelayCommand] private void SelectNone()     => IsCategoryChecked = false;
    [RelayCommand] private void SelectDefaults() => Entries.ToList().ForEach(e => e.IsSelected = e.Entry.Default);

    // Tree icon mapping
    private static readonly Dictionary<string, (string Glyph, uint Rgb)> Icons = new()
    {
        ["Developer Tools"]        = ("", 0x2EC4B6),
        ["Microsoft Edge"]         = ("", 0x3FA7E0),
        ["Internet"]               = ("", 0x4FC3F7),
        ["Applications"]           = ("", 0x9C7BE8),
        ["Multimedia"]             = ("", 0xE8669B),
        ["Utilities"]              = ("", 0x67C98B),
        ["Windows"]                = ("", 0x3FA7E0),
        ["Firefox"]                = ("", 0xFF8A3D),
        ["Opera"]                  = ("", 0xE8544C),
        ["Opera GX"]               = ("", 0xE8544C),
        ["Safari"]                 = ("", 0x4FC3F7),
        ["Google Chrome"]          = ("", 0xE8C14C),
        ["Vivaldi"]                = ("", 0xE8544C),
        ["Arc Browser"]            = ("", 0xE8669B),
        ["Brave"]                  = ("", 0xFF7139),
        ["Avast Secure Browser"]   = ("", 0xFF7A18),
        ["AVG Secure Browser"]     = ("", 0x4FC3F7),
        ["Norton Private Browser"] = ("", 0xE8C14C),
        ["Avira Secure Browser"]   = ("", 0xE8544C),
        ["Microsoft Store"]        = ("", 0x4FC3F7),
        ["Spotify"]                = ("", 0x4CC25F),
        ["iTunes"]                 = ("", 0xE8669B),
        ["WhatsApp"]               = ("", 0x4CC25F),
        ["Games"]                  = ("", 0x9C7BE8),
    };
    private const string FolderGlyph = "";
    private const uint FolderRgb = 0xE8B14C;

    public static string GlyphFor(string category) =>
        Icons.TryGetValue(category, out var icon) ? icon.Glyph : FolderGlyph;

    public static Microsoft.UI.Xaml.Media.SolidColorBrush BrushFor(string category) =>
        Brush(Icons.TryGetValue(category, out var icon) ? icon.Rgb : FolderRgb);

    public string Glyph => GlyphFor(Name);
    public Microsoft.UI.Xaml.Media.SolidColorBrush IconBrush => BrushFor(Name);

    private static readonly Dictionary<uint, Microsoft.UI.Xaml.Media.SolidColorBrush> Brushes = new();

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(uint rgb)
    {
        if (Brushes.TryGetValue(rgb, out var cached)) return cached;
        var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        Brushes[rgb] = brush;
        return brush;
    }

    public CleanerCategoryViewModel(
        string name,
        string macroGroup = "Apps",
        string iconGlyph = "\uE71D",
        string accentColorHex = "#8B5CF6",
        string iconBackgroundHex = "#1F8B5CF6",
        string? appIconSource = null,
        bool hasAppIcon = false,
        Action<CleanerCategoryViewModel, bool>? onBulkSelect = null)
    {
        Name              = name;
        MacroGroup        = macroGroup;
        IconGlyph         = iconGlyph;
        AccentColorHex    = accentColorHex;
        IconBackgroundHex = iconBackgroundHex;
        AppIconSource     = appIconSource;
        _onBulkSelect     = onBulkSelect;
    }
}
