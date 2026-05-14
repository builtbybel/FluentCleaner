using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

public sealed partial class CleanerPage : Page, ISearchablePage, IPageActions
{
    public CleanerPageViewModel ViewModel { get; } = new();

    private bool _loaded;

    public CleanerPage()
    {
        InitializeComponent();
        // Auto-load on first appearance using whatever path settings resolves to
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;

            AppSettings.Reload();
            var paths = AppSettings.Instance.ResolveDatabasePaths().ToList();
            if (paths.Count == 0) paths.Add(Path.Combine(AppContext.BaseDirectory, "Winapp2.ini"));
            await ViewModel.LoadWinapp2Async(paths);
        };
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

        Add("Select all",      () => ViewModel.SelectAllCommand.Execute(null));
        Add("Select none",     () => ViewModel.SelectNoneCommand.Execute(null));
        Add("Select defaults", () => ViewModel.SelectDefaultsCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add("Expand all",      () => ViewModel.ExpandAllCommand.Execute(null));
        Add("Collapse all",    () => ViewModel.CollapseAllCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add("Sort by size ↓",  () => ViewModel.SortResultsDescCommand.Execute(null));
        Add("Sort by size ↑",  () => ViewModel.SortResultsAscCommand.Execute(null));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add("Refresh",         () => ViewModel.RefreshCommand.Execute(null));
    }

    // Open the detail view for the clicked result row
    private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScanResultLine line && line.Result is not null)
            ViewModel.SelectedResultLine = line;
    }

    // Click a path row in the detail list;just highlight the file in Explorer.
    // Headers and registry keys are ignored; only real file paths get /select treatment.
    private void DetailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DetailLine { IsHeader: false } line) return;
        var path = line.Text;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return;

        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // Entry flyout; Tag="{x:Bind}" gives us the CleanerEntryViewModel directly
    private async void EntryAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
            await ViewModel.AnalyzeSingleEntryAsync(vm);
    }

    private async void EntryClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerEntryViewModel vm })
        {
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForEntry(vm)))
                return;

            await ViewModel.CleanSingleEntryAsync(vm);
        }
    }

    // Ask Groq to explain the entry;result is cached so repeated opens are instant
    private async void EntryExplain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: CleanerEntryViewModel vm }) return;

        var textBlock = new TextBlock
        {
            Text = "Thinking…",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = vm.Name,
            CloseButtonText = "Close",
            Content = textBlock
        };

        // Show the dialog immediately (don't await), then fill in the answer
        var showTask = dialog.ShowAsync().AsTask();
        textBlock.Text = await AiExplainer.ExplainAsync(vm.Entry);
        await showTask;
    }


    // Category flyout; same trick with CleanerCategoryViewModel
    private async void CatAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
            await ViewModel.AnalyzeCategoryAsync(vm);
    }

    private async void CatClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CleanerCategoryViewModel vm })
        {
            if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForCategory(vm)))
                return;

            await ViewModel.CleanCategoryAsync(vm);
        }
    }

    private async void RunCleaner_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.RunCleanerCommand.CanExecute(null))
            return;

        if (!await ConfirmWarningsAsync(ViewModel.GetWarningsForSelectedEntries()))
            return;

        await ((IAsyncRelayCommand)ViewModel.RunCleanerCommand).ExecuteAsync(null);
    }

    private async Task<bool> ConfirmWarningsAsync(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Cleaning warning",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text =
                        "The selected Winapp2 entries include the following warnings:" +
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        string.Join($"{Environment.NewLine}{Environment.NewLine}", warnings),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Show/hide the [...] button when hovering over a category header or entry row.
    // the buttons sit at Opacity="0" 
    private void CatHeader_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 1);
    private void CatHeader_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 0);
    private void EntryRow_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 1);
    private void EntryRow_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        SetMenuButtonOpacity(sender, 0);

    private static void SetMenuButtonOpacity(object sender, double opacity)
    {
        if (sender is Grid g)
            foreach (var btn in g.Children.OfType<Button>())
                btn.Opacity = opacity;
    }
}
