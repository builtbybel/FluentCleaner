using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentCleaner.Services;
using FluentCleaner.Plugins.Tweaks;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Plugins.Tweaks;

/// <summary>
/// The tweak catalogue page. Holds no rules of its own: everything comes from the .ini files in
/// Rules\, read by <see cref="TweakDatabase"/>, so a new rule pack never needs a new build.
/// </summary>
public sealed partial class TweaksPageViewModel : ObservableObject
{
    /// <summary>Every rule that fits this PC, filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<TweakRowViewModel> Rows { get; } = [];

    private List<TweakRowViewModel> _all = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";

    public bool IsNotBusy => !IsBusy;
    public bool IsEmpty => Rows.Count == 0;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    // --- filters -------------------------------------------------------------

    /// <summary>"All categories" plus every category the loaded rule files actually use.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The first entry of <see cref="Categories"/>; selecting it filters nothing.</summary>
    public string AllCategories { get; } = ResourceService.Get("St_TweaksAllCategories");

    // Nullable: a bound ComboBox hands back null the moment its item source is emptied.
    [ObservableProperty] public partial string? CategoryFilter { get; set; } = "";
    [ObservableProperty] public partial bool OnlyOpen { get; set; }

    partial void OnCategoryFilterChanged(string? value) => Rebuild();
    partial void OnOnlyOpenChanged(bool value) => Rebuild();

    // --- selection -----------------------------------------------------------

    /// <summary>The row whose description the detail strip shows. Null hides the strip.</summary>
    [ObservableProperty] public partial TweakRowViewModel? SelectedRow { get; set; }

    // NullToVisibilityConverter only answers for strings, so the strip binds to this instead.
    public bool HasSelection => SelectedRow is not null;

    partial void OnSelectedRowChanged(TweakRowViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    // --- outcome of the last run ---------------------------------------------

    [ObservableProperty] public partial bool InfoOpen { get; set; }
    [ObservableProperty] public partial string InfoTitle { get; set; } = "";
    [ObservableProperty] public partial string InfoMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity InfoSeverity { get; set; } = InfoBarSeverity.Informational;

    /// <summary>A rule asked for it, so the strip offers the restart instead of just naming it.</summary>
    [ObservableProperty] public partial bool NeedsExplorerRestart { get; set; }

    /// <summary>Ends Explorer and waits for it to come back; values read only at startup take effect.</summary>
    [RelayCommand]
    private void RestartExplorer()
    {
        WindowsActions.RestartExplorer();
        NeedsExplorerRestart = false;
        InfoOpen = false;
    }

    // --- scanning ------------------------------------------------------------

    /// <summary>Reads the catalogue and checks every rule against this PC.</summary>
    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;

        // try/finally, not just a happy path: anything that throws in here would otherwise
        // leave IsBusy stuck true, and both buttons disabled for the rest of the session.
        try
        {
            var progress = new Progress<WorkProgress>(p =>
            {
                StatusText = p.Text;
                Progress = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
            });

            // Scanner reads the registry for every rule; off the UI thread so the list stays alive.
            var findings = await Task.Run(() => Scanner.Scan(progress));

            _all = findings.Select(f => new TweakRowViewModel(f)).ToList();
            RefreshCategories();
            Rebuild();

            StatusText = ResourceService.Fmt("St_TweaksChecked", _all.Count);
        }
        catch (Exception ex)
        {
            InfoSeverity = InfoBarSeverity.Error;
            InfoTitle = ResourceService.Get("St_TweaksScanFailed");
            InfoMessage = ex.Message;
            InfoOpen = true;
        }
        finally
        {
            Progress = 0;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Rebuilds the category list from what the rule files actually contain, so a pack's own
    /// category appears without the app knowing its name. Only touched when the set really
    /// changed: clearing a bound collection makes the ComboBox push a null selection through,
    /// and a second scan would then wipe the filter it is supposed to keep.
    /// </summary>
    private void RefreshCategories()
    {
        var wanted = new List<string> { AllCategories };
        wanted.AddRange(_all.Select(r => r.Category).Where(c => c.Length > 0).Distinct().Order());
        if (wanted.SequenceEqual(Categories)) return;

        var wasSelected = CategoryFilter ?? "";
        Categories.Clear();
        foreach (var name in wanted) Categories.Add(name);
        CategoryFilter = wanted.Contains(wasSelected) ? wasSelected : AllCategories;
    }

    // --- applying ------------------------------------------------------------

    private bool CanApply => !IsBusy;

    /// <summary>Writes every ticked rule, or puts it back when it is already applied.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    public Task ApplyAsync() => RunAsync(_all.Select(row => row.Finding).ToList());

    /// <summary>
    /// Runs one rule straight from its context menu, whatever is ticked elsewhere. The tick
    /// is set on a copy of the list, so a right-click never changes the user's selection.
    /// </summary>
    public async Task ApplyOneAsync(TweakRowViewModel row)
    {
        var finding = row.Finding;
        var wasWanted = finding.Wanted;
        finding.Wanted = true;
        try { await RunAsync([finding]); }
        finally { finding.Wanted = wasWanted && !finding.Done; row.Refresh(); }
    }

    // The applier reads Wanted off the findings it is handed, so both callers differ only in
    // which findings they pass. try/finally for the same reason as the scan: a throw in here
    // would otherwise leave the page permanently busy.
    private async Task RunAsync(List<Finding> findings)
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;

        try
        {
            var progress = new Progress<WorkProgress>(p =>
            {
                StatusText = p.Text;
                Progress = p.Total == 0 ? 0 : p.Completed * 100.0 / p.Total;
            });

            var report = await Task.Run(() => Applier.Apply(findings, progress));

            foreach (var row in _all) row.Refresh();

            StatusText = report.Failed.Count == 0
                ? ResourceService.Fmt("St_TweaksApplied", report.Fixed, report.Reset)
                : ResourceService.Fmt("St_TweaksPartly", report.Fixed, report.Reset, report.Failed.Count);

            Announce(report);
            Rebuild();            // "only open" hides what the run just applied
        }
        catch (Exception ex)
        {
            InfoSeverity = InfoBarSeverity.Error;
            InfoTitle = ResourceService.Get("St_TweaksScanFailed");
            InfoMessage = ex.Message;
            InfoOpen = true;
        }
        finally
        {
            Progress = 0;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Turns the applier's report into the strip above the list. Three things are worth saying
    /// out loud: rights that were missing, a shell that has to restart, and rules that failed.
    /// </summary>
    private void Announce(Report report)
    {
        NeedsExplorerRestart = report.Restart.Contains("explorer");
        var parts = new List<string>();

        if (report.NeedsAdmin) parts.Add(ResourceService.Get("St_TweaksNeedAdmin"));
        if (report.Restart.Contains("system")) parts.Add(ResourceService.Get("St_TweaksNeedReboot"));
        if (NeedsExplorerRestart) parts.Add(ResourceService.Get("St_TweaksNeedExplorer"));
        if (report.Failed.Count > 0)
            parts.Add(ResourceService.Fmt("St_TweaksFailedList", string.Join(", ", report.Failed.Take(5))));

        InfoSeverity = report.NeedsAdmin || report.Failed.Count > 0
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;

        InfoTitle = ResourceService.Fmt("St_TweaksApplied", report.Fixed, report.Reset);
        InfoMessage = string.Join("  ", parts);
        InfoOpen = parts.Count > 0 || report.Fixed + report.Reset > 0;
    }

    // --- list ----------------------------------------------------------------

    public int SelectedCount => _all.Count(row => row.IsSelected);

    // Rebuilt rather than filtered in place: the list is ~100 rows, so clarity beats cleverness.
    // The three filters are independent and all have to pass.
    private void Rebuild()
    {
        var needle = SearchText.Trim();
        var category = CategoryFilter ?? "";
        bool byCategory = category.Length > 0 && category != AllCategories;

        var visible = _all.Where(row =>
            (!OnlyOpen || !row.IsApplied)
            && (!byCategory || row.Category == category)
            && (needle.Length == 0
                || row.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.Category.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)));

        Rows.Clear();
        foreach (var row in visible) Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
