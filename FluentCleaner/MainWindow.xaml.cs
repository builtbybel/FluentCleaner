using FluentCleaner.Plugins.Apps;
using FluentCleaner.Plugins.Tweaks;
using FluentCleaner.Services;
using FluentCleaner.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace FluentCleaner;

public sealed partial class MainWindow : Window
{
    public string InsiderSubtitle   => AppInfo.InsiderSubtitle;
    public string VersionFooterText => $"v{AppInfo.DisplayVersion} · Open Source";

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            var crashPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FluentCleaner", "crash.log");
            System.IO.File.AppendAllText(crashPath, $"[INIT COMPONENT FAILED {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\nMessage: {ex.Message}\nHResult: 0x{ex.HResult:X8}\n{ex}\n\n");
            throw;
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        SyncSearchState();
        SizeChanged += MainWindow_SizeChanged;
        UpdateTitleSearch(AppWindow.Size.Width);

        NavView.SelectedItem = NavItemCleaner;
        NavFrame.Navigate(typeof(CleanerPage));
    }

    // --- Keyboard Accelerators ------------------------------------------------

    private void AccelFocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (TitleSearchBox.IsEnabled && TitleSearchBox.Visibility == Visibility.Visible)
            TitleSearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void AccelNav_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigationViewItem? target = sender.Key switch
        {
            VirtualKey.Number1 => NavItemCleaner,
            VirtualKey.Number2 => NavItemApps,
            VirtualKey.Number3 => NavItemTweaks,
            VirtualKey.Number4 => NavItemCustom,
            VirtualKey.Number5 => NavItemTerminal,
            _                  => null
        };
        if (target is not null)
        {
            NavView.SelectedItem = target;
            args.Handled = true;
        }
    }

    // --- TitleBar pane toggle -------------------------------------------------

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    // --- Navigation -----------------------------------------------------------

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        TitleSearchBox.Text = "";
        var prevContent = NavFrame.Content;
        if (prevContent is ISearchablePage old)
            old.OnSearch("");

        var transition = new EntranceNavigationTransitionInfo();

        if (args.IsSettingsSelected)
        {
            NavigateToSettings("General", transition);
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (tag.StartsWith("Settings:", StringComparison.OrdinalIgnoreCase))
            {
                var section = tag["Settings:".Length..];
                NavigateToSettings(section, transition);
            }
            else
            {
                switch (tag)
                {
                    case "Cleaner":  NavFrame.Navigate(typeof(CleanerPage),  null, transition); break;
                    case "Apps":     NavFrame.Navigate(typeof(AppsPage),     null, transition); break;
                    case "Tweaks":   NavFrame.Navigate(typeof(TweaksPage),   null, transition); break;
                    case "Custom":   NavFrame.Navigate(typeof(CustomPage),   null, transition); break;
                    case "Terminal": NavFrame.Navigate(typeof(TerminalPage), null, transition); break;
                }
            }
        }

        SyncSearchState();
    }

    private void NavigateToSettings(string section, NavigationTransitionInfo transition)
    {
        if (NavFrame.Content is SettingsPage existing)
        {
            existing.NavigateToSection(section);
        }
        else
        {
            NavFrame.Navigate(typeof(SettingsPage), section, transition);
        }
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "GitHub" })
            await AppLinks.OpenAsync(AppLinks.GitHub);
    }

    // --- Page actions flyout --------------------------------------------------

    private void PageActionsFlyout_Opening(object sender, object e)
    {
        PageActionsFlyout.Items.Clear();

        if (NavFrame.Content is IPageActions provider)
            provider.BuildActions(PageActionsFlyout);
        else
        {
            PageActionsFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = ResourceService.Get("St_NoActions"),
                IsEnabled = false
            });
        }
    }

    // --- Search ---------------------------------------------------------------

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

    private void UpdateTitleSearch(double width)
    {
        bool compact = width < 580;
        TitleSearchBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchIconButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        if (!compact)
            TitleSearchBox.Width = width < 760 ? 220 : 320;
    }
}
