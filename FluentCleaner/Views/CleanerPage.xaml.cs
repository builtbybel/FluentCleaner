using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace FluentCleaner.Views;

/// <summary>Cleaner page: entry tree on the left, results on the right, one footer.</summary>
public sealed partial class CleanerPage : Page, ISearchablePage, IPageActions, IRefreshablePage
{
    public CleanerPageViewModel ViewModel { get; } = new();

    private bool _loaded;
    private bool _sortDescending = true;

    public CleanerPage()
    {
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Categories.CollectionChanged += OnCategoriesChanged;

        UpdateStatusBar();

        // On Loaded, not in the ctor: measured 20 ms faster. The catalogue is preloaded at launch.
        Loaded += async (_, _) =>
        {
            if (Application.Current is App app && app.MainWindow is { } mainWin)
            {
                mainWin.Activated -= MainWindow_Activated;
                mainWin.Activated += MainWindow_Activated;
            }

            if (_loaded) return;
            _loaded = true;

            AppSettings.Reload();
            ShowWhatsNewOnce();   // before the load, which saves a first run's selection
            await ViewModel.LoadWinapp2Async(CleanerPageViewModel.DatabasePaths());
        };

        Unloaded += (_, _) =>
        {
            if (Application.Current is App app && app.MainWindow is { } mainWin)
                mainWin.Activated -= MainWindow_Activated;
        };
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated && _loaded && !ViewModel.IsBusy)
            _ = ViewModel.RefreshRunningProcessesAsync();
    }

    // Once after an update. A fresh install has no saved selection yet and gets nothing.
    // Saved right away, so the bar doesn't come back if someone just ignores it.
    private void ShowWhatsNewOnce()
    {
        var settings = AppSettings.Instance;
        var now = AppInfo.VersionString;
        if (settings.ModernLastVersion == now) return;

        if (settings.ModernLastVersion is not null || settings.HasSavedCleanerSelection)
        {
            UpdateNotice.Message = ResourceService.Fmt("St_UpdatedTo", now);
            UpdateNotice.IsOpen = true;
        }

        settings.ModernLastVersion = now;
        settings.Save();
    }

    private async void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        UpdateNotice.IsOpen = false;
        await AppLinks.OpenAsync(AppLinks.LatestRelease);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_loaded)
            _ = ViewModel.RefreshCustomEntriesAsync();
    }

    // --- ISearchablePage -----------------------------------------------------
    public void OnSearch(string text) => ViewModel.SearchText = text;

    // Applying a seed runs Analyze, which only scans visible entries, so the filter goes.
    private async void ShareSelection_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.MainWindow?.ClearSearch();
        await FluentSeedDialog.OpenAsync(ViewModel, XamlRoot, ActualTheme);
    }

    // --- IPageActions --------------------------------------------------------
    public void BuildActions(MenuFlyout flyout)
    {
        flyout.AddItem(ResourceService.Get("St_MenuSelectAll"), () => ViewModel.SelectAllCommand.Execute(null));
        flyout.AddItem(ResourceService.Get("St_MenuSelectNone"), () => ViewModel.SelectNoneCommand.Execute(null));
        flyout.AddItem(ResourceService.Get("St_MenuSelectDefaults"), () => ViewModel.SelectDefaultsCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.AddItem(ResourceService.Get("St_MenuRefresh"), Refresh);
    }

    // CanExecute keeps F5 from starting a second load while one is running.
    public void Refresh()
    {
        if (ViewModel.RefreshCommand.CanExecute(null))
            ViewModel.RefreshCommand.Execute(null);
    }

    // --- Status bar ----------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateStatusBar();

    private void OnCategoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateStatusBar();

    private void EntryCheck_Changed(object sender, RoutedEventArgs e) => UpdateStatusBar();

    // Counts span the whole tree, so they are computed here rather than bound.
    // Scan status and results live in the run header above the results.
    private void UpdateStatusBar()
    {
        var entries = ViewModel.Categories.SelectMany(c => c.Entries).ToList();
        StatusCounts.Text =
            ResourceService.Fmt("St_StatusEntries", entries.Count, ViewModel.CatalogueCount) + "        " +   // gap, no separator, like Explorer's status bar
            ResourceService.Fmt("St_StatusSelected", entries.Count(entry => entry.IsSelected));
    }

    // Offered in the run header after a clean that skipped files (OfferElevation).
    private void RestartElevated_Click(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Exit();
        }
        catch { }   // UAC declined
    }

    // --- Sorting -------------------------------------------------------------

    private void SortBySize_Click(object sender, RoutedEventArgs e)
    {
        _sortDescending = !_sortDescending;
        SortGlyph.Glyph = _sortDescending ? "" : "";
        ViewModel.SortResultLinesBySize(_sortDescending);
    }

    private void SortByName_Click(object sender, RoutedEventArgs e)
    {
        var sorted = ViewModel.ResultLines
            .OrderBy(l => l.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            int from = ViewModel.ResultLines.IndexOf(sorted[i]);
            if (from != i) ViewModel.ResultLines.Move(from, i);
        }
    }

    // --- List interaction ----------------------------------------------------

    private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScanResultLine line && line.Result is not null)
            ViewModel.SelectedResultLine = line;
    }

    private void DetailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DetailLine { IsHeader: false } line) return;
        var path = line.Text;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // --- Exclusions ----------------------------------------------------------

    private void ExcludeFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        AddGlobalExclusion($"FILE|{dir}|{file}", ResourceService.Fmt("St_ExcludedFile", file));
    }

    private void ExcludeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;

        AddGlobalExclusion($"PATH|{dir}", ResourceService.Fmt("St_ExcludedFolder", dir));
    }

    private void AddGlobalExclusion(string rule, string status)
    {
        var settings = AppSettings.Instance;
        if (settings.GlobalExclusions.Contains(rule, StringComparer.OrdinalIgnoreCase)) return;

        settings.GlobalExclusions.Add(rule);
        settings.GlobalExclusionsEnabled = true;
        settings.Save();
        ViewModel.StatusText = status;
    }

    // --- Entry actions -------------------------------------------------------

    private async void EntryAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
            await ViewModel.AnalyzeSingleEntryAsync(vm);
    }

    private async void EntryClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
        {
            if (!await CheckRunningBrowsersAsync(new[] { vm })) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForEntry(vm))) return;

            await ViewModel.CleanSingleEntryAsync(vm);
        }
    }

    private async void EntryExplain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CleanerEntryViewModel vm }) return;

        var textBlock = new TextBlock
        {
            Text = ResourceService.Get("DlgExplainThinking"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = vm.Name,
            CloseButtonText = ResourceService.Get("DlgExplainClose"),
            Content = textBlock
        };

        var showTask = dialog.ShowAsync().AsTask();
        textBlock.Text = await AiExplainer.ExplainAsync(vm.Entry);
        await showTask;
    }

    private void EntrySource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CleanerEntryViewModel vm }) return;
        RuleLabPage.Open(vm.ShortName, vm.Entry.RawText);
    }

    // --- Category actions ----------------------------------------------------

    private async void CatAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
            await ViewModel.AnalyzeCategoryAsync(vm);
    }

    private async void CatClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
        {
            var selected = vm.Entries.Where(entry => entry.IsSelected).ToList();
            if (!await CheckRunningBrowsersAsync(selected)) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForCategory(vm))) return;

            await ViewModel.CleanCategoryAsync(vm);
        }
    }

    private async void RunCleaner_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.RunCleanerCommand.CanExecute(null)) return;

        if (!await CheckRunningBrowsersAsync()) return;
        if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForSelectedEntries())) return;

        await ((IAsyncRelayCommand)ViewModel.RunCleanerCommand).ExecuteAsync(null);
    }

    // --- Safety prompts ------------------------------------------------------

    private static readonly (string Process, string DisplayName, int[] LangSecRefs)[] KnownBrowsers =
    [
        ("chrome",  "Google Chrome",   [3029]),
        ("firefox", "Mozilla Firefox", [3026]),
        ("msedge",  "Microsoft Edge",  [3006]),
        ("opera",   "Opera",           [3027, 3035]),
        ("brave",   "Brave",           [3034]),
        ("vivaldi", "Vivaldi",         [3033]),
    ];

    private async Task<bool> CheckRunningBrowsersAsync(IEnumerable<CleanerEntryViewModel>? selectedEntries = null)
    {
        var selectedCodes = (selectedEntries ?? ViewModel.Categories.SelectMany(c => c.Entries).Where(entry => entry.IsSelected))
            .Select(entry => entry.Entry.LangSecRef ?? -1)
            .ToHashSet();

        var running = KnownBrowsers
            .Where(b => b.LangSecRefs.Any(selectedCodes.Contains))
            .Where(b => System.Diagnostics.Process.GetProcessesByName(b.Process).Length > 0)
            .Select(b => b.DisplayName)
            .ToList();

        if (running.Count == 0) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = ResourceService.Get("DlgBrowsersTitle"),
            PrimaryButtonText = ResourceService.Get("DlgBrowsersContinue"),
            CloseButtonText = ResourceService.Get("DlgBrowsersCancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = ResourceService.Fmt("DlgBrowsersMessage", string.Join(", ", running))
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmWarningsAsync(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = ResourceService.Get("DlgWarningTitle"),
            PrimaryButtonText = ResourceService.Get("DlgWarningContinue"),
            CloseButtonText = ResourceService.Get("DlgWarningCancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text =
                        ResourceService.Get("DlgWarningMessage") +
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        string.Join($"{Environment.NewLine}{Environment.NewLine}", warnings),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

/// <summary>Tree chevron: collapsed points right, expanded points down.</summary>
public partial class BoolToChevronConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        ((char)(value is true ? 0xE70D : 0xE76C)).ToString();

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>Type column in the detail list.</summary>
public partial class PathKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        if (s.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return "Registry";

        var ext = Path.GetExtension(s);
        return string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
