using FluentCleaner.Models;
using FluentCleaner.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentCleaner.ViewModels;

// One checkbox row in the left panel. Thin wrapper around CleanerEntry
// holding UI selection, live scanned size, and running-process state.
public partial class CleanerEntryViewModel : ObservableObject
{
    public CleanerEntry Entry { get; }
    public string CategoryName { get; }
    public string ShortName { get; }

    [ObservableProperty] public partial bool   IsSelected { get; set; }
    [ObservableProperty] public partial string SizeText { get; set; } = "";
    [ObservableProperty] public partial long   ScannedBytes { get; set; }
    [ObservableProperty] public partial bool   IsProcessRunning { get; set; }
    [ObservableProperty] public partial string RunningProcessName { get; set; } = "";
    [ObservableProperty] public partial string RunningWarningText { get; set; } = "";

    public string  Name           => Entry.Name;
    public string? Warning        => Entry.Warning;
    public bool    HasWarning     => !string.IsNullOrWhiteSpace(Entry.Warning);
    public bool    HasScannedSize => ScannedBytes > 0;
    public string  RawSourceText  => string.IsNullOrWhiteSpace(Entry.RawText) ? $"[{Entry.Name}]" : Entry.RawText.Trim();

    public string AnalyzeText => ResourceService.Fmt("MenuAnalyzeNamed", ShortName);
    public string CleanText   => ResourceService.Fmt("MenuCleanNamed", ShortName);

    partial void OnScannedBytesChanged(long value) => OnPropertyChanged(nameof(HasScannedSize));

    public CleanerEntryViewModel(CleanerEntry entry, string categoryName = "")
    {
        Entry        = entry;
        CategoryName = categoryName;
        IsSelected   = entry.Default;

        var prefix = (categoryName.Length > 0 ? categoryName : CategoryResolver.TryMapLangSecRef(entry).Name) + " ";
        ShortName  = entry.Name.Length > prefix.Length && entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? entry.Name[prefix.Length..]
            : entry.Name;
    }
}
