using FluentCleaner.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.SetObserved();
        };

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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
            sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
            sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine($"--- InnerException ({inner.GetType().FullName}): {inner.Message}");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString());
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

        try
        {
            MainWindow = new MainWindow();
            SetupTitleBar();
            RestoreWindowSize();
            ApplyBackdrop(AppSettings.Instance.Backdrop);
            ApplyTheme(AppSettings.Instance.Theme);
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }

        //Remember size for next launch and guarantee complete process termination on close
        MainWindow.Closed += (_, _) =>
        {
            try
            {
                var size = MainWindow.AppWindow.Size;
                AppSettings.Instance.WindowWidth  = size.Width;
                AppSettings.Instance.WindowHeight = size.Height;
                AppSettings.Instance.Save();
            }
            catch { }
            finally
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
                Environment.Exit(0);
            }
        };

        MainWindow.AppWindow.Closing += (_, _) =>
        {
            try
            {
                var size = MainWindow.AppWindow.Size;
                AppSettings.Instance.WindowWidth  = size.Width;
                AppSettings.Instance.WindowHeight = size.Height;
                AppSettings.Instance.Save();
            }
            catch { }
            finally
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
                Environment.Exit(0);
            }
        };
    }

    private void SetupTitleBar()
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var bar = MainWindow!.AppWindow.TitleBar;
            bar.ButtonBackgroundColor         = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    //Picks up the saved size from settings, falls back to 1160x740 on first run
    private void RestoreWindowSize()
    {
        var w = Math.Max(720, AppSettings.Instance.WindowWidth);
        var h = Math.Max(540, AppSettings.Instance.WindowHeight);
        MainWindow!.AppWindow.Resize(new SizeInt32(w, h));

        if (DisplayArea.GetFromWindowId(MainWindow.AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            var area = display.WorkArea;
            MainWindow.AppWindow.Move(new PointInt32(
                area.X + Math.Max(0, (area.Width  - w) / 2),
                area.Y + Math.Max(0, (area.Height - h) / 2)));
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

    // MicaAlt by default for authentic Windows 11 tabbed/nav depth; Mica or Acrylic supported
    public void ApplyBackdrop(string? backdrop)
    {
        if (MainWindow is null) return;

        MainWindow.SystemBackdrop = backdrop?.ToLowerInvariant() switch
        {
            "acrylic" => new DesktopAcrylicBackdrop(),
            "mica"    => new MicaBackdrop { Kind = MicaKind.Base },
            _         => new MicaBackdrop { Kind = MicaKind.BaseAlt }
        };
    }
}
