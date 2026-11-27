using FluentCleaner.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Management.Deployment;

namespace FluentCleaner.Services;

/// <summary>
/// NativeAOT-safe, UI-thread-safe application & native Windows 11 system icon resolver.
/// Priority 1: REAL installed Windows application logos (.exe via App Paths / common install dirs /
///             Uninstall DisplayIcon / DetectFiles, or MSIX Store PackageManager Logo).
/// Priority 2: Authentic multi-color Windows 11 Fluent system icons extracted directly from
///             System32\imageres.dll and System32\shell32.dll for non-app categories
///             (Applications, Developer Tools, Games, Internet, Multimedia, Utilities, Windows, etc.).
/// Priority 3: Bundled high-DPI PNG fallback (only if native extraction finds nothing).
/// </summary>
public static class AppIconResolverService
{
    // Native Windows 11 multi-color Fluent icons inside System32\imageres.dll / shell32.dll / ddores.dll
    // Negative numbers are resource IDs in Windows DLLs (stable across Windows 10 & Windows 11).
    private static readonly Dictionary<string, (string Dll, int ResourceId)[]> NativeSystemIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows"]            = [("imageres.dll", -109), ("shell32.dll", -15)],   // Colorful Windows PC / Monitor
        ["Applications"]       = [("imageres.dll", -11),  ("shell32.dll", -2)],    // Colorful Application Window
        ["Developer Tools"]    = [("cmd.exe", 0),         ("imageres.dll", -5377)],// Native Terminal / Dev Tool
        ["Developer & AI"]     = [("cmd.exe", 0),         ("imageres.dll", -5377)],
        ["Command Palette"]    = [("cmd.exe", 0),         ("imageres.dll", -5377)],
        ["Games"]              = [("ddores.dll", -2029),  ("imageres.dll", -186)], // Colorful Gamepad Controller
        ["Internet"]           = [("shell32.dll", -14),   ("imageres.dll", -25)],  // Colorful Globe
        ["Multimedia"]         = [("imageres.dll", -108), ("imageres.dll", -18)],  // Colorful Media / Music-Video
        ["Utilities"]          = [("imageres.dll", -114), ("shell32.dll", -22)],   // Colorful Settings / Tools
        ["Language Files"]     = [("imageres.dll", -25),  ("shell32.dll", -21)],   // Colorful Globe / Language
        ["Long"]               = [("imageres.dll", -3),   ("shell32.dll", -4)],    // Native Windows 11 Yellow Folder
        ["Other Applications"] = [("imageres.dll", -11),  ("shell32.dll", -2)],
    };

    // Known app category -> candidate executable names AND common install paths
    private static readonly Dictionary<string, (string[] Exes, string[] FallbackPaths)> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Firefox"]                 = (["firefox.exe"], [@"%ProgramFiles%\Mozilla Firefox\firefox.exe", @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"]),
        ["Mozilla Firefox"]         = (["firefox.exe"], [@"%ProgramFiles%\Mozilla Firefox\firefox.exe", @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"]),
        ["Google Chrome"]           = (["chrome.exe"],  [@"%ProgramFiles%\Google\Chrome\Application\chrome.exe", @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe", @"%LocalAppData%\Google\Chrome\Application\chrome.exe"]),
        ["Chrome"]                  = (["chrome.exe"],  [@"%ProgramFiles%\Google\Chrome\Application\chrome.exe", @"%LocalAppData%\Google\Chrome\Application\chrome.exe"]),
        ["Microsoft Edge"]          = (["msedge.exe"],  [@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"]),
        ["Edge"]                    = (["msedge.exe"],  [@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"]),
        ["Edge Chromium"]           = (["msedge.exe"],  [@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"]),
        ["Brave"]                   = (["brave.exe"],   [@"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe", @"%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe"]),
        ["Opera"]                   = (["opera.exe"],   [@"%LocalAppData%\Programs\Opera\opera.exe", @"%ProgramFiles%\Opera\opera.exe"]),
        ["Opera GX"]                = (["opera.exe"],   [@"%LocalAppData%\Programs\Opera GX\opera.exe"]),
        ["Vivaldi"]                 = (["vivaldi.exe"], [@"%LocalAppData%\Vivaldi\Application\vivaldi.exe", @"%ProgramFiles%\Vivaldi\Application\vivaldi.exe"]),
        ["Microsoft PowerToys"]     = (["PowerToys.exe"], [@"%ProgramFiles%\PowerToys\PowerToys.exe", @"%LocalAppData%\PowerToys\PowerToys.exe"]),
        ["PowerToys"]               = (["PowerToys.exe"], [@"%ProgramFiles%\PowerToys\PowerToys.exe", @"%LocalAppData%\PowerToys\PowerToys.exe"]),
        ["Adobe"]                   = (["Acrobat.exe", "AcroRd32.exe"], [@"%ProgramFiles%\Adobe\Acrobat DC\Acrobat\Acrobat.exe", @"%ProgramFiles(x86)%\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe"]),
        ["Adobe Acrobat"]           = (["Acrobat.exe"], [@"%ProgramFiles%\Adobe\Acrobat DC\Acrobat\Acrobat.exe"]),
        ["Adobe Reader"]            = (["AcroRd32.exe"], [@"%ProgramFiles(x86)%\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe"]),
        ["Spotify"]                 = (["Spotify.exe"], [@"%AppData%\Spotify\Spotify.exe"]),
        ["Discord"]                 = (["Discord.exe"], [@"%LocalAppData%\Discord\Update.exe"]),
        ["WhatsApp"]                = (["WhatsApp.exe"], []),
        ["Telegram"]                = (["Telegram.exe"], [@"%AppData%\Telegram Desktop\Telegram.exe"]),
        ["Thunderbird"]             = (["thunderbird.exe"], [@"%ProgramFiles%\Mozilla Thunderbird\thunderbird.exe"]),
        ["iTunes"]                  = (["iTunes.exe"], [@"%ProgramFiles%\iTunes\iTunes.exe"]),
        ["VLC"]                     = (["vlc.exe"], [@"%ProgramFiles%\VideoLAN\VLC\vlc.exe"]),
        ["VLC Media Player"]        = (["vlc.exe"], [@"%ProgramFiles%\VideoLAN\VLC\vlc.exe"]),
        ["Steam"]                   = (["steam.exe"], [@"%ProgramFiles(x86)%\Steam\steam.exe"]),
        ["7-Zip"]                   = (["7zFM.exe"], [@"%ProgramFiles%\7-Zip\7zFM.exe"]),
        ["Notepad++"]               = (["notepad++.exe"], [@"%ProgramFiles%\Notepad++\notepad++.exe"]),
        ["VS Code"]                 = (["Code.exe"], [@"%LocalAppData%\Programs\Microsoft VS Code\Code.exe", @"%ProgramFiles%\Microsoft VS Code\Code.exe"]),
        ["Visual Studio Code"]      = (["Code.exe"], [@"%LocalAppData%\Programs\Microsoft VS Code\Code.exe"]),
        ["Cursor"]                  = (["Cursor.exe"], [@"%LocalAppData%\Programs\cursor\Cursor.exe"]),
        ["Docker"]                  = (["Docker Desktop.exe"], [@"%ProgramFiles%\Docker\Docker\Docker Desktop.exe"]),
        ["Docker Desktop"]          = (["Docker Desktop.exe"], [@"%ProgramFiles%\Docker\Docker\Docker Desktop.exe"]),
        ["Slack"]                   = (["slack.exe"], [@"%LocalAppData%\slack\slack.exe"]),
        ["Zoom"]                    = (["Zoom.exe"], [@"%AppData%\Zoom\bin\Zoom.exe"]),
        ["OBS Studio"]              = (["obs64.exe"], [@"%ProgramFiles%\obs-studio\bin\64bit\obs64.exe"]),
        ["Arc Browser"]             = (["Arc.exe"], []),
        ["Internet Explorer"]       = (["iexplore.exe"], [@"%ProgramFiles%\Internet Explorer\iexplore.exe"]),
        ["Microsoft Store"]         = ([], []),
        ["Windows Store"]           = ([], []),
    };

    // UI-thread cache: normalized category key -> ImageSource (preferring native extracted icons)
    private static readonly ConcurrentDictionary<string, ImageSource> _nativeIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> _bundledIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _missCache = new(StringComparer.OrdinalIgnoreCase);

    public static void InvalidateMissCache() => _missCache.Clear();

    /// <summary>Returns the best cached icon (native extracted icon preferred over bundled PNG).</summary>
    public static ImageSource? TryGetCached(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        var key = NormalizeCategoryKey(categoryName);
        if (_nativeIconCache.TryGetValue(key, out var nativeImg)) return nativeImg;
        if (_bundledIconCache.TryGetValue(key, out var bundledImg)) return bundledImg;
        return null;
    }

    /// <summary>
    /// Synchronously creates a BitmapImage for a bundled ms-appx:/// fallback URI (only used until
    /// or unless native Windows icon extraction succeeds). Must be called on the UI thread.
    /// </summary>
    public static ImageSource? TryGetBundledBitmap(string categoryName, string? msAppxUri)
    {
        if (string.IsNullOrWhiteSpace(msAppxUri)) return null;
        var key = NormalizeCategoryKey(categoryName);

        // If we already extracted the real native Windows icon, never downgrade it to a bundled PNG!
        if (_nativeIconCache.TryGetValue(key, out var nativeImg))
            return nativeImg;

        if (_bundledIconCache.TryGetValue(key, out var existing))
            return existing;

        try
        {
            var bmp = new BitmapImage(new Uri(msAppxUri))
            {
                DecodePixelWidth  = 48,
                DecodePixelHeight = 48
            };
            _bundledIconCache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Asynchronously extracts either:
    /// 1. The REAL installed application icon (.exe / MSIX) for app categories (Firefox, Chrome, Edge, PowerToys, etc.), OR
    /// 2. The REAL native multi-color Windows 11 Fluent system icon from imageres.dll / shell32.dll for non-app categories.
    /// Always runs extraction off the UI thread and marshals WriteableBitmap / BitmapImage creation to DispatcherQueue.
    /// </summary>
    public static async Task TryResolveNativeIconAsync(
        string categoryName,
        IReadOnlyList<CleanerEntry> entries,
        DispatcherQueue dispatcher,
        Action<ImageSource> onResolved)
    {
        var key = NormalizeCategoryKey(categoryName);
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (_nativeIconCache.TryGetValue(key, out var cached))
        {
            dispatcher.TryEnqueue(() => onResolved(cached));
            return;
        }

        if (_missCache.ContainsKey(key))
            return;

        var extracted = await Task.Run(() => ExtractIconDataBackground(key, entries));
        if (extracted is null)
        {
            _missCache[key] = 1;
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                ImageSource? source = null;
                if (extracted.LogoUri is not null)
                {
                    source = new BitmapImage(extracted.LogoUri)
                    {
                        DecodePixelWidth  = 48,
                        DecodePixelHeight = 48
                    };
                }
                else if (extracted.BgraPixels is not null && extracted.Width > 0 && extracted.Height > 0)
                {
                    var wb = new WriteableBitmap(extracted.Width, extracted.Height);
                    using (var stream = wb.PixelBuffer.AsStream())
                    {
                        stream.Write(extracted.BgraPixels, 0, extracted.BgraPixels.Length);
                    }
                    wb.Invalidate();
                    source = wb;
                }

                if (source is not null)
                {
                    _nativeIconCache[key] = source;
                    onResolved(source);
                }
            }
            catch { }
        });
    }

    private static string NormalizeCategoryKey(string categoryName)
    {
        var trimmed = categoryName.Trim();
        if (trimmed.StartsWith("Dangerous ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["Dangerous ".Length..].Trim();
        return trimmed;
    }

    private sealed record ExtractedIconData(Uri? LogoUri, int Width, int Height, byte[]? BgraPixels);

    private static ExtractedIconData? ExtractIconDataBackground(string key, IReadOnlyList<CleanerEntry> entries)
    {
        // 1. Non-app categories -> extract genuine native multi-color Windows 11 system icon from System32 DLLs
        if (NativeSystemIcons.TryGetValue(key, out var sysCandidates))
        {
            var sysDir = Environment.SystemDirectory;
            foreach (var (dll, resId) in sysCandidates)
            {
                var dllPath = Path.Combine(sysDir, dll);
                if (!File.Exists(dllPath)) continue;
                var bmp = ExtractDllResourceIconBgra(dllPath, resId);
                if (bmp is not null) return bmp;
            }
            return null;
        }

        // 2. Known apps -> Registry App Paths + common install directories
        if (KnownApps.TryGetValue(key, out var appInfo))
        {
            foreach (var exe in appInfo.Exes)
            {
                var regPath = FindAppPathInRegistry(exe);
                if (!string.IsNullOrWhiteSpace(regPath) && File.Exists(regPath))
                {
                    var bmp = ExtractWin32IconBgra(regPath);
                    if (bmp is not null) return bmp;
                }
            }

            foreach (var rawFallback in appInfo.FallbackPaths)
            {
                var expanded = Environment.ExpandEnvironmentVariables(rawFallback);
                if (File.Exists(expanded))
                {
                    var bmp = ExtractWin32IconBgra(expanded);
                    if (bmp is not null) return bmp;
                }
            }
        }

        // 3. Uninstall DisplayIcon matching category/app name
        var uninstallIcon = FindDisplayIconInUninstallRegistry(key);
        if (!string.IsNullOrWhiteSpace(uninstallIcon) && File.Exists(uninstallIcon))
        {
            var bmp = ExtractWin32IconBgra(uninstallIcon);
            if (bmp is not null) return bmp;
        }

        // 4. Any .exe referenced in DetectFiles of the category's entries
        foreach (var entry in entries)
        {
            foreach (var raw in entry.DetectFiles)
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw).Trim().Trim('"');
                if (expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(expanded))
                {
                    var bmp = ExtractWin32IconBgra(expanded);
                    if (bmp is not null) return bmp;
                }
            }
        }

        // 5. MSIX / Store PackageManager Logo URI (WhatsApp, Microsoft Store, Store Firefox, PowerToys, etc.)
        var pkgLogo = TryFindPackageLogoUri(key);
        if (pkgLogo is not null)
            return new ExtractedIconData(pkgLogo, 0, 0, null);

        return null;
    }

    private static string? FindAppPathInRegistry(string exeName)
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                var val = key?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    var clean = Environment.ExpandEnvironmentVariables(val.Trim().Trim('"'));
                    if (File.Exists(clean)) return clean;
                }
            }
            catch { }
        }
        return null;
    }

    private static string? FindDisplayIconInUninstallRegistry(string appName)
    {
        string[] paths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var basePath in paths)
            {
                try
                {
                    using var parent = root.OpenSubKey(basePath);
                    if (parent is null) continue;
                    foreach (var sub in parent.GetSubKeyNames())
                    {
                        using var key = parent.OpenSubKey(sub);
                        var displayName = key?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        if (!displayName.Equals(appName, StringComparison.OrdinalIgnoreCase) &&
                            !displayName.StartsWith(appName + " ", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var iconRaw = key?.GetValue("DisplayIcon") as string;
                        if (string.IsNullOrWhiteSpace(iconRaw)) continue;

                        var commaIdx = iconRaw.IndexOf(',');
                        var cleanPath = (commaIdx >= 0 ? iconRaw[..commaIdx] : iconRaw).Trim().Trim('"');
                        cleanPath = Environment.ExpandEnvironmentVariables(cleanPath);
                        if (File.Exists(cleanPath)) return cleanPath;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    private static Uri? TryFindPackageLogoUri(string appName)
    {
        try
        {
            var pm = new PackageManager();
            var needle = appName.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
            if (needle.Equals("WindowsStore", StringComparison.OrdinalIgnoreCase))
                needle = "WindowsStore";

            foreach (var pkg in pm.FindPackagesForUser(""))
            {
                var name = pkg.DisplayName ?? pkg.Id.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (name.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                    pkg.Id.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    name.Replace(" ", "").Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    if (pkg.Logo is { } logo && File.Exists(logo.LocalPath))
                        return logo;
                }
            }
        }
        catch { }
        return null;
    }

    // --- NativeAOT-Safe Win32 Icon Extraction (Zero System.Drawing) -----------

    private const uint SHGFI_ICON      = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint DIB_RGB_COLORS  = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int    fIcon;
        public int    xHotspot;
        public int    yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int    bmType;
        public int    bmWidth;
        public int    bmHeight;
        public int    bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(IntPtr hObject, int nCount, out BITMAP lpObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[]? lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    private static ExtractedIconData? ExtractDllResourceIconBgra(string dllPath, int iconIndexOrResId)
    {
        var large = new IntPtr[1];
        uint extracted = ExtractIconExW(dllPath, iconIndexOrResId, large, null, 1);
        if (extracted == 0 || large[0] == IntPtr.Zero)
            return null;

        try
        {
            return HIconToBgra(large[0]);
        }
        finally
        {
            DestroyIcon(large[0]);
        }
    }

    private static ExtractedIconData? ExtractWin32IconBgra(string filePath)
    {
        var shfi = new SHFILEINFO();
        var res = SHGetFileInfoW(filePath, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return HIconToBgra(shfi.hIcon);
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    private static ExtractedIconData? HIconToBgra(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
            return null;

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
                return null;

            if (GetObjectW(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), out var bm) == 0)
                return null;

            int w = bm.bmWidth;
            int h = bm.bmHeight;
            if (w <= 0 || h <= 0 || w > 256 || h > 256)
                return null;

            var pixels = new byte[w * h * 4];
            var bmi = new BITMAPINFOHEADER
            {
                biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth       = w,
                biHeight      = -h, // top-down DIB
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = 0   // BI_RGB
            };

            var hdc = GetDC(IntPtr.Zero);
            try
            {
                int lines = GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)h, pixels, ref bmi, DIB_RGB_COLORS);
                if (lines == 0) return null;

                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { hasAlpha = true; break; }
                }

                if (!hasAlpha && iconInfo.hbmMask != IntPtr.Zero)
                {
                    var mask = new byte[w * h * 4];
                    var maskBmi = bmi;
                    if (GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)h, mask, ref maskBmi, DIB_RGB_COLORS) > 0)
                    {
                        for (int i = 0; i < pixels.Length; i += 4)
                            pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
                    }
                }

                // Premultiply alpha for WinUI 3 WriteableBitmap (BGRA8 premultiplied)
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte a = pixels[i + 3];
                    if (a < 255)
                    {
                        pixels[i]     = (byte)(pixels[i]     * a / 255);
                        pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                        pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                    }
                }

                return new ExtractedIconData(null, w, h, pixels);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask  != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }
}
