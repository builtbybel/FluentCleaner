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

        SyncSearchState();                           //enable/disable search for initial page
        SizeChanged += MainWindow_SizeChanged;       //watch for window resize; compact search
        UpdateTitleSearch(AppWindow.Size.Width);     //apply correct search mode on first load
    }

    // --- TitleBar pane toggle -------------------------------------------------

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    // --- Navigation -----------------------------------------------------------

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        TitleSearchBox.Text = "";
        if (NavFrame.Content is ISearchablePage old)
            old.OnSearch("");

        var transition = new DrillInNavigationTransitionInfo();//zoom in transition

        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage), null, transition);
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "Cleaner":  NavFrame.Navigate(typeof(CleanerPage),  null, transition); break;
                case "Terminal": NavFrame.Navigate(typeof(TerminalPage), null, transition); break;
                case "Tools":    NavFrame.Navigate(typeof(ToolsPage),    null, transition); break;
            }
        }

        SyncSearchState();
    }


    // --- Page actions flyout --------------------------------------------------

    // Rebuilt every time the flyout opens so it always reflects the current page.
    private void PageActionsFlyout_Opening(object sender, object e)
    {
        PageActionsFlyout.Items.Clear();

        if (NavFrame.Content is IPageActions provider)
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

    // disable search controls on pages that don't support it
    private void SyncSearchState()
    {
        bool searchable = NavFrame.Content is ISearchablePage;
        TitleSearchBox.IsEnabled = searchable;
        SearchIconButton.IsEnabled = searchable;
    }

    private void TitleSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput
            && NavFrame.Content is ISearchablePage page)
            page.OnSearch(sender.Text);
    }

    // --- Search collapse -----------------------------------------------------

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args) =>
        UpdateTitleSearch(args.Size.Width);

    //below 560 px:collapse to icon + flyout; between 560-700:shrink box; above:full width
    private void UpdateTitleSearch(double width)
    {
        bool compact = width < 560;
        TitleSearchBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchIconButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        if (!compact)
            TitleSearchBox.Width = width < 700 ? 220 : 280;
    }
}