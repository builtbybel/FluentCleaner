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
        //Language must be applied BEFORE InitializeComponent so WinUI reads it once at startup
        try
        {
            var lang = AppSettings.Instance.Language;
            if (!string.IsNullOrWhiteSpace(lang))
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
        }
        catch { /* if resources broken, let app keep running in default language */ }

        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            e.Handled = true; //stop the app from dying silently(0xC000027B)
            LogCrash(e.Exception);
        };
    }

    //when the app blows up, dump it to crash.log so users can send it over.
    //lives next to auto.log: %AppData%\FluentCleaner\crash.log
    private static void LogCrash(Exception ex)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FluentCleaner", "crash.log");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n{new string('-', 60)}\n\n");
        }
        catch { /* logging must never be the thing that crashes us */ }
    }

    //Entry point;load settings, build the window, wire everything up.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {

        AppSettings.Reload();

        //force language for code-side strings (CLI, status messages, dialogs)
        // XAML x:Uid strings always follow the Windows display language
        ResourceService.SetLanguage(AppSettings.Instance.Language);

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

        // window first, then read Winapp2 in the background while the first tab gets built.
        // before Activate the window came up 66 ms later
        ViewModels.CleanerPageViewModel.StartPreload();

        //Remember size for next launch
        MainWindow.Closed += (_, _) =>
        {
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
        var w = AppSettings.Instance.WindowWidth;
        var h = AppSettings.Instance.WindowHeight;
        MainWindow!.AppWindow.Resize(new SizeInt32(w, h));

        if (DisplayArea.GetFromWindowId(MainWindow.AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            var area = display.WorkArea;
            MainWindow.AppWindow.Move(new PointInt32(
                area.X + (area.Width  - w) / 2,
                area.Y + (area.Height - h) / 2));
        }
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

    // Settings > Appearance > Backdrop. Mica is default, Mica Alt is more colorful
    public void ApplyBackdrop(string? backdrop)
    {
        if (MainWindow is null) return;

        MainWindow.SystemBackdrop = backdrop?.ToLowerInvariant() switch
        {
            "acrylic" => new DesktopAcrylicBackdrop(),
            "micaalt" => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            _         => new MicaBackdrop()
        };
    }
}
