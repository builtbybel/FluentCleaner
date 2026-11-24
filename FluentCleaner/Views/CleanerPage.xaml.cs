using CommunityToolkit.Mvvm.Input;
using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentCleaner.Views;

public sealed partial class CleanerPage : Page, ISearchablePage, IPageActions
{
    public CleanerPageViewModel ViewModel { get; } = new();

    private bool _loaded;
    private bool _isDraggingSplitter;
    private double _dragStartPointerX;
    private double _dragStartColumnWidth;

    public CleanerPage()
    {
        InitializeComponent();

        var savedWidth = AppSettings.Instance.CleanerPaneWidth;
        if (savedWidth >= 240 && savedWidth <= 650)
            LeftPaneColumn.Width = new GridLength(savedWidth);

        Loaded += async (_, _) =>
        {
            UpdateDonutChart();
            if (_loaded) return;
            _loaded = true;

            AppSettings.Reload();
            var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
            if (paths.Count == 0) paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));
            await ViewModel.LoadWinapp2Async(paths);
        };

        ActualThemeChanged += (_, _) => UpdateDonutChart();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or nameof(ViewModel.WindowsBytes) or nameof(ViewModel.BrowsersBytes)
                or nameof(ViewModel.DevAiBytes) or nameof(ViewModel.AppsBytes) or nameof(ViewModel.ScanProgress)
                or nameof(ViewModel.IsBusy))
            {
                DispatcherQueue.TryEnqueue(UpdateDonutChart);
            }
        };
    }

    private void UpdateDonutChart()
    {
        if (DonutCanvas != null)
        {
            StorageDonutRenderer.Render(
                DonutCanvas,
                ViewModel.WindowsBytes,
                ViewModel.BrowsersBytes,
                ViewModel.DevAiBytes,
                ViewModel.AppsBytes,
                ViewModel.ScanProgress,
                ViewModel.IsBusy);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_loaded)
        {
            _ = ViewModel.RefreshCustomEntriesAsync();
            ViewModel.RefreshFooterMetadata();
            ViewModel.RefreshSystemDriveInfo();
        }
    }

    // --- Keyboard Accelerators ------------------------------------------------

    private void AccelAnalyze_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.AnalyzeCommand.CanExecute(null))
            ViewModel.AnalyzeCommand.Execute(null);
        args.Handled = true;
    }

    private void AccelClean_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CanRunCleaner)
            RunCleaner_Click(this, new RoutedEventArgs());
        args.Handled = true;
    }

    private void AccelEscape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsBusy)
            ViewModel.CancelCommand.Execute(null);
        else if (ViewModel.IsShowingDetail)
            ViewModel.ClearDetailCommand.Execute(null);
        args.Handled = true;
    }

    // --- ISearchablePage ----------------------------------------------------------
    public void OnSearch(string text) => ViewModel.SearchText = text;

    // --- IPageActions ----------------------------------------------------------
    public void BuildActions(MenuFlyout flyout)
    {
        void Add(string label, Action action)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }

        Add(ResourceService.Get("St_MenuSelectAll"), () => ViewModel.SelectAllCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSelectNone"), () => ViewModel.SelectNoneCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSelectDefaults"), () => ViewModel.SelectDefaultsCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuExpandAll"), () => ViewModel.ExpandAllCommand.Execute(null));
        Add(ResourceService.Get("St_MenuCollapseAll"), () => ViewModel.CollapseAllCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuSortDesc"), () => ViewModel.SortResultsDescCommand.Execute(null));
        Add(ResourceService.Get("St_MenuSortAsc"), () => ViewModel.SortResultsAscCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add("Export Audit Report…", () => ExportReport_Click(this, new RoutedEventArgs()));
        Add(ResourceService.Get("St_MenuRefresh"), () => ViewModel.RefreshCommand.Execute(null));
    }

    // --- Audit Report Export --------------------------------------------------

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExportReport) return;

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName      = $"FluentCleaner_Audit_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            picker.FileTypeChoices.Add("Markdown Report", [".md"]);
            picker.FileTypeChoices.Add("CSV Audit Log",   [".csv"]);

            var hwnd = WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            bool isCsv = file.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var content = ViewModel.BuildAuditReport(csvFormat: isCsv);
            await File.WriteAllTextAsync(file.Path, content);

            ViewModel.StatusText = $"Audit report saved to {file.Name}";
            ViewModel.ShowBanner(InfoBarSeverity.Success, "Audit Report Exported", $"Saved to {file.Path}");
        }
        catch (Exception ex)
        {
            ViewModel.ShowBanner(InfoBarSeverity.Error, "Export Failed", ex.Message);
        }
    }

    // --- Inline Filter Pills ---------------------------------------------------

    private void FilterPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string mode })
        {
            ViewModel.FilterMode = mode;
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    // --- Results & Detail Inspector -------------------------------------------

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

    private void ExcludeFileInline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        ExcludeFilePath(path);
    }

    private void ExcludeFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string path }) return;
        ExcludeFilePath(path);
    }

    private void ExcludeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

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
        var settings = Services.AppSettings.Instance;
        if (settings.GlobalExclusions.Contains(rule, StringComparer.OrdinalIgnoreCase)) return;

        settings.GlobalExclusions.Add(rule);
        settings.GlobalExclusionsEnabled = true;
        settings.Save();
        ViewModel.StatusText = status;
        ViewModel.RefreshFooterMetadata();
        ViewModel.ShowBanner(InfoBarSeverity.Informational, "Exclusion Added", status, undoExclusionRule: rule);
    }

    // --- Inspector Toolbar Actions --------------------------------------------

    private async void DetailExplain_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntryVm is { } vm)
            await ShowAiExplanationAsync(vm);
    }

    private void DetailRuleLab_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntryVm is { } vm)
            RuleLabPage.Open(vm.Entry.Name, vm.Entry.RawText);
    }

    private async void DetailClean_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntryVm is not { } vm) return;
        if (!await CheckRunningBrowsersAsync([vm])) return;
        if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForEntry(vm))) return;

        await ViewModel.CleanSingleEntryAsync(vm);
    }

    // --- Entry & Category Flyout Handlers -------------------------------------

    private async void EntryAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
            await ViewModel.AnalyzeSingleEntryAsync(vm);
    }

    private async void EntryClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
        {
            if (!await CheckRunningBrowsersAsync([vm])) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForEntry(vm))) return;

            await ViewModel.CleanSingleEntryAsync(vm);
        }
    }

    private async void EntryExplain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
            await ShowAiExplanationAsync(vm);
    }

    private async Task ShowAiExplanationAsync(CleanerEntryViewModel vm)
    {
        var textBlock = new TextBlock
        {
            Text = ResourceService.Get("DlgExplainThinking"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            CornerRadius = new CornerRadius(8),
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
        RuleLabPage.Open(vm.Entry.Name, vm.Entry.RawText);
    }

    private async void CatAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
            await ViewModel.AnalyzeCategoryAsync(vm);
    }

    private async void CatClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
        {
            var selected = vm.Entries.Where(e => e.IsSelected).ToList();
            if (!await CheckRunningBrowsersAsync(selected)) return;
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForCategory(vm))) return;

            await ViewModel.CleanCategoryAsync(vm);
        }
    }

    private async void RunCleaner_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.RunCleanerCommand.CanExecute(null))
            return;

        if (!await CheckRunningBrowsersAsync()) return;
        if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForSelectedEntries()))
            return;

        await ((IAsyncRelayCommand)ViewModel.RunCleanerCommand).ExecuteAsync(null);
    }

    // --- Safety Dialogs -------------------------------------------------------

    private static readonly (string Process, string DisplayName, int[] LangSecRefs)[] KnownBrowsers =
    [
        ("chrome",  "Google Chrome",    [3029]),
        ("firefox", "Mozilla Firefox",  [3026]),
        ("msedge",  "Microsoft Edge",   [3006]),
        ("opera",   "Opera",            [3027, 3035]),
        ("brave",   "Brave",            [3034]),
        ("vivaldi", "Vivaldi",          [3033]),
    ];

    private async Task<bool> CheckRunningBrowsersAsync(IEnumerable<CleanerEntryViewModel>? selectedEntries = null)
    {
        var selectedCodes = (selectedEntries ?? ViewModel.Categories.SelectMany(c => c.Entries).Where(e => e.IsSelected))
            .Select(e => e.Entry.LangSecRef ?? -1)
            .ToHashSet();

        var running = KnownBrowsers
            .Where(b => b.LangSecRefs.Any(selectedCodes.Contains))
            .Where(b => System.Diagnostics.Process.GetProcessesByName(b.Process).Length > 0)
            .Select(b => b.DisplayName)
            .ToList();

        if (running.Count == 0)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            CornerRadius = new CornerRadius(8),
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
        if (warnings.Count == 0)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            CornerRadius = new CornerRadius(8),
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

    // --- Row Hover Accent -----------------------------------------------------

    private void EntryRow_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 1.0);

    private void EntryRow_PointerExited(object sender, PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 0.35);

    private static void SetMenuButtonOpacity(object sender, double opacity)
    {
        if (sender is Grid g)
            foreach (var btn in g.Children.OfType<Button>())
                btn.Opacity = opacity;
    }

    // --- Interactive Pane Splitter (§8.2) -------------------------------------

    private void PaneSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        if (!_isDraggingSplitter && Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brush))
            PaneSplitterLine.Fill = (Brush)brush;
    }

    private void PaneSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter)
        {
            ProtectedCursor = null;
            if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var brush))
                PaneSplitterLine.Fill = (Brush)brush;
        }
    }

    private void PaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PaneSplitter).Properties.IsLeftButtonPressed) return;

        _isDraggingSplitter = true;
        PaneSplitter.CapturePointer(e.Pointer);
        _dragStartPointerX = e.GetCurrentPoint(this).Position.X;
        _dragStartColumnWidth = LeftPaneColumn.ActualWidth;

        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brush))
            PaneSplitterLine.Fill = (Brush)brush;

        e.Handled = true;
    }

    private void PaneSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter) return;

        var currentX = e.GetCurrentPoint(this).Position.X;
        var delta = currentX - _dragStartPointerX;
        var maxAllowed = Math.Max(260, ActualWidth - 340);
        var targetWidth = Math.Clamp(_dragStartColumnWidth + delta, 240, Math.Min(650, maxAllowed));

        LeftPaneColumn.Width = new GridLength(targetWidth);
        e.Handled = true;
    }

    private void PaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        EndSplitterDrag(e);

    private void PaneSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndSplitterDrag(e);

    private void EndSplitterDrag(PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter) return;

        _isDraggingSplitter = false;
        try { PaneSplitter.ReleasePointerCapture(e.Pointer); } catch { }

        ProtectedCursor = null;
        if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var brush))
            PaneSplitterLine.Fill = (Brush)brush;

        var newWidth = LeftPaneColumn.ActualWidth;
        if (newWidth >= 240)
        {
            AppSettings.Instance.CleanerPaneWidth = newWidth;
            AppSettings.Instance.Save();
        }
        e.Handled = true;
    }
}