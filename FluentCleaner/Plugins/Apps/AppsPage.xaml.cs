using FluentCleaner.Services;
using Windows.ApplicationModel.DataTransfer;
using FluentCleaner.Views;   // ISearchablePage, IPageActions
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;   // RightTappedRoutedEventArgs
using Microsoft.UI.Xaml.Controls.Primitives;   // FlyoutShowOptions

namespace FluentCleaner.Plugins.Apps;

/// <summary>
/// Uninstaller for store packages and classic programs. Windows reports what is installed;
/// AppRules.ini only classifies how safe each one is to remove.
/// </summary>
public sealed partial class AppsPage : Page, ISearchablePage, IPageActions
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

    // --- removing ------------------------------------------------------------

    // Uninstalling cannot be undone from here, so the names go on screen once more and the
    // user confirms before anything is started. The dialog lists them rather than counting
    // them: a wrong tick is much easier to spot in a name than in a number.
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

    // One dialog for both ways in, so a right-click can never be the quieter route to the
    // same irreversible thing.
    private async Task<bool> ConfirmAsync(List<string> picked)
    {
        var listed = string.Join("\n", picked.Take(15));
        if (picked.Count > 15) listed += "\n" + ResourceService.Fmt("St_AppsAndMore", picked.Count - 15);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            // Without this a dialog built in code renders light while the app is dark.
            RequestedTheme = ActualTheme,
            Title = picked.Count == 1
                ? ResourceService.Fmt("St_AppsConfirmTitleOne", picked[0])
                : ResourceService.Fmt("St_AppsConfirmTitle", picked.Count),
            // A single app is already named in the title; listing it again reads as a stutter.
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

    private MenuFlyout? _rowMenu;            // built once, reused by every row
    private MenuFlyoutItem? _removeItem;     // disabled for anything Windows protects
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
        // Same confirmation as the button: one app is still one uninstall that cannot be undone.
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

        // Only what acts on the clicked row; select all and select none are list actions and
        // stay in the [...] menu, where they already were.
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
        void Add(string label, Action action)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }

        Add(ResourceService.Get("St_MenuSelectAll"), () => SetAll(true));
        Add(ResourceService.Get("St_MenuSelectNone"), () => SetAll(false));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(ResourceService.Get("St_MenuRefresh"), async () => await ViewModel.ScanAsync());
    }

    // Only what Windows lets go: anything protected stays unticked even when the whole list
    // is picked, so the confirmation can never be handed an app that cannot be removed.
    private void SetAll(bool selected)
    {
        foreach (var row in ViewModel.Rows)
            row.IsSelected = selected && row.CanRemove;
    }
}
