using FluentCleaner.Services;
using Windows.ApplicationModel.DataTransfer;
using FluentCleaner.Views;   // ISearchablePage, IPageActions
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;   // RightTappedRoutedEventArgs
using Microsoft.UI.Xaml.Controls.Primitives;   // FlyoutShowOptions

namespace FluentCleaner.Plugins.Apps;

//apps tab: uninstall store apps and programs
public sealed partial class AppsPage : Page, ISearchablePage, IPageActions, IRefreshablePage
{
    public AppsPageViewModel ViewModel { get; } = new();

    private bool _scanned;

    public AppsPage()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (_scanned) return;
            _scanned = true;
            await ViewModel.ScanAsync();
        };
    }

    private void Rules_Click(object sender, RoutedEventArgs e) =>
        PluginPaths.Reveal("Apps", "AppRules.ini");

    // --- removing ------------------------------------------------------------

    // can't be undone, so confirm first;list the names, a wrong tick is easier to spot than in a number
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var picked = ViewModel.Rows.Where(row => row.IsSelected && row.CanRemove)
                                   .Select(row => row.Name).ToList();
        if (picked.Count == 0)
        {
            ViewModel.StatusText = ResourceService.Get("St_AppsNothingPicked");
            return;
        }

        if (await ConfirmAsync(picked)) await ViewModel.RemoveAsync();
    }

    // same dialog for button and right-click
    private async Task<bool> ConfirmAsync(List<string> picked)
    {
        var listed = string.Join("\n", picked.Take(15));
        if (picked.Count > 15) listed += "\n" + ResourceService.Fmt("St_AppsAndMore", picked.Count - 15);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            // else it's light in dark mode
            RequestedTheme = ActualTheme,
            Title = picked.Count == 1
                ? ResourceService.Fmt("St_AppsConfirmTitleOne", picked[0])
                : ResourceService.Fmt("St_AppsConfirmTitle", picked.Count),
            // one app is already in the title
            Content = picked.Count == 1
                ? null
                : new TextBlock { Text = listed, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = ResourceService.Get("St_AppsConfirmRemove"),
            CloseButtonText = ResourceService.Get("St_AppsConfirmCancel"),
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // --- row context menu ----------------------------------------------------

    private MenuFlyout? _rowMenu;            // one menu for all rows
    private MenuFlyoutItem? _removeItem;     // disabled for protected apps
    private AppRowViewModel? _menuRow;

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not AppRowViewModel row) return;

        _menuRow = row;
        _rowMenu ??= BuildRowMenu();
        _removeItem!.IsEnabled = row.CanRemove;

        _rowMenu.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private MenuFlyout BuildRowMenu()
    {
        _removeItem = new MenuFlyoutItem { Text = ResourceService.Get("MenuUninstallOne/Text") };
        // same confirm as the button
        _removeItem.Click += async (_, _) =>
        {
            if (_menuRow is not { } row || !row.CanRemove) return;
            if (await ConfirmAsync([row.Name])) await ViewModel.RemoveOneAsync(row);
        };

        var copy = new MenuFlyoutItem { Text = ResourceService.Get("MenuCopyAppId/Text") };
        copy.Click += (_, _) =>
        {
            if (_menuRow is not { } row) return;
            var package = new DataPackage();
            package.SetText(row.Identifier);
            Clipboard.SetContent(package);
            ViewModel.StatusText = ResourceService.Get("St_AppsIdCopied");
        };

        // only row stuff here, select all/none is in the [...] menu
        var menu = new MenuFlyout();
        menu.Items.Add(_removeItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(copy);
        return menu;
    }

    // --- ISearchablePage -----------------------------------------------------

    public void OnSearch(string text) => ViewModel.SearchText = text;

    // --- IPageActions --------------------------------------------------------

    public void BuildActions(MenuFlyout flyout)
    {
        flyout.AddItem(ResourceService.Get("St_MenuSelectAll"), () => SetAll(true));
        flyout.AddItem(ResourceService.Get("St_MenuSelectNone"), () => SetAll(false));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.AddItem(ResourceService.Get("St_MenuRefresh"), Refresh);
    }

    public async void Refresh() => await ViewModel.ScanAsync();

    // protected apps never get ticked
    private void SetAll(bool selected)
    {
        foreach (var row in ViewModel.Rows)
            row.IsSelected = selected && row.CanRemove;
    }
}
