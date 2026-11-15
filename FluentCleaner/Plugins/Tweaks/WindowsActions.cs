using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentCleaner.Plugins.Tweaks;

//the small jobs that belong to the shell rather than to the registry, kept out of the applier
internal static class WindowsActions
{
    private const int HwndBroadcast = 0xffff;       // every top level window
    private const uint WmSettingChange = 0x001A;    // a setting changed, go and read it again
    private const uint SmtoAbortIfHung = 0x0002;    // one frozen app must not hold up the rest

    //asks Explorer and open apps to pick up theme and taskbar registry changes
    public static void NotifyShellSettingsChanged() =>
        SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero,
            "ImmersiveColorSet", SmtoAbortIfHung, 100, out _);

    //ends explorer in this session and waits for it to come back, since some values are read
    //only once at startup; failures are silent, a shell that stays away is not ours to fix
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
            //windows restarts the shell by itself as a rule, this is only for when it did not
            if (!shellReturned)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
