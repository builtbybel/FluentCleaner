using FluentCleaner.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentCleaner.ViewModels;

// One checkbox row in the left panel. Thin wrapper around CleanerEntry
// this is just enough state for the UI, nothing else
public partial class CleanerEntryViewModel : ObservableObject
{
    public CleanerEntry Entry { get; }

    [ObservableProperty] public partial bool   IsSelected { get; set; }
    [ObservableProperty] public partial string SizeText { get; set; } = "";

    public string  Name    => Entry.Name;
    public string? Warning => Entry.Warning;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Entry.Warning);

    public CleanerEntryViewModel(CleanerEntry entry)
    {
        Entry      = entry;
        IsSelected = entry.Default;
    }
}
