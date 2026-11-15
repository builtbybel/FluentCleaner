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

/// <summary>
/// Registry tweak catalogue. The rules live in Rules\*.ini and are read by the engine in
/// <see cref="Tweaks"/>; this page only lists them and runs the applier.
/// </summary>
public sealed partial class TweaksPage : Page, ISearchablePage, IPageActions
{
    public TweaksPageViewModel ViewModel { get; } = new();

    private bool _scanned;

    public TweaksPage()
    {
        InitializeComponent();

        // First visit checks the PC once; a tab that is reopened keeps what it already found.
        Loaded += async (_, _) =>
        {
            if (_scanned) return;
            _scanned = true;
            await ViewModel.ScanAsync();
        };
    }

    // --- row context menu ----------------------------------------------------

    private MenuFlyout? _rowMenu;          // built once, reused by every row
    private MenuFlyoutItem? _applyItem;    // its label follows the row it was opened on
    private TweakRowViewModel? _menuRow;

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not TweakRowViewModel row) return;

        // The menu acts on this row, so the selection and the description follow it.
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

        // Row actions only; select all/none are list actions and live in the [...] menu.
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

    // Regedit reopens wherever it was last left, and that is stored in LastKey — writing it
    // first is the documented way to send it somewhere. Only the key, not the value name.
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
        void Add(string label, Action action)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }

        Add(ResourceService.Get("St_MenuSelectAll"), () => SetAll(true));
        Add(ResourceService.Get("St_MenuSelectNone"), () => SetAll(false));
        flyout.Items.Add(new MenuFlyoutSeparator());
        // Forget() drops the cached catalogue so a rule pack dropped into Rules\ shows up
        // without restarting the app.
        Add(ResourceService.Get("St_MenuRefresh"), async () =>
        {
            Tweaks.TweakDatabase.Forget();
            await ViewModel.ScanAsync();
        });
    }

    private void SetAll(bool selected)
    {
        foreach (var row in ViewModel.Rows) row.IsSelected = selected;
    }
}
