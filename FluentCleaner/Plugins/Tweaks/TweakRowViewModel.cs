using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Plugins.Tweaks;

namespace FluentCleaner.Plugins.Tweaks;

//one row in the tweak list, wraps a Finding
//tick box writes straight to Finding.Wanted, the Applier only looks at that
public sealed partial class TweakRowViewModel : ObservableObject
{
    internal Finding Finding { get; }

    internal TweakRowViewModel(Finding finding)
    {
        // Finding first, the IsSelected setter writes to it
        Finding = finding;
        IsSelected = finding.Wanted;
    }

    public string Title => Finding.Tweak.Title;
    public string Description => Finding.Tweak.Text;
    public string Category => Finding.Tweak.Where;

    //all values already set
    public bool IsApplied => Finding.Done;

    //!IsApplied, saves a converter
    public bool IsOpen => !Finding.Done;

    //HKLM or policy key
    public bool NeedsAdmin => Finding.Tweak.NeedsAdmin;

    //apply or reset
    public string ActionLabel => Finding.Done
        ? Services.ResourceService.Get("St_TweakReset")
        : Services.ResourceService.Get("St_TweakApply");

    //all values the rule writes, one per line, for the detail strip
    public string RegistryPaths => string.Join("\n", Finding.Tweak.Entries.Select(e =>
        $"{e.Path}\\{e.ValueName} = {e.Recommended}"));

    //key of the first value, without value name (for regedit)
    public string KeyPath => Finding.Tweak.Entries.FirstOrDefault()?.Path ?? "";

    [ObservableProperty] public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => Finding.Wanted = value;

    //after scan or apply
    internal void Refresh()
    {
        // writes the same value back to Finding.Wanted, harmless
        IsSelected = Finding.Wanted;
        OnPropertyChanged(nameof(IsApplied));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(ActionLabel));
    }
}
