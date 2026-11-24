using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentCleaner.Plugins.Tweaks;

//shell stuff (broadcast, explorer restart), not registry
internal static class WindowsActions
{
    private const int HwndBroadcast = 0xffff;       // all top level windows
    private const uint WmSettingChange = 0x001A;    // setting changed, reread it
    private const uint SmtoAbortIfHung = 0x0002;    // skip hung apps

    //tells Explorer and open apps to reload theme/taskbar settings
    public static void NotifyShellSettingsChanged()
    {
        SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero,
            "ImmersiveColorSet", SmtoAbortIfHung, 100, out _);
        Services.ShellNotification.NotifyAssocChanged();
    }

    //kills explorer in this session and waits for it to come back;
    //some values are only read at startup. errors are ignored
    public static void RestartExplorer()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("explorer")
                         .Where(process => process.SessionId == session))
            {
                process.Kill();
                process.WaitForExit(3000);
                process.Dispose();
            }
            Thread.Sleep(400);
            var running = Process.GetProcessesByName("explorer");
            var shellReturned = running.Any(process => process.SessionId == session);
            foreach (var process in running) process.Dispose();
            //windows usually restarts it by itself, this is just the fallback
            if (!shellReturned)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
