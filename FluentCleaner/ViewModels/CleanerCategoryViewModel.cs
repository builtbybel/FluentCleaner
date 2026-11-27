using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using FluentCleaner.Services;

namespace FluentCleaner.ViewModels;

// Groups a bunch of entries under a shared label (e.g. "Google Chrome").
// Select/deselect all lives here because it only touches its own entries.
// Analyze/clean are handled by the page VM; see CleanerPage.xaml.cs.
public partial class CleanerCategoryViewModel : ObservableObject
{
    public string Name { get; }
    public ObservableCollection<CleanerEntryViewModel> Entries { get; } = new();

    [ObservableProperty] public partial bool IsExpanded { get; set; } = false;

    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;                                         // the category row in the tree

    [RelayCommand] private void SelectAll() => Entries.ToList().ForEach(e => e.IsSelected = true);           // check every entry in this category

    [RelayCommand] private void SelectNone() => Entries.ToList().ForEach(e => e.IsSelected = false);          // uncheck every entry in this category

    [RelayCommand] private void SelectDefaults() => Entries.ToList().ForEach(e => e.IsSelected = e.Entry.Default); // restore the Default=True/False values from the database

    [RelayCommand]
    public async Task<CompactionSummary> CompactDatabasesAsync()
    {
        var targets = SqliteCompactorTargets.DiscoverExistingDatabases();
        var service = new SqliteCompactorService();
        return await service.CompactAllAsync(targets);
    }

    public CleanerCategoryViewModel(string name) => Name = name;

    // What the tree shows. Generic categories are localised by convention ("Utilities" ->
    // Cat_Utilities); brand names like Firefox or Adobe have no key, and ResourceService.Get
    // hands the key back on a miss, which is the signal to keep the raw name.
    public string DisplayName
    {
        get
        {
            var key = "Cat_" + Name.Replace(" ", "");
            var text = ResourceService.Get(key);
            return text == key ? Name : text;
        }
    }

    // Right-click menu, same as on an entry.
    public string AnalyzeText => ResourceService.Fmt("MenuAnalyzeNamed", DisplayName);
    public string CleanText   => ResourceService.Fmt("MenuCleanNamed", DisplayName);

    // --- Tree icon -----------------------------------------------------------

    // Segoe Fluent Icons glyph + accent per known category. Anything missing keeps the
    // folder, so a plain folder in the tree means "custom or unrecognised category".
    // Adding a category is one line; the glyphs were checked against the installed font.
    private static readonly Dictionary<string, (string Glyph, uint Rgb)> Icons = new()
    {
        ["Developer Tools"]        = ("", 0x2EC4B6),   // code; Section= in our Winapp2.ini
        ["Microsoft Edge"]         = ("", 0x3FA7E0),   // globe
        ["Internet"]               = ("", 0x4FC3F7),
        ["Applications"]           = ("", 0x9C7BE8),   // app tiles
        ["Multimedia"]             = ("", 0xE8669B),   // video
        ["Utilities"]              = ("", 0x67C98B),   // wrench
        ["Windows"]                = ("", 0x3FA7E0),   // monitor
        ["Firefox"]                = ("", 0xFF8A3D),
        ["Opera"]                  = ("", 0xE8544C),
        ["Opera GX"]               = ("", 0xE8544C),
        ["Safari"]                 = ("", 0x4FC3F7),
        ["Google Chrome"]          = ("", 0xE8C14C),
        ["Vivaldi"]                = ("", 0xE8544C),
        ["Arc Browser"]            = ("", 0xE8669B),
        ["Brave"]                  = ("", 0xFF7139),   // shield
        ["Avast Secure Browser"]   = ("", 0xFF7A18),
        ["AVG Secure Browser"]     = ("", 0x4FC3F7),
        ["Norton Private Browser"] = ("", 0xE8C14C),
        ["Avira Secure Browser"]   = ("", 0xE8544C),
        ["Microsoft Store"]        = ("", 0x4FC3F7),   // shopping bag
        ["Spotify"]                = ("", 0x4CC25F),   // music
        ["iTunes"]                 = ("", 0xE8669B),
        ["WhatsApp"]               = ("", 0x4CC25F),   // chat bubble
        ["Games"]                  = ("", 0x9C7BE8),   // controller
    };

    private const string FolderGlyph = "";
    private const uint FolderRgb = 0xE8B14C;

    // Static, so a result row after Analyze gets the same icon as its category in the tree.
    public static string GlyphFor(string category) =>
        Icons.TryGetValue(category, out var icon) ? icon.Glyph : FolderGlyph;

    public static Microsoft.UI.Xaml.Media.SolidColorBrush BrushFor(string category) =>
        Brush(Icons.TryGetValue(category, out var icon) ? icon.Rgb : FolderRgb);

    public string Glyph => GlyphFor(Name);
    public Microsoft.UI.Xaml.Media.SolidColorBrush IconBrush => BrushFor(Name);

    // One brush per colour, shared across rows; a new brush per binding would be wasteful.
    private static readonly Dictionary<uint, Microsoft.UI.Xaml.Media.SolidColorBrush> Brushes = new();

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(uint rgb)
    {
        if (Brushes.TryGetValue(rgb, out var cached)) return cached;

        var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        Brushes[rgb] = brush;
        return brush;
    }
}