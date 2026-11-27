using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

/// <summary>
/// Provides 64-bit safe Windows Shell change notifications via shell32.dll SHChangeNotify.
/// Refreshes the Windows Explorer icon cache, thumbnail caches, and desktop associations
/// dynamically without requiring explorer.exe to be killed or restarted.
/// </summary>
public static class ShellNotification
{
    // Shell change notification event identifiers (from ShlObj.h)
    public const int SHCNE_RENAMEITEM = 0x00000001;
    public const int SHCNE_CREATE = 0x00000002;
    public const int SHCNE_DELETE = 0x00000004;
    public const int SHCNE_MKDIR = 0x00000008;
    public const int SHCNE_RMDIR = 0x00000010;
    public const int SHCNE_MEDIAINSERTED = 0x00000020;
    public const int SHCNE_MEDIAREMOVED = 0x00000040;
    public const int SHCNE_DRIVEREMOVED = 0x00000080;
    public const int SHCNE_DRIVEADD = 0x00000100;
    public const int SHCNE_NETSHARE = 0x00000200;
    public const int SHCNE_NETUNSHARE = 0x00000400;
    public const int SHCNE_ATTRIBUTES = 0x00000800;
    public const int SHCNE_UPDATEDIR = 0x00001000;
    public const int SHCNE_UPDATEITEM = 0x00002000;
    public const int SHCNE_SERVERDISCONNECT = 0x00004000;
    public const int SHCNE_UPDATEIMAGE = 0x00008000;
    public const int SHCNE_DRIVEADDGUI = 0x00010000;
    public const int SHCNE_RENAMEFOLDER = 0x00020000;
    public const int SHCNE_FREESPACE = 0x00040000;
    public const int SHCNE_EXTENDED_EVENT = 0x04000000;
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNE_DISKEVENTS = 0x0002381F;
    public const int SHCNE_GLOBALEVENTS = 0x0C0581E0;
    public const int SHCNE_ALLEVENTS = 0x7FFFFFFF;
    public const int SHCNE_INTERRUPT = unchecked((int)0x80000000);

    // Shell notification flags
    public const uint SHCNF_IDLIST = 0x0000;
    public const uint SHCNF_PATHA = 0x0001;
    public const uint SHCNF_PRINTERA = 0x0002;
    public const uint SHCNF_DWORD = 0x0003;
    public const uint SHCNF_PATHW = 0x0005;
    public const uint SHCNF_PRINTERW = 0x0006;
    public const uint SHCNF_TYPE = 0x00FF;
    public const uint SHCNF_FLUSH = 0x1000;
    public const uint SHCNF_FLUSHNOWAIT = 0x2000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern void SHChangeNotify(
        int wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);

    /// <summary>
    /// Notifies the Windows Shell that file associations or icon caches have changed.
    /// This forces Explorer to reload icon caches and rebind file associations without killing explorer.exe.
    /// </summary>
    public static void NotifyAssocChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            Debug.WriteLine("[ShellNotification] Dispatched SHCNE_ASSOCCHANGED to Windows Shell.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellNotification] Warning: Failed to dispatch shell notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Explicit alias for NotifyAssocChanged to refresh Windows icon cache dynamically.
    /// </summary>
    public static void RefreshIconCache() => NotifyAssocChanged();

    /// <summary>
    /// Asynchronously notifies the Windows Shell on a background thread.
    /// </summary>
    public static Task RefreshIconCacheAsync() => Task.Run(RefreshIconCache);

    /// <summary>
    /// Determines whether the specified file path is a Windows Shell icon cache or thumbnail cache database.
    /// </summary>
    public static bool IsShellCacheFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
            return false;

        // IconCache.db or backup
        if (fileName.Equals("IconCache.db", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("IconCache.db.backup", StringComparison.OrdinalIgnoreCase))
            return true;

        // iconcache_*.db or thumbcache_*.db
        if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            if (fileName.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("thumbcache", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Files located inside the Windows Explorer thumbnail / icon cache directory
        if (filePath.IndexOf(@"\Microsoft\Windows\Explorer\", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.IndexOf("Cache", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // Search AppIconCache
        if (filePath.IndexOf(@"\LocalState\AppIconCache", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether the specified registry path belongs to Windows Shell caches or MRU structures.
    /// </summary>
    public static bool IsShellRegistryKey(string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            return false;

        return keyPath.IndexOf(@"\Explorer\UserAssist", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Explorer\FileExts", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Explorer\RecentDocs", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Windows\Shell\Bags", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Windows\Shell\BagMRU", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Explorer\WordWheelQuery", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Explorer\RunMRU", StringComparison.OrdinalIgnoreCase) >= 0 ||
               keyPath.IndexOf(@"\Explorer\ComDlg32\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Analyzes a ScanResult to determine if it affects Windows Shell or Explorer caches.
    /// </summary>
    public static bool ShouldNotify(ScanResult? result)
    {
        if (result is null)
            return false;

        var entryName = result.Entry?.Name ?? "";
        if (entryName.IndexOf("Windows Shell", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entryName.IndexOf("Windows Explorer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entryName.IndexOf("Icon Cache", StringComparison.OrdinalIgnoreCase) >= 0 ||
            entryName.IndexOf("Thumbnails", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (result.FilesToDelete.Any(IsShellCacheFile))
            return true;

        if (result.RegistryToDelete.Any(r => IsShellRegistryKey(r.KeyPath)))
            return true;

        return false;
    }
}
