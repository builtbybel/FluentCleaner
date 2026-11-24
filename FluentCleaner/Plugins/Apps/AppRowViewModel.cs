using CommunityToolkit.Mvvm.ComponentModel;
using FluentCleaner.Plugins.Apps;
using FluentCleaner.Services;

namespace FluentCleaner.Plugins.Apps;

//one row in the apps list, wraps InstalledApp;tick box writes to Selected
public sealed partial class AppRowViewModel : ObservableObject
{
    internal InstalledApp App { get; }

    internal AppRowViewModel(InstalledApp app)
    {
        App = app;
        IsSelected = app.Selected;
    }

    public string Name => App.Name;

    //store apps give "CN=Microsoft Corporation, O=..., L=..." -> only the CN part.
    //classic programs already have a plain name
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

    //store app or program
    public string Kind => App.IsProgram
        ? ResourceService.Get("St_AppProgram")
        : ResourceService.Get("St_AppPackage");

    //removed, protected, safe or personal
    public string SafetyLabel => App.Removed
        ? ResourceService.Get("St_AppRemoved")
        : App.Safety switch
        {
            AppSafety.Protected => ResourceService.Get("St_AppProtected"),
            AppSafety.Candidate => ResourceService.Get("St_AppCandidate"),
            _ => ResourceService.Get("St_AppPersonal"),
        };

    //package family name or uninstall key, for copy
    public string Identifier => App.Family.StartsWith("prog:", StringComparison.Ordinal)
        ? App.Family.Substring(5)
        : App.Family;

    //false = tick box disabled
    public bool CanRemove => App.CanRemove;

    //greyed out when already removed
    public double RowOpacity => App.Removed ? 0.45 : 1.0;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => App.Selected = value;

    //after a removal run
    internal void Refresh()
    {
        IsSelected = App.Selected;
        OnPropertyChanged(nameof(SafetyLabel));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(RowOpacity));
    }
}
