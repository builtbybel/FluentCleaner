using System.Diagnostics;
using System.Globalization;
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
    private static readonly Dictionary<string, int[]> DonationAmounts = new()
    {
        ["EUR"] = [5, 10, 25, 50, 100, 200, 500, 1000],
        ["USD"] = [5, 10, 25, 50, 100, 200, 500, 1000],
        ["GBP"] = [5, 10, 25, 50, 100, 200, 500, 1000],
        ["CHF"] = [5, 10, 25, 50, 100, 200, 500, 1000],
        ["CAD"] = [10, 20, 50, 100, 200, 400, 750, 1500],
        ["AUD"] = [10, 20, 50, 100, 200, 400, 750, 1500],
        ["JPY"] = [500, 1000, 2500, 5000, 10000, 20000, 50000, 100000, 150000]
    };
    private string? _updateVersion; // null = up to date, string = new version available
    private bool _pageReady; // true when the page has finished loading and is ready to handle events
    private string _groqKey = "";
    private string _openAiKey = "";
    private string _anthropicKey = "";
    private string _currentProvider = "Groq";
    private bool _navigationReady;

    public SettingsPageViewModel ViewModel { get; } = new();
    public string AppVersion => AppInfo.DisplayVersion;
    public Visibility InsiderBadgeVisibility => AppInfo.IsInsider ? Visibility.Visible : Visibility.Collapsed;

    public SettingsPage()
    {
        InitializeComponent();
        _navigationReady = true;
        ShowSettingsSection("General");
        InitializeDonationOptions();
        AiProviderBox.ItemsSource = new[] { "Groq", "OpenAI", "Anthropic" };
        Loaded += async (_, _) =>
        {
            _pageReady = false;
            ViewModel.Refresh();                                    //sync database toggles, paths, theme
            await CheckForUpdateAsync(silent: true);               //silent update check; banner only if newer version found
            LoadAiSettings();
            await LoadSchedulerSettingsAsync();

            // Translator credit: hidden when the language file leaves it empty.
            var credit = ResourceService.Get("LblTranslatorCredit");
            var hasCredit = !string.IsNullOrWhiteSpace(credit) && credit != "LblTranslatorCredit";
            lblTranslatorCredit.Text       = hasCredit ? credit : "";
            lblTranslatorCredit.Visibility = hasCredit ? Visibility.Visible : Visibility.Collapsed;

            _pageReady = true;
        };
    }

    // The settings page has one local navigator and one content surface. Keeping the
    // sections here avoids a second Frame/router while still killing the long one-pager.
    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_navigationReady || SettingsNav.SelectedItem is not ListViewItem item) return;
        ShowSettingsSection(item.Tag as string ?? "General");
    }

    private void ShowSettingsSection(string section)
    {
        AppearanceSection.Visibility = DatabaseSection.Visibility =
            section == "General" ? Visibility.Visible : Visibility.Collapsed;
        CookiesSection.Visibility = section == "Cookies" ? Visibility.Visible : Visibility.Collapsed;
        ExclusionsSection.Visibility = section == "Exclusions" ? Visibility.Visible : Visibility.Collapsed;
        PostCleanSection.Visibility = section == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = section == "History" ? Visibility.Visible : Visibility.Collapsed;
        AiSection.Visibility = section == "AI" ? Visibility.Visible : Visibility.Collapsed;
        SchedulerSection.Visibility = section == "Scheduler" ? Visibility.Visible : Visibility.Collapsed;
        BackupSection.Visibility = AboutSection.Visibility =
            section == "About" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.ChangeView(null, 0, null, true);
    }

    private async void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageReady) return;

        var result = await new ContentDialog
        {
            XamlRoot          = XamlRoot,
            RequestedTheme    = ActualTheme,
            Title             = ResourceService.Get("DlgRestartTitle"),
            Content           = ResourceService.Get("DlgRestartMessage"),
            PrimaryButtonText = ResourceService.Get("DlgRestartNow"),
            CloseButtonText   = ResourceService.Get("DlgRestartLater"),
            DefaultButton     = ContentDialogButton.Primary
        }.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            Process.Start(Environment.ProcessPath!);
            Application.Current.Exit();
        }
    }

    // --- Update check --------------------------------------------

    private async Task CheckForUpdateAsync(bool silent = false)
    {
        try
        {
            var latest = (await _http.GetStringAsync(AppLinks.VersionCheck))
                .Trim();

            _updateVersion = Version.TryParse(latest, out var remote) &&
                             Version.TryParse(AppInfo.VersionString, out var local) &&
                             remote > local ? latest : null;
        }
        catch { _updateVersion = null; }

        if (_updateVersion is not null)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title   = ResourceService.Fmt("St_UpdateAvailable", _updateVersion);
            UpdateBar.Message = ResourceService.Get("St_UpdateMessage");

            var btn = new Button { Content = ResourceService.Get("St_Download"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            btn.Click += async (_, _) => await AppLinks.OpenAsync(AppLinks.Releases);
            UpdateBar.ActionButton = btn;
            UpdateBar.IsOpen = true;
        }
        else if (!silent)
        {
            UpdateBar.Severity     = InfoBarSeverity.Success;
            UpdateBar.Title        = ResourceService.Get("St_UpToDate");
            UpdateBar.Message      = ResourceService.Fmt("St_UpToDateMessage", AppInfo.DisplayVersion);
            UpdateBar.ActionButton = null;
            UpdateBar.IsOpen       = true;
        }
    }

    // --- IPageActions --------------------------------------------

    public void BuildActions(MenuFlyout flyout)
    {
        if (_updateVersion is not null)
        {
            var updateItem = new MenuFlyoutItem { Text = ResourceService.Fmt("St_MenuUpdate", _updateVersion) };
            updateItem.Click += async (_, _) => await AppLinks.OpenAsync(AppLinks.Releases);
            flyout.Items.Add(updateItem);
        }
        else
        {
            var checkItem = new MenuFlyoutItem { Text = ResourceService.Get("St_MenuCheckUpdates") };
            checkItem.Click += async (_, _) => await CheckForUpdateAsync();
            flyout.Items.Add(checkItem);
        }
    }

    private void DonationBanner_Dismiss(object sender, RoutedEventArgs e) =>
        DonationBanner.IsOpen = false;

    private void InitializeDonationOptions()
    {
        DonationCurrencyBox.ItemsSource = DonationAmounts.Keys;

        var localCurrency = RegionInfo.CurrentRegion.ISOCurrencySymbol;
        DonationCurrencyBox.SelectedItem = DonationAmounts.ContainsKey(localCurrency)
            ? localCurrency
            : "EUR";
    }

    private void DonationCurrencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DonationCurrencyBox.SelectedItem is not string currency) return;

        DonationAmountBox.ItemsSource = DonationAmounts[currency]
            .Cast<object>()
            .Append(ResourceService.Get("Donation_OtherAmount"))
            .ToArray();
        DonationAmountBox.SelectedIndex = 1; // sensible second tier for every currency
    }

    private void DonationAmountBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DonationCurrencyBox.IsEnabled = DonationAmountBox.SelectedItem is int;

    private async void Link_GitHub(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.GitHub);
    private async void Link_Issues(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.Issues);
    private async void Link_Releases(object sender, RoutedEventArgs e) => await AppLinks.OpenAsync(AppLinks.Releases);
    private async void Link_Donate(object sender, RoutedEventArgs e)
    {
        if (DonationAmountBox.SelectedItem is string)
        {
            await AppLinks.OpenAsync(AppLinks.Donate);
            return;
        }

        if (DonationAmountBox.SelectedItem is not int amount ||
            DonationCurrencyBox.SelectedItem is not string currency)
            return;

        await AppLinks.OpenAsync(AppLinks.CreatePayPalDonationUrl(amount, currency));
    }
    private async void Link_KoFi(object sender, RoutedEventArgs e)     => await AppLinks.OpenAsync(AppLinks.KoFi);
    private async void Link_Faq(object sender, RoutedEventArgs e)        => await AppLinks.OpenAsync(AppLinks.Faq);
    private async void Link_IconCredit(object sender, RoutedEventArgs e) => await AppLinks.OpenAsync(AppLinks.IconCredit);

    private async Task LoadSchedulerSettingsAsync()
    {
        SchedulerFrequencyBox.ItemsSource = new[]
        {
            ResourceService.Get("Scheduler_FreqDaily"),
            ResourceService.Get("Scheduler_FreqWeekly"),
            ResourceService.Get("Scheduler_FreqLogon"),
        };

        var settings = AppSettings.Instance;
        SchedulerFrequencyBox.SelectedIndex = settings.ModernSchedulerFrequency switch
        {
            "Weekly" => 1,
            "Logon"  => 2,
            _        => 0,
        };

        SchedulerTimePicker.Time = TimeSpan.TryParse(settings.ModernSchedulerTime, out var time)
            ? time
            : new TimeSpan(3, 0, 0);
        SchedulerShutdownCheck.IsChecked = settings.ModernSchedulerShutdownAfter;

        var exists = await Task.Run(TaskSchedulerService.Exists);
        SchedulerEnabledToggle.IsOn = exists;
        SetSchedulerStatus(exists);
        SchedulerResultText.Text = "";
        UpdateSchedulerControls();
    }

    private void SchedulerEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_pageReady)
            UpdateSchedulerControls();
    }

    private void SchedulerFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageReady)
            UpdateSchedulerControls();
    }

    private void UpdateSchedulerControls()
    {
        var enabled = SchedulerEnabledToggle.IsOn;
        SchedulerFrequencyBox.IsEnabled = enabled;
        SchedulerTimePicker.IsEnabled = enabled && SchedulerFrequencyBox.SelectedIndex != 2;
        SchedulerShutdownCheck.IsEnabled = enabled;
    }

    private void SaveSchedulerSettings()
    {
        var settings = AppSettings.Instance;
        settings.ModernSchedulerFrequency = SchedulerFrequencyBox.SelectedIndex switch
        {
            1 => "Weekly",
            2 => "Logon",
            _ => "Daily",
        };
        settings.ModernSchedulerTime = SchedulerTimePicker.Time.ToString(@"hh\:mm");
        settings.ModernSchedulerShutdownAfter = SchedulerShutdownCheck.IsChecked == true;
        settings.Save();
    }

    private void SetSchedulerStatus(bool active) =>
        SchedulerStatusText.Text = ResourceService.Get(
            active ? "Scheduler_StatusActive" : "Scheduler_StatusInactive");

    private async void SchedulerApply_Click(object sender, RoutedEventArgs e)
    {
        SaveSchedulerSettings();
        SchedulerApplyButton.IsEnabled = false;
        SchedulerResultText.Text = ResourceService.Get("Scheduler_Applying");

        (bool Ok, string Message) result;
        if (!SchedulerEnabledToggle.IsOn)
        {
            result = await Task.Run(TaskSchedulerService.Delete);
        }
        else
        {
            var frequency = SchedulerFrequencyBox.SelectedIndex switch
            {
                1 => SchedulerFrequency.Weekly,
                2 => SchedulerFrequency.Logon,
                _ => SchedulerFrequency.Daily,
            };
            var scheduledTime = SchedulerTimePicker.Time;
            var shutdownAfter = SchedulerShutdownCheck.IsChecked == true;
            result = await Task.Run(() => TaskSchedulerService.CreateOrUpdate(
                frequency,
                scheduledTime,
                shutdownAfter));
        }

        SchedulerResultText.Text = $"{(result.Ok ? "✓" : "✗")} {result.Message}";
        SetSchedulerStatus(await Task.Run(TaskSchedulerService.Exists));
        SchedulerApplyButton.IsEnabled = true;
    }

    private void SchedulerOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
        }
        catch { }
    }

    private void LoadAiSettings()
    {
        _groqKey = AppSettings.Instance.GroqApiKey ?? "";
        _openAiKey = AppSettings.Instance.OpenAiApiKey ?? "";
        _anthropicKey = AppSettings.Instance.AnthropicApiKey ?? "";
        _currentProvider = AppSettings.Instance.AiProvider is "OpenAI" or "Anthropic"
            ? AppSettings.Instance.AiProvider : "Groq";

        AiProviderBox.SelectedItem = _currentProvider;
        ApplyProviderToUi();
    }

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageReady) return;

        StashCurrentKey();
        _currentProvider = AiProviderBox.SelectedItem as string ?? "Groq";
        ApplyProviderToUi();

        AppSettings.Instance.AiProvider = _currentProvider;
        AppSettings.Instance.Save();
    }

    private void StashCurrentKey()
    {
        switch (_currentProvider)
        {
            case "OpenAI": _openAiKey = ApiKeyBox.Password.Trim(); break;
            case "Anthropic": _anthropicKey = ApiKeyBox.Password.Trim(); break;
            default: _groqKey = ApiKeyBox.Password.Trim(); break;
        }
    }

    private void ApplyProviderToUi()
    {
        (ApiKeyBox.Password, ApiKeyBox.PlaceholderText, btnGetApiKey.NavigateUri) = _currentProvider switch
        {
            "OpenAI"    => (_openAiKey, "sk-...", new Uri("https://platform.openai.com/api-keys")),
            "Anthropic" => (_anthropicKey, "sk-ant-...", new Uri("https://console.anthropic.com/settings/keys")),
            _           => (_groqKey, "gsk_...", new Uri("https://console.groq.com/keys")),
        };

        lblApiTestResult.Text = "";
        lblApiTestResult.Visibility = Visibility.Collapsed;
    }

    // Saves every provider key so switching providers never drops an edit.
    private void ApiKeySave_Click(object sender, RoutedEventArgs e)
    {
        StashCurrentKey();
        AppSettings.Instance.AiProvider = _currentProvider;
        AppSettings.Instance.GroqApiKey = _groqKey.Length == 0 ? null : _groqKey;
        AppSettings.Instance.OpenAiApiKey = _openAiKey.Length == 0 ? null : _openAiKey;
        AppSettings.Instance.AnthropicApiKey = _anthropicKey.Length == 0 ? null : _anthropicKey;
        AppSettings.Instance.Save();
    }

    // Sends one short request instead of accepting a key based on its prefix.
    private async void ApiKeyTest_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { lblApiTestResult.Text = ResourceService.Get("St_ApiKeyMissing"); lblApiTestResult.Visibility = Visibility.Visible; return; }

        btnTestKey.IsEnabled        = false;
        lblApiTestResult.Text       = ResourceService.Get("St_ApiKeyTesting");
        lblApiTestResult.Visibility = Visibility.Visible;
        lblApiTestResult.Text       = await AiExplainer.TestKeyAsync(key, _currentProvider);
        btnTestKey.IsEnabled        = true;
    }

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

    // Shows the built-in protected paths read-only;these are always skipped, no matter what the database says
    private async void ProtectedPaths_Click(object sender, RoutedEventArgs e)
    {
        var list = new TextBox
        {
            Text            = string.Join("\n", CleaningService.ProtectedPaths),
            IsReadOnly      = true,
            FontFamily      = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize        = 12,
            TextWrapping    = TextWrapping.NoWrap,
            AcceptsReturn   = true,
            BorderThickness = new Thickness(0)
        };

        await new ContentDialog
        {
            XamlRoot        = XamlRoot,
            RequestedTheme  = ActualTheme,
            Title           = ResourceService.Get("DlgProtectedTitle"),
            Content         = new StackPanel
            {
                Spacing  = 10,
                Children =
                {
                    new TextBlock { Text = ResourceService.Get("DlgProtectedDesc"), TextWrapping = TextWrapping.Wrap },
                    list
                }
            },
            CloseButtonText = ResourceService.Get("DlgExplainClose")
        }.ShowAsync();
    }

    // --- Export / Import settings -----------------------------------------

    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop, SuggestedFileName = "settings" };
        picker.FileTypeChoices.Add("JSON", [".json"]);

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            AppSettings.ExportTo(file.Path);
            ViewModel.StatusText = ResourceService.Fmt("St_ExportSuccess", file.Name);
        }
        catch (Exception ex) { ViewModel.StatusText = ResourceService.Fmt("St_ExportFailed", ex.Message); }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add(".json");

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            AppSettings.ImportFrom(file.Path);
            ViewModel.Refresh();
            CookiesSection.ReloadSettings();
            ViewModel.StatusText = ResourceService.Fmt("St_ImportSuccess", file.Name);
        }
        catch (Exception ex) { ViewModel.StatusText = ResourceService.Fmt("St_ImportFailed", ex.Message); }
    }
}
