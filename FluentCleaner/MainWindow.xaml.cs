using FluentCleaner.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner;

public sealed partial class MainWindow : Window
{
    // Both exposed so App.xaml.cs can handle window setup and theming centrally.
    public Frame     NavigationFrame   => ContentFrame;
    public UIElement TitleBarDragRegion => TitleBarDragArea;

    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(CleanerPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        Type? page = item.Tag?.ToString() switch
        {
            "Cleaner"  => typeof(CleanerPage),
            "Registry" => typeof(RegistryPage), //just decorotive for now, will be implemented in the future
            "Tools"    => typeof(ToolsPage),
            "Settings" => typeof(SettingsPage),
            _          => null
        };

        if (page is not null)
            ContentFrame.Navigate(page);
    }
}
