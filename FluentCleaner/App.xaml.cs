using FluentCleaner.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FluentCleaner;

public partial class App : Application
{
    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppSettings.Reload(); // read settings.json from %AppData$
        MainWindow = new MainWindow();
        SetupTitleBar();
        ResizeToDefault();
        ApplyTheme(AppSettings.Instance.Theme);
        MainWindow.Activate();
    }

    private void SetupTitleBar()
    {
        MainWindow!.ExtendsContentIntoTitleBar = true;
        MainWindow.SetTitleBar(MainWindow.TitleBarDragRegion);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = MainWindow.AppWindow.TitleBar;
            bar.ButtonBackgroundColor         = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonHoverBackgroundColor    = Colors.Transparent; // overridden by ApplyTheme
            bar.ButtonPressedBackgroundColor  = Colors.Transparent;
        }
    }

    private void ResizeToDefault()
    {
        MainWindow!.AppWindow.Resize(new SizeInt32(960, 620));

        if (DisplayArea.GetFromWindowId(MainWindow.AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            var area = display.WorkArea;
            MainWindow.AppWindow.Move(new PointInt32(
                area.X + (area.Width  - 960) / 2,
                area.Y + (area.Height - 620) / 2));
        }
    }

    public void ApplyTheme(string? theme)
    {
        var elementTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark"  => ElementTheme.Dark,
            _       => ElementTheme.Default
        };

        if (MainWindow?.Content is FrameworkElement root)
            root.RequestedTheme = elementTheme;

        if (MainWindow is not { } win) return;

        win.NavigationFrame.RequestedTheme = elementTheme;

        // Update titlebar button icon colors; Windows doesn't do this automatically
        // when RequestedTheme changes on the content, so we sync them manually.
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        // Foreground: white in dark, black in light
        Windows.UI.Color? fg = elementTheme switch
        {
            ElementTheme.Dark  => Colors.White,
            ElementTheme.Light => Colors.Black,
            _                  => null   // system default
        };

        // Hover/pressed background: subtle tint matching the theme
        Windows.UI.Color? hoverBg = elementTheme switch
        {
            ElementTheme.Dark  => Windows.UI.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), // ~12% white
            ElementTheme.Light => Windows.UI.Color.FromArgb(0x0F, 0x00, 0x00, 0x00), // ~6% black
            _                  => null
        };
        Windows.UI.Color? pressedBg = elementTheme switch
        {
            ElementTheme.Dark  => Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), // ~20% white
            ElementTheme.Light => Windows.UI.Color.FromArgb(0x18, 0x00, 0x00, 0x00), // ~10% black
            _                  => null
        };

        var bar = win.AppWindow.TitleBar;
        bar.ButtonForegroundColor         = fg;
        bar.ButtonHoverForegroundColor    = fg;
        bar.ButtonPressedForegroundColor  = fg;
        bar.ButtonInactiveForegroundColor = fg;
        bar.ButtonHoverBackgroundColor    = hoverBg;
        bar.ButtonPressedBackgroundColor  = pressedBg;
    }
}
