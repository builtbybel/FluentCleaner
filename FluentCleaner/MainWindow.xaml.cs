using FluentCleaner.Services;
using FluentCleaner.Plugins.Apps;
using FluentCleaner.Plugins.Tweaks;
using FluentCleaner.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentCleaner;

public sealed partial class MainWindow : Window
{
    // One place for glyph and label, shared by the tab and the "+" menu.
    private sealed record PageKind(Type PageType, string Glyph, string LabelKey, string Fallback, string? Image = null);

    private const string AppIcon = "ms-appx:///Assets/AppIcon.ico";

    private static readonly PageKind[] NewTabKinds =
    [
        new(typeof(CleanerPage),  "", "NavCleaner/Content",  "Cleaner", AppIcon),
        new(typeof(TweaksPage),   "", "NavTweaks/Content",   "Tweaks"),
        new(typeof(AppsPage),     "", "NavApps/Content",     "Apps"),
        new(typeof(TerminalPage), "", "NavTerminal/Content", "Terminal"),
        new(typeof(CustomPage),   "", "NavCustom/Content",   "Custom"),
    ];

    private static readonly PageKind SettingsKind =
        new(typeof(SettingsPage), "", "SettingsTitle/Text", "Settings");

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        SetTitleBar(TitleDragRegion);

        InsiderLabel.Text = AppInfo.InsiderSubtitle;
        AppMenuSearch.Text = ResourceService.Get("SearchBox/PlaceholderText");        // Queued so the window shows without waiting ~50 ms for the page.
        DispatcherQueue.TryEnqueue(() => AddTab(NewTabKinds[0]));

        //No ~800 ms slide-in for new tabs;they appear with their page, like Terminal
        Tabs.Loaded += (_, _) =>
        {
            if (FindDescendant<ListView>(Tabs) is { } strip)
                strip.ItemContainerTransitions = [];
        };

        SizeChanged += (_, _) => UpdateCaptionInset();
        UpdateCaptionInset();
    }

    // --- Tabs -----------------------------------------------------------------

    private Frame? ActiveFrame => (Tabs.SelectedItem as TabViewItem)?.Tag as Frame;

    private object? ActivePage => ActiveFrame?.Content;

    //opens a page as new tab and returns it (Rule Lab uses it)
    public T OpenTab<T>(string header) where T : Page =>
        (T)AddTab(new PageKind(typeof(T), "", "", header)).Content;   //test beaker, like "Test in Rule Lab"

    private Frame AddTab(PageKind kind)
    {
        var frame = new Frame();

        //No page transition: it read as lag
        frame.Navigate(kind.PageType, null, new SuppressNavigationTransitionInfo());

        //MinWidth = Explorer tab width, MaxWidth + trimming for long Rule Lab names
        var label = Label(kind);
        var tab = new TabViewItem
        {
            Header = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis },
            IconSource = Icon(kind),
            Tag = frame,
            MinWidth = 180,
            MaxWidth = 240
        };
        ToolTipService.SetToolTip(tab, label);
        Tabs.TabItems.Add(tab);

        Tabs.SelectedIndex = Tabs.TabItems.Count - 1;
        return frame;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    // Coloured image where set, like Explorer's tabs; otherwise the mono glyph
    private static IconSource Icon(PageKind kind) => kind.Image is { } uri
        ? new ImageIconSource { ImageSource = new BitmapImage(new Uri(uri)) }
        : new FontIconSource { Glyph = kind.Glyph };

    private static string Label(PageKind kind)
    {
        if (kind.LabelKey.Length == 0) return kind.Fallback;   //OpenTab passes its header as is
        var text = ResourceService.Get(kind.LabelKey);
        return text == kind.LabelKey ? kind.Fallback : text;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SearchBox.Text = "";
        UpdateSearchIndicator();
        if (e.RemovedItems.FirstOrDefault() is TabViewItem { Tag: Frame old }
            && old.Content is ISearchablePage searchable)
            searchable.OnSearch("");

        PageHost.Child = ActiveFrame;

        SyncSearchState();
    }

    //The last tab can't be closed
    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (Tabs.TabItems.Count > 1)
            Tabs.TabItems.Remove(args.Tab);
    }

    private void NewTabFlyout_Opening(object sender, object e)
    {
        NewTabFlyout.Items.Clear();

        foreach (var kind in NewTabKinds)
        {
            var item = new MenuFlyoutItem
            {
                Text = Label(kind),
                Icon = Icon(kind).CreateIconElement()
            };
            item.Click += (_, _) => AddTab(kind);
            NewTabFlyout.Items.Add(item);
        }
    }

    // Settings opens only once; a second click selects the open tab
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var open = Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(
            t => t.Tag is Frame f && f.SourcePageType == typeof(SettingsPage));

        if (open is not null) Tabs.SelectedItem = open;
        else AddTab(SettingsKind);
    }

    private async void Donate_Click(object sender, RoutedEventArgs e) =>
        await AppLinks.OpenAsync(AppLinks.Donate);

    private async void GitHub_Click(object sender, RoutedEventArgs e) =>
        await AppLinks.OpenAsync(AppLinks.GitHub);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await AppLinks.OpenAsync(AppLinks.CreateUpdateUrl("modern", AppInfo.VersionString));

    // --- App menu (∨) -----------------------------------------------------------

    private int _pageActionCount;   //items currently inserted above the separator

    // The active page's actions on top, rebuilt on every open.
    private void AppMenu_Opening(object sender, object e)
    {
        for (int i = 0; i < _pageActionCount; i++)
            AppMenu.Items.RemoveAt(0);
        _pageActionCount = 0;

        if (ActivePage is IPageActions provider)
        {
            // Built in a spare flyout and moved over: an item can have only one parent
            var scratch = new MenuFlyout();
            provider.BuildActions(scratch);
            var items = scratch.Items.ToList();
            scratch.Items.Clear();
            foreach (var item in items)
                AppMenu.Items.Insert(_pageActionCount++, item);
        }

        AppMenuSeparator.Visibility = _pageActionCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ActivePage is not IRefreshablePage page) return;
        page.Refresh();
        args.Handled = true;
    }

    // --- Search ---------------------------------------------------------------

    // Disabled on pages without search, which also turns off Ctrl+F.
    private void SyncSearchState() => AppMenuSearch.IsEnabled = ActivePage is ISearchablePage;

    // Queued so the menu has closed before the search box opens.
    private void Search_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => FlyoutBase.ShowAttachedFlyout(AppMenuButton));

    private void SearchFlyout_Opened(object sender, object e) =>
        SearchBox.Focus(FocusState.Programmatic);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput
            && ActivePage is ISearchablePage page)
            page.OnSearch(sender.Text);

        UpdateSearchIndicator();
    }

    public void ClearSearch()
    {
        SearchBox.Text = "";
        UpdateSearchIndicator();
        if (ActivePage is ISearchablePage page) page.OnSearch("");
    }

    // Two glyphs instead of recolouring one, so both follow a theme switch.
    private void UpdateSearchIndicator()
    {
        bool active = !string.IsNullOrEmpty(SearchBox.Text);
        MenuGlyph.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        MenuGlyphActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    // Width of the caption buttons. RightInset is in physical pixels, hence the scale
    private void UpdateCaptionInset()
    {
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0) scale = 1.0;

        CaptionSpacer.Width = AppWindow.TitleBar.RightInset / scale;
    }
}
