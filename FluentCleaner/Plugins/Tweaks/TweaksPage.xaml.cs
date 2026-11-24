using System.Diagnostics;
using FluentCleaner.Services;
using FluentCleaner.Views;   // ISearchablePage, IPageActions
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;   // RightTappedRoutedEventArgs
using Microsoft.UI.Xaml.Controls.Primitives;   // FlyoutShowOptions
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;

namespace FluentCleaner.Plugins.Tweaks;

//tweaks tab;rules are in WinRules.ini, this page lists them and runs the Applier
public sealed partial class TweaksPage : Page, ISearchablePage, IPageActions, IRefreshablePage
{
    public TweaksPageViewModel ViewModel { get; } = new();

    private bool _scanned;

    public TweaksPage()
    {
        InitializeComponent();

        // scan once on first load, not again when the tab comes back
        Loaded += async (_, _) =>
        {
            if (_scanned) return;
            _scanned = true;
            await ViewModel.ScanAsync();
        };
    }

    private void Rules_Click(object sender, RoutedEventArgs e) =>
        PluginPaths.Reveal("Tweaks", "WinRules.ini");

    // --- row context menu ----------------------------------------------------

    private MenuFlyout? _rowMenu;          // one menu for all rows
    private MenuFlyoutItem? _applyItem;    // text changes per row (apply/reset)
    private TweakRowViewModel? _menuRow;

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not TweakRowViewModel row) return;

        // right-clicked row gets selected too
        _menuRow = row;
        ViewModel.SelectedRow = row;
        _rowMenu ??= BuildRowMenu();
        _applyItem!.Text = row.ActionLabel;

        _rowMenu.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private MenuFlyout BuildRowMenu()
    {
        _applyItem = new MenuFlyoutItem();
        _applyItem.Click += async (_, _) =>
        {
            if (_menuRow is { } row) await ViewModel.ApplyOneAsync(row);
        };

        var copy = new MenuFlyoutItem { Text = ResourceService.Get("MenuCopyRegPath/Text") };
        copy.Click += (_, _) => CopyPath();

        var regedit = new MenuFlyoutItem { Text = ResourceService.Get("MenuOpenRegedit/Text") };
        regedit.Click += (_, _) => OpenRegedit();

        // only row stuff here, select all/none is in the [...] menu
        var menu = new MenuFlyout();
        menu.Items.Add(_applyItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(copy);
        menu.Items.Add(regedit);
        return menu;
    }

    private void CopyPath()
    {
        if (_menuRow is not { } row) return;
        var package = new DataPackage();
        package.SetText(row.RegistryPaths);
        Clipboard.SetContent(package);
        ViewModel.StatusText = ResourceService.Get("St_TweaksPathCopied");
    }

    // regedit opens at LastKey, so set that first (key only, no value name)
    private void OpenRegedit()
    {
        if (_menuRow?.KeyPath is not { Length: > 0 } key) return;
        try
        {
            using var reg = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", true);
            reg?.SetValue("LastKey", key, RegistryValueKind.String);
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch (Exception ex) { ViewModel.StatusText = ex.Message; }
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

    // Forget() = re-read the ini files, new rules show up without restarting the app
    public async void Refresh()
    {
        if (ViewModel.IsBusy) return;
        Tweaks.TweakDatabase.Forget();
        await ViewModel.ScanAsync();
    }

    private void SetAll(bool selected)
    {
        foreach (var row in ViewModel.Rows) row.IsSelected = selected;
    }
}
