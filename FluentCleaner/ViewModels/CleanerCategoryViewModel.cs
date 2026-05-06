using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace FluentCleaner.ViewModels;

// Groups a bunch of entries under a shared label (e.g. "Google Chrome").
// Select/deselect all lives here because it only touches its own entries.
// Analyze/clean are handled by the page VM; see CleanerPage.xaml.cs.
public partial class CleanerCategoryViewModel : ObservableObject
{
    public string Name { get; }
    public ObservableCollection<CleanerEntryViewModel> Entries { get; } = new();

    [ObservableProperty] public partial bool IsExpanded { get; set; } = false;

    [RelayCommand] private void SelectAll()      => Entries.ToList().ForEach(e => e.IsSelected = true);           // check every entry in this category
    [RelayCommand] private void SelectNone()     => Entries.ToList().ForEach(e => e.IsSelected = false);          // uncheck every entry in this category
    [RelayCommand] private void SelectDefaults() => Entries.ToList().ForEach(e => e.IsSelected = e.Entry.Default); // restore the Default=True/False values from the database

    public CleanerCategoryViewModel(string name) => Name = name;
}
