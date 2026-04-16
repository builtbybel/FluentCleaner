using FluentCleaner.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentCleaner.ViewModels;

// One checkbox row in the left panel. Thin wrapper around CleanerEntry
// this is just enough state for the UI, nothing else
public partial class CleanerEntryViewModel : ObservableObject
{
    public CleanerEntry Entry { get; }

    [ObservableProperty] private bool   _isSelected;
    [ObservableProperty] private string _sizeText = "";

    public string  Name    => Entry.Name;
    public string? Warning => Entry.Warning;
    public bool HasWarning => !string.IsNullOrWhiteSpace(Entry.Warning);

    public CleanerEntryViewModel(CleanerEntry entry)
    {
        Entry      = entry;
        _isSelected = entry.Default;
    }
}
