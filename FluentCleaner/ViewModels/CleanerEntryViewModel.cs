using FluentCleaner.Models;
using FluentCleaner.Services;
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

    // Tree label only: under "Microsoft Edge", "Microsoft Edge Caches" reads as "Caches".
    // Results, menus, search and FluentSeeds keep the full name.
    public string ShortName { get; }
    public bool HasWarning => !string.IsNullOrWhiteSpace(Entry.Warning);

    // Right-click menu: with the name in it, you can see which entry it belongs to.
    // The short form, like the tree: the full names made the menu half a screen wide.
    public string AnalyzeText => ResourceService.Fmt("MenuAnalyzeNamed", ShortName);
    public string CleanText   => ResourceService.Fmt("MenuCleanNamed", ShortName);

    public CleanerEntryViewModel(CleanerEntry entry)
    {
        Entry      = entry;
        IsSelected = entry.Default;

        var prefix = CategoryResolver.TryMapLangSecRef(entry).Name + " ";
        ShortName  = entry.Name.Length > prefix.Length && entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? entry.Name[prefix.Length..]
            : entry.Name;
    }
}
