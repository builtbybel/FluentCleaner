using FluentCleaner.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FluentCleaner;

public partial class App : Application
{
    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    //Entry point;load settings, build the window, wire everything up.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {

        AppSettings.Reload();

        // SilentRunner headless clean, no window; /SHUTDOWN shuts down after
        var cmdArgs = Environment.GetCommandLineArgs();
        bool isAuto     = cmdArgs.Any(a => a.Equals("/AUTO",     StringComparison.OrdinalIgnoreCase));
        bool isShutdown = cmdArgs.Any(a => a.Equals("/SHUTDOWN", StringComparison.OrdinalIgnoreCase));

        if (isAuto)
        {
            _ = SilentRunner.RunAsync(isShutdown);
            return;
        }

        MainWindow = new MainWindow();
        SetupTitleBar();
        RestoreWindowSize();
        ApplyBackdrop(AppSettings.Instance.Backdrop);
        ApplyTheme(AppSettings.Instance.Theme);
        MainWindow.Activate();

        //Remember size for next launch
        MainWindow.Closed += (_, _) =>
        {
            // Do not persist the maximized work-area size as a normal window
            // size; restoring it on a different DPI/display can crop content.
            if (MainWindow.AppWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Maximized)
                return;

            var size = MainWindow.AppWindow.Size;
            AppSettings.Instance.WindowWidth  = size.Width;
            AppSettings.Instance.WindowHeight = size.Height;
            AppSettings.Instance.Save();
        };
    }
    // idea from John Gage Faulkner's WinUI3SampleStarterApp
    // https://github.com/johngagefaulkner/WinUI3SampleStarterApp
    // remove idle background only;so it lets TitleBar blend with Mica
    // don't touch hover/pressed, PreferredTheme handles it
    // overriding all states breaks hover (learned that the hard way)
    private void SetupTitleBar()
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = MainWindow!.AppWindow.TitleBar;
            bar.ButtonBackgroundColor         = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    //Picks up the saved size from settings, falls back to 960x620 on first run
    private void RestoreWindowSize()
    {
        if (DisplayArea.GetFromWindowId(MainWindow!.AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            var area = display.WorkArea;

            // AppWindow sizes are restored from the previous session, but the
            // saved size may belong to a different monitor or DPI scale. Clamp
            // it to the current work area so the bottom/right edge never lands
            // outside the visible screen.
            const int minWidth = 760;
            const int minHeight = 520;
            var maxWidth = Math.Max(minWidth, area.Width - 32);
            var maxHeight = Math.Max(minHeight, area.Height - 48);
            var savedWidth = AppSettings.Instance.WindowWidth;
            var savedHeight = AppSettings.Instance.WindowHeight;

            // A previous build could persist a maximized/near-maximized size.
            // Treat that as stale and start with the intended compact default.
            if (savedWidth >= area.Width - 64 && savedHeight >= area.Height - 80)
            {
                savedWidth = 960;
                savedHeight = 620;
            }

            var w = Math.Clamp(savedWidth, minWidth, maxWidth);
            var h = Math.Clamp(savedHeight, minHeight, maxHeight);

            MainWindow.AppWindow.Resize(new SizeInt32(w, h));
            MainWindow.AppWindow.Move(new PointInt32(
                area.X + (area.Width  - w) / 2,
                area.Y + (area.Height - h) / 2));

            return;
        }

        MainWindow.AppWindow.Resize(new SizeInt32(
            Math.Max(760, AppSettings.Instance.WindowWidth),
            Math.Max(520, AppSettings.Instance.WindowHeight)));
    }

    // Switches light/dark/system 
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

        win.AppWindow.TitleBar.PreferredTheme = elementTheme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark  => TitleBarTheme.Dark,
            _                  => TitleBarTheme.UseDefaultAppMode
        };
    }

    // Mica by default, acrylic if the user set it via terminal. No Settings UI for this on purpose
    public void ApplyBackdrop(string? backdrop)
    {
        if (MainWindow is null) return;

        MainWindow.SystemBackdrop = backdrop?.ToLowerInvariant() switch
        {
            "acrylic" => new DesktopAcrylicBackdrop(),
            _         => new MicaBackdrop()
        };
    }
}
