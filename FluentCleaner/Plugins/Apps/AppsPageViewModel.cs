using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentCleaner.Plugins.Apps;
using FluentCleaner.Services;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Plugins.Apps;

//app uninstaller;windows lists the apps, AppRules.ini only says how safe they are to remove
public sealed partial class AppsPageViewModel : ObservableObject
{
    public ObservableCollection<AppRowViewModel> Rows { get; } = [];

    private List<AppRowViewModel> _all = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool Indeterminate { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";

    public bool IsNotBusy => !IsBusy;
    public bool IsEmpty => Rows.Count == 0;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    // --- filters -------------------------------------------------------------

    //all + one per safety level
    public ObservableCollection<string> SafetyFilters { get; } =
    [
        ResourceService.Get("St_AppsAllLevels"),
        ResourceService.Get("St_AppCandidate"),
        ResourceService.Get("St_AppPersonal"),
        ResourceService.Get("St_AppProtected"),
    ];

    [ObservableProperty] public partial string SafetyFilter { get; set; } = ResourceService.Get("St_AppsAllLevels");
    [ObservableProperty] public partial bool OnlyRemovable { get; set; } = true;

    partial void OnSafetyFilterChanged(string value) => Rebuild();
    partial void OnOnlyRemovableChanged(bool value) => Rebuild();

    // --- outcome -------------------------------------------------------------

    [ObservableProperty] public partial bool InfoOpen { get; set; }
    [ObservableProperty] public partial string InfoTitle { get; set; } = "";
    [ObservableProperty] public partial string InfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; } = InfoBarSeverity.Informational;

    //ticked rows, shown on the remove button
    public int SelectedCount => _all.Count(row => row.IsSelected);

    // --- scanning ------------------------------------------------------------

    //uninstall keys first, then store apps
    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Indeterminate = true;
        Progress = 0;

        // finally, or a throw leaves IsBusy stuck and the buttons dead
        try
        {
            var progress = new Progress<WorkProgress>(p =>
            {
                StatusText = p.Text;
                Indeterminate = p.Indeterminate;
                Progress = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
            });

            var scan = await PackageService.ScanAsync(progress);

            _all = scan.Apps.Select(a => new AppRowViewModel(a)).ToList();
            Rebuild();

            // store apps failed, programs are still there;show what's missing
            if (scan.Error.Length > 0)
            {
                InfoSeverity = InfoBarSeverity.Warning;
                InfoTitle = ResourceService.Get("St_AppsPartialTitle");
                InfoMessage = scan.Error;
                InfoOpen = true;
            }

            StatusText = ResourceService.Fmt("St_AppsFound", _all.Count);
        }
        finally
        {
            Indeterminate = false;
            Progress = 0;
            IsBusy = false;
        }
    }

    // --- removing ------------------------------------------------------------

    private bool CanRemove => !IsBusy;

    //removes the ticked apps;store via powershell, programs via their uninstaller.
    //uninstaller window still open = "started", not removed
    [RelayCommand(CanExecute = nameof(CanRemove))]
    public Task RemoveAsync() =>
        RunAsync(_all.Where(row => row.IsSelected && row.CanRemove).Select(row => row.App).ToList());

    //one app from the context menu, ignores the ticks
    public Task RemoveOneAsync(AppRowViewModel row) =>
        row.CanRemove ? RunAsync([row.App]) : Task.CompletedTask;

    private async Task RunAsync(List<InstalledApp> selected)
    {
        if (selected.Count == 0 || IsBusy) return;

        IsBusy = true;
        Progress = 0;
        int removed = 0, started = 0;
        var failed = new List<string>();

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var app = selected[i];
                StatusText = ResourceService.Fmt(
                    app.IsProgram ? "Apps_RemovingProgram" : "Apps_Removing", app.Name);
                Indeterminate = true;

                try
                {
                    bool gone;
                    if (app.IsProgram) gone = await ProgramService.RemoveAsync(app);
                    else { await PackageService.RemoveAsync(app); gone = true; }

                    if (gone) { removed++; app.Removed = true; app.Selected = false; }
                    else started++;
                }
                //failed one stays ticked, next app goes on
                catch (Exception ex) { failed.Add(app.Name + ": " + ex.Message); }

                Indeterminate = false;
                Progress = (i + 1) * 100.0 / selected.Count;
            }

            foreach (var row in _all) row.Refresh();
            Rebuild();

            InfoSeverity = failed.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            InfoTitle = ResourceService.Fmt("St_AppsRemoved", removed);
            var parts = new List<string>();
            if (started > 0) parts.Add(ResourceService.Fmt("St_AppsStarted", started));
            if (failed.Count > 0) parts.Add(ResourceService.Fmt("St_AppsFailedList", string.Join(", ", failed.Take(5))));
            InfoMessage = string.Join("  ", parts);
            InfoOpen = true;

            StatusText = ResourceService.Fmt("St_AppsFound", _all.Count);
        }
        finally
        {
            Indeterminate = false;
            Progress = 0;
            IsBusy = false;
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    // --- list ----------------------------------------------------------------

    private void Rebuild()
    {
        var needle = SearchText.Trim();
        bool byLevel = SafetyFilter != SafetyFilters[0];

        var visible = _all.Where(row =>
            (!OnlyRemovable || row.CanRemove)
            && (!byLevel || row.SafetyLabel == SafetyFilter)
            && (needle.Length == 0
                || row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.Publisher.Contains(needle, StringComparison.OrdinalIgnoreCase)));

        Rows.Clear();
        foreach (var row in visible) Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
    }
}
