using FluentCleaner.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentCleaner;

public sealed partial class MainWindow : Window
{
    public string InsiderSubtitle => AppInfo.InsiderSubtitle;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(CleanerPage));
        SyncSearchState();                           //enable/disable search for initial page
        SizeChanged += MainWindow_SizeChanged;       //watch for window resize; compact search
        UpdateTitleSearch(AppWindow.Size.Width);     //apply correct search mode on first load
    }

    // --- Navigation -----------------------------------------------------------

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        // Reset search on every page change
        TitleSearchBox.Text = "";
        if (ContentFrame.Content is ISearchablePage old)
            old.OnSearch("");

        Type? page = item.Tag?.ToString() switch
        {
            "Cleaner" => typeof(CleanerPage),
            "Terminal" => typeof(TerminalPage),
            "Tools" => typeof(ToolsPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (page is not null)
            ContentFrame.Navigate(page, null, new DrillInNavigationTransitionInfo());//zoom in transition 

        SyncSearchState();
    }

    // disable search controls on pages that don't support it
    private void SyncSearchState()
    {
        bool searchable = ContentFrame.Content is ISearchablePage;
        TitleSearchBox.IsEnabled = searchable;
        SearchIconButton.IsEnabled = searchable;
    }

    // --- Page actions flyout --------------------------------------------------

    // Rebuilt every time the flyout opens so it always reflects the current page.
    private void PageActionsFlyout_Opening(object sender, object e)
    {
        PageActionsFlyout.Items.Clear();

        if (ContentFrame.Content is IPageActions provider)
            provider.BuildActions(PageActionsFlyout);
        else
        {
            PageActionsFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "No actions for this page",
                IsEnabled = false
            });
        }
    }

    // --- Search ---------------------------------------------------------------

    private void TitleSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput
            && ContentFrame.Content is ISearchablePage page)
            page.OnSearch(sender.Text);
    }

    // --- Search collapse -----------------------------------------------------

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args) =>
        UpdateTitleSearch(args.Size.Width);

    //below 560 px:collapse to icon + flyout; between 560-700: shrink box; above: full width
    private void UpdateTitleSearch(double width)
    {
        bool compact = width < 560;
        TitleSearchBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchIconButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        if (!compact)
            TitleSearchBox.Width = width < 700 ? 220 : 280;
    }
}