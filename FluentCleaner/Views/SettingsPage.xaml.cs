using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentCleaner.Views;

public sealed partial class SettingsPage : Page, IPageActions
{
    private static readonly HttpClient _http = new();
    private string? _updateVersion; // null = up to date, string = new version available

    public SettingsPageViewModel ViewModel { get; } = new();
    public string AppVersion => AppInfo.DisplayVersion;
    public Visibility InsiderBadgeVisibility => AppInfo.IsInsider ? Visibility.Visible : Visibility.Collapsed;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ViewModel.Refresh();
            await CheckForUpdateAsync(silent: true);
        };
    }

    // --- Update check --------------------------------------------

    private async Task CheckForUpdateAsync(bool silent = false)
    {
        try
        {
            var latest = (await _http.GetStringAsync(
                "https://raw.githubusercontent.com/builtbybel/FluentCleaner/main/version.txt"))
                .Trim();

            _updateVersion = Version.TryParse(latest, out var remote) &&
                             Version.TryParse(AppInfo.VersionString, out var local) &&
                             remote > local ? latest : null;
        }
        catch { _updateVersion = null; }

        if (_updateVersion is not null)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title   = $"Update available — {_updateVersion}";
            UpdateBar.Message = "A new version of FluentCleaner is ready to download.";

            var btn = new Button { Content = "Download", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            btn.Click += async (_, _) => await AppLinks.OpenAsync(AppLinks.Releases);
            UpdateBar.ActionButton = btn;
            UpdateBar.IsOpen = true;
        }
        else if (!silent)
        {
            UpdateBar.Severity     = InfoBarSeverity.Success;
            UpdateBar.Title        = "You're up to date";
            UpdateBar.Message      = $"Version {AppInfo.DisplayVersion} is the latest.";
            UpdateBar.ActionButton = null;
            UpdateBar.IsOpen       = true;
        }
    }

    // --- IPageActions --------------------------------------------

    public void BuildActions(MenuFlyout flyout)
    {
        if (_updateVersion is not null)
        {
            var updateItem = new MenuFlyoutItem { Text = $"⬆  Update available — {_updateVersion}" };
            updateItem.Click += async (_, _) => await AppLinks.OpenAsync(AppLinks.Releases);
            flyout.Items.Add(updateItem);
        }
        else
        {
            var checkItem = new MenuFlyoutItem { Text = "Check for updates" };
            checkItem.Click += async (_, _) => await CheckForUpdateAsync();
            flyout.Items.Add(checkItem);
        }
    }

    private void DonationBanner_Dismiss(object sender, RoutedEventArgs e) =>
        DonationBanner.IsOpen = false;

    private async void Link_GitHub(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.GitHub);
    private async void Link_Issues(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.Issues);
    private async void Link_Releases(object sender, RoutedEventArgs e) => await AppLinks.OpenAsync(AppLinks.Releases);
    private async void Link_Donate(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.Donate);

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".ini");

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.CustomPath = file.Path;
    }
}
