using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Plugins.Apps;
using FluentCleaner.Services;

namespace FluentCleaner.Plugins.Apps;

/// <summary>
/// One installed app in the list. Wraps the engine's <see cref="InstalledApp"/>; the tick box
/// writes straight through to <c>Selected</c>, which is what the removal loop reads.
/// </summary>
public sealed partial class AppRowViewModel : ObservableObject
{
    internal InstalledApp App { get; }

    internal AppRowViewModel(InstalledApp app)
    {
        App = app;
        IsSelected = app.Selected;
    }

    public string Name => App.Name;

    /// <summary>
    /// A store package reports its publisher as a full certificate subject
    /// ("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, …"); only the common
    /// name is worth a column. A classic program already reports a plain name.
    /// </summary>
    public string Publisher
    {
        get
        {
            var raw = App.Publisher;
            int cn = raw.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (cn < 0) return raw;
            var rest = raw.Substring(cn + 3);
            int comma = rest.IndexOf(',');
            return (comma < 0 ? rest : rest.Substring(0, comma)).Trim().Trim('"');
        }
    }

    /// <summary>Store app or classic program — they are removed in completely different ways.</summary>
    public string Kind => App.IsProgram
        ? ResourceService.Get("St_AppProgram")
        : ResourceService.Get("St_AppPackage");

    /// <summary>What AppRules.ini made of it, or why Windows will not let it go.</summary>
    public string SafetyLabel => App.Removed
        ? ResourceService.Get("St_AppRemoved")
        : App.Safety switch
        {
            AppSafety.Protected => ResourceService.Get("St_AppProtected"),
            AppSafety.Candidate => ResourceService.Get("St_AppCandidate"),
            _ => ResourceService.Get("St_AppPersonal"),
        };

    /// <summary>
    /// What identifies this app to Windows: the package family name for a store app, the
    /// uninstall key for a classic program. Worth copying when writing a script or searching.
    /// </summary>
    public string Identifier => App.Family.StartsWith("prog:", StringComparison.Ordinal)
        ? App.Family.Substring(5)
        : App.Family;

    /// <summary>False for anything Windows needs; the tick box is disabled for those.</summary>
    public bool CanRemove => App.CanRemove;

    /// <summary>Greys out a row that this session already uninstalled.</summary>
    public double RowOpacity => App.Removed ? 0.45 : 1.0;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => App.Selected = value;

    /// <summary>Re-reads the engine's own state after a removal run.</summary>
    internal void Refresh()
    {
        IsSelected = App.Selected;
        OnPropertyChanged(nameof(SafetyLabel));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(RowOpacity));
    }
}
