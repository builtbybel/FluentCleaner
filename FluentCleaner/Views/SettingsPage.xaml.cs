using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentCleaner.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; } = new();
    public string AppVersion => AppInfo.VersionString;

    public SettingsPage()
    {
        InitializeComponent();
        // Refresh stats whenever the user navigates back to this page
        Loaded += (_, _) => ViewModel.Refresh();
    }

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
