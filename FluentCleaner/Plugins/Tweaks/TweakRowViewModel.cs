using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Plugins.Tweaks;

namespace FluentCleaner.Plugins.Tweaks;

/// <summary>
/// One row of the tweak catalogue. Wraps a <see cref="Finding"/> — the engine's own pairing of
/// a rule with what this PC currently says about it — and exposes it to the list. The tick box
/// writes straight through to <c>Finding.Wanted</c>, because the applier reads that and nothing
/// else when deciding what to write.
/// </summary>
public sealed partial class TweakRowViewModel : ObservableObject
{
    internal Finding Finding { get; }

    internal TweakRowViewModel(Finding finding)
    {
        // Finding first: the IsSelected setter writes through to it.
        Finding = finding;
        IsSelected = finding.Wanted;
    }

    public string Title => Finding.Tweak.Title;
    public string Description => Finding.Tweak.Text;
    public string Category => Finding.Tweak.Where;

    /// <summary>True when every value of this rule already stands as the catalogue wants it.</summary>
    public bool IsApplied => Finding.Done;

    /// <summary>
    /// Inverse of <see cref="IsApplied"/>, so the state column needs no inverting converter.
    /// </summary>
    public bool IsOpen => !Finding.Done;

    /// <summary>Needs the machine hive or a policy key, so a standard user cannot write it.</summary>
    public bool NeedsAdmin => Finding.Tweak.NeedsAdmin;

    /// <summary>What ticking this row would do: apply the rule, or put it back.</summary>
    public string ActionLabel => Finding.Done
        ? Services.ResourceService.Get("St_TweakReset")
        : Services.ResourceService.Get("St_TweakApply");

    /// <summary>
    /// Every registry value the rule writes, one per line. Shown in the detail strip so the
    /// user can see exactly what a rule touches before ticking it — this app writes to the
    /// registry, and "trust me" is not good enough.
    /// </summary>
    public string RegistryPaths => string.Join("\n", Finding.Tweak.Entries.Select(e =>
        $"{e.Path}\\{e.ValueName} = {e.Recommended}"));

    /// <summary>
    /// The key of the first value the rule writes, without the value name — what Registry
    /// Editor needs to be sent somewhere. A rule writing several values opens at the first.
    /// </summary>
    public string KeyPath => Finding.Tweak.Entries.FirstOrDefault()?.Path ?? "";

    [ObservableProperty] public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => Finding.Wanted = value;

    /// <summary>Re-reads what the engine now holds after a scan or an apply run.</summary>
    internal void Refresh()
    {
        // The setter writes the same value straight back into Finding.Wanted, which is a no-op.
        IsSelected = Finding.Wanted;
        OnPropertyChanged(nameof(IsApplied));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(ActionLabel));
    }
}
