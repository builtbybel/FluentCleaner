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
/// NativeAOT-safe, UI-thread-safe application icon resolver.
/// Tier 1: Bundled high-DPI PNG assets (ms-appx:///Assets/Icons/*.png) verified on disk.
/// Tier 2: Dynamic Win32 PE/ICO extraction (App Paths / indexed Uninstall DisplayIcon / PathExpander DetectFiles)
///         via ExtractIconExW + SHGetFileInfoW + GetDIBits -> WriteableBitmap on DispatcherQueue.
/// Tier 3: Indexed Windows Store / MSIX PackageManager Logo URI lookup with MRT scale & ACL validation.
/// Non-app categories (Utilities, Developer Tools, Multimedia, Games, etc.) return null
/// so the UI renders their vibrant colorful Fluent icon badge with zero binding errors.
/// </summary>
public static class AppIconResolverService
{
    private static readonly HashSet<string> GenericCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Applications", "Developer Tools", "Developer & AI", "AI", "Command Palette",
        "Utilities", "Games", "Multimedia", "Language Files", "Internet", "Long",
        "Other Applications", "System", "Logs", "Registry"
    };

    private static readonly HashSet<string> GenericExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall.exe", "unins000.exe", "uninst.exe", "update.exe", "updater.exe", "setup.exe"
    };

    private static readonly Dictionary<string, string[]> KnownAppExes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spotify"]                 = ["Spotify.exe"],
        ["Discord"]                 = ["Discord.exe"],
        ["WhatsApp"]                = ["WhatsApp.exe"],
        ["Telegram"]                = ["Telegram.exe"],
        ["Thunderbird"]             = ["thunderbird.exe"],
        ["iTunes"]                  = ["iTunes.exe"],
        ["VLC"]                     = ["vlc.exe"],
        ["VLC Media Player"]        = ["vlc.exe"],
        ["Steam"]                   = ["steam.exe"],
        ["7-Zip"]                   = ["7zFM.exe"],
        ["Notepad++"]               = ["notepad++.exe"],
        ["VS Code"]                 = ["Code.exe"],
        ["Visual Studio Code"]      = ["Code.exe"],
        ["Cursor"]                  = ["Cursor.exe"],
        ["Windsurf"]                = ["Windsurf.exe"],
        ["Docker"]                  = ["Docker Desktop.exe"],
        ["Docker Desktop"]          = ["Docker Desktop.exe"],
        ["Slack"]                   = ["slack.exe"],
        ["Zoom"]                    = ["Zoom.exe"],
        ["OBS Studio"]              = ["obs64.exe"],
        ["Blender"]                 = ["blender.exe"],
        ["GIMP"]                    = ["gimp-2.10.exe", "gimp-3.0.exe"],
        ["WinRAR"]                  = ["WinRAR.exe"],
        ["qBittorrent"]             = ["qbittorrent.exe"],
        ["FileZilla"]               = ["filezilla.exe"],
        ["Audacity"]                = ["audacity.exe"],
        ["HandBrake"]               = ["HandBrake.exe"],
        ["Arc Browser"]             = ["Arc.exe"],
        ["Opera GX"]                = ["opera.exe"],
        ["Avast Secure Browser"]    = ["AvastBrowser.exe"],
        ["AVG Secure Browser"]      = ["AVGBrowser.exe"],
        ["Norton Private Browser"]  = ["NortonBrowser.exe"],
        ["Avira Secure Browser"]    = ["AviraBrowser.exe"],
        ["Safari"]                  = ["Safari.exe"],
        ["Internet Explorer"]       = ["iexplore.exe"],
    };

    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _missCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<ExtractedIconData?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<List<(string DisplayName, string IconPath, int IconIndex)>> _uninstallIndex =
        new(BuildUninstallIconIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<List<(string DisplayName, string IdName, Uri? Logo)>> _packageIndex =
        new(BuildPackageIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<PathExpander> _pathExpander =
        new(() => new PathExpander(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Clears cached misses (e.g., when custom rules are reloaded with new paths).</summary>
    public static void InvalidateMissCache() => _missCache.Clear();

    /// <summary>Returns a cached ImageSource if already resolved for this category.</summary>
    public static ImageSource? TryGetCached(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        return _iconCache.TryGetValue(NormalizeCategoryKey(categoryName), out var img) ? img : null;
    }

    /// <summary>
    /// Synchronously creates and caches a BitmapImage for a bundled ms-appx:/// asset URI.
    /// Must be called on the UI thread; returns null cleanly if the asset is missing on disk.
    /// </summary>
    public static ImageSource? TryGetBundledBitmap(string? categoryName, string? msAppxUri)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        if (string.IsNullOrWhiteSpace(msAppxUri)) return null;

        var key = NormalizeCategoryKey(categoryName);
        if (_iconCache.TryGetValue(key, out var existing))
            return existing;

        const string prefix = "ms-appx:///";
        if (msAppxUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rel = msAppxUri[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var diskPath = Path.Combine(AppContext.BaseDirectory, rel);
            if (!IsReadableFile(diskPath))
                return null;
        }

        try
        {
            var bmp = new BitmapImage(new Uri(msAppxUri))
            {
                DecodePixelWidth  = 48,
                DecodePixelHeight = 48
            };
            _iconCache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Asynchronously extracts a real installed application logo (Win32 .exe or MSIX package)
    /// off the UI thread with in-flight deduplication and invokes onResolved on the UI DispatcherQueue.
    /// </summary>
    public static async Task TryResolveDynamicAppIconAsync(
        string? categoryName,
        IReadOnlyList<CleanerEntry>? entries,
        DispatcherQueue? dispatcher,
        Action<ImageSource>? onResolved)
    {
        if (string.IsNullOrWhiteSpace(categoryName) || dispatcher is null || onResolved is null)
            return;

        var key = NormalizeCategoryKey(categoryName);
        if (string.IsNullOrWhiteSpace(key) || GenericCategories.Contains(key))
            return;

        if (_iconCache.TryGetValue(key, out var cached))
        {
            dispatcher.TryEnqueue(() => onResolved(cached));
            return;
        }

        if (_missCache.ContainsKey(key))
            return;

        var safeEntries = entries is not null ? entries.ToList() : [];
        var task = _inFlight.GetOrAdd(key, k => Task.Run(() => ExtractIconDataBackground(k, safeEntries)));

        ExtractedIconData? extracted;
        try
        {
            extracted = await task;
        }
        finally
        {
            _inFlight.TryRemove(KeyValuePair.Create(key, task));
        }

        if (extracted is null)
        {
            if (safeEntries.Count > 0)
                _missCache[key] = 1;
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (_iconCache.TryGetValue(key, out var already))
                {
                    onResolved(already);
                    return;
                }

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
                    _iconCache[key] = source;
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

    private static ExtractedIconData? ExtractIconDataBackground(string appName, IReadOnlyList<CleanerEntry> entries)
    {
        // 1. Known executable names via Registry App Paths
        if (KnownAppExes.TryGetValue(appName, out var exeNames))
        {
            foreach (var exe in exeNames)
            {
                var path = FindAppPathInRegistry(exe);
                if (!string.IsNullOrWhiteSpace(path) && IsReadableFile(path))
                {
                    var bmp = ExtractFromIconSource(path, 0);
                    if (bmp is not null) return bmp;
                }
            }
        }

        // 2. Indexed Uninstall DisplayIcon matching category/app name (with trailing numeric index support)
        var uninstallIcon = FindDisplayIconInUninstallIndex(appName);
        if (uninstallIcon is not null)
        {
            var bmp = ExtractFromIconSource(uninstallIcon.Value.Path, uninstallIcon.Value.Index);
            if (bmp is not null) return bmp;
        }

        // 3. DetectFiles using Winapp2 PathExpander (handles %LocalLowAppData%, %ProgramFilesX86%, wildcards)
        foreach (var entry in entries)
        {
            if (entry?.DetectFiles is null) continue;
            foreach (var raw in entry.DetectFiles)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    foreach (var resolved in _pathExpander.Value.ResolvePaths(raw))
                    {
                        var clean = resolved.Trim().Trim('"');
                        var fileName = Path.GetFileName(clean);
                        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            !GenericExeNames.Contains(fileName) &&
                            IsReadableFile(clean))
                        {
                            var bmp = ExtractFromIconSource(clean, 0);
                            if (bmp is not null) return bmp;
                        }
                    }
                }
                catch { }
            }
        }

        // 4. Cached MSIX / Store PackageManager Logo URI (with MRT fallback & ACL check)
        var pkgLogo = TryFindPackageLogoUri(appName);
        if (pkgLogo is not null)
            return new ExtractedIconData(pkgLogo, 0, 0, null);

        return null;
    }

    private static ExtractedIconData? ExtractFromIconSource(string path, int iconIndex)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsReadableFile(path))
                return null;
            return Uri.TryCreate(Path.GetFullPath(path), UriKind.Absolute, out var fileUri)
                ? new ExtractedIconData(fileUri, 0, 0, null)
                : null;
        }

        return ExtractWin32IconBgra(path, iconIndex);
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
                    if (IsReadableFile(clean)) return clean;
                }
            }
            catch { }
        }
        return null;
    }

    private static List<(string DisplayName, string IconPath, int IconIndex)> BuildUninstallIconIndex()
    {
        var results = new List<(string DisplayName, string IconPath, int IconIndex)>();
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
                        try
                        {
                            using var key = parent.OpenSubKey(sub);
                            var displayName = key?.GetValue("DisplayName") as string;
                            var iconRaw = key?.GetValue("DisplayIcon") as string;
                            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(iconRaw)) continue;

                            var (cleanPath, index) = ParseIconLocation(iconRaw);
                            if (!string.IsNullOrWhiteSpace(cleanPath))
                                results.Add((displayName, cleanPath, index));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        return results;
    }

    private static (string Path, int Index) ParseIconLocation(string raw)
    {
        var trimmed = raw.Trim();
        int index = 0;

        // Only treat trailing ",<int>" as icon index so directories containing commas stay intact
        int lastComma = trimmed.LastIndexOf(',');
        if (lastComma > 0)
        {
            var suffix = trimmed[(lastComma + 1)..].Trim().Trim('"');
            if (int.TryParse(suffix, out int parsedIdx))
            {
                index = parsedIdx;
                trimmed = trimmed[..lastComma].Trim();
            }
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed.Trim('"'));
        return (expanded, index);
    }

    private static (string Path, int Index)? FindDisplayIconInUninstallIndex(string appName)
    {
        foreach (var (displayName, iconPath, iconIndex) in _uninstallIndex.Value)
        {
            if (displayName.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith(appName + " ", StringComparison.OrdinalIgnoreCase))
            {
                if (IsReadableFile(iconPath))
                    return (iconPath, iconIndex);
            }
        }
        return null;
    }

    private static List<(string DisplayName, string IdName, Uri? Logo)> BuildPackageIndex()
    {
        var list = new List<(string DisplayName, string IdName, Uri? Logo)>();
        try
        {
            var pm = new PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(""))
            {
                try
                {
                    var dName = pkg.DisplayName ?? "";
                    var idName = pkg.Id?.Name ?? "";
                    var logo = ResolveMrtLogoUri(pkg.Logo);
                    if (logo is not null && (!string.IsNullOrWhiteSpace(dName) || !string.IsNullOrWhiteSpace(idName)))
                        list.Add((dName, idName, logo));
                }
                catch { } // Ignore single unreadable/staged MSIX package without aborting loop
            }
        }
        catch { }
        return list;
    }

    private static Uri? TryFindPackageLogoUri(string appName)
    {
        var needle = appName.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        foreach (var (displayName, idName, logo) in _packageIndex.Value)
        {
            if (logo is null) continue;
            if (displayName.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith(appName + " ", StringComparison.OrdinalIgnoreCase) ||
                idName.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                (needle.Length >= 5 && idName.EndsWith("." + needle, StringComparison.OrdinalIgnoreCase)))
            {
                return logo;
            }
        }
        return null;
    }

    private static Uri? ResolveMrtLogoUri(Uri? logo)
    {
        if (logo is null || !logo.IsFile) return null;
        var local = logo.LocalPath;
        if (IsReadableFile(local)) return logo;

        var dir = Path.GetDirectoryName(local);
        var stem = Path.GetFileNameWithoutExtension(local);
        var ext = Path.GetExtension(local);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(ext))
            return null;

        string[] qualifiers =
        [
            ".scale-200", ".scale-150", ".scale-125", ".scale-100", ".scale-400",
            "_altform-unplated.targetsize-48", ".targetsize-48",
            "_altform-lightunplated.targetsize-48", ".targetsize-32",
            "_altform-unplated", "_altform-lightunplated"
        ];
        foreach (var q in qualifiers)
        {
            var candidate = Path.Combine(dir, $"{stem}{q}{ext}");
            if (IsReadableFile(candidate))
                return new Uri(candidate);
        }
        return null;
    }

    private static bool IsReadableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // --- NativeAOT-Safe Win32 Icon Extraction (Zero System.Drawing, zero Shell COM dependency) ---

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
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, IntPtr phiconSmall, uint nIcons);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

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

    private static ExtractedIconData? ExtractWin32IconBgra(string filePath, int iconIndex)
    {
        IntPtr hIcon = IntPtr.Zero;
        if (iconIndex != -1 && ExtractIconExW(filePath, iconIndex, out var extracted, IntPtr.Zero, 1) > 0 && extracted != IntPtr.Zero)
        {
            hIcon = extracted;
        }
        else if (iconIndex <= 0)
        {
            var shfi = new SHFILEINFO();
            var res = SHGetFileInfoW(filePath, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (res != IntPtr.Zero)
                hIcon = shfi.hIcon;
        }

        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            if (!GetIconInfo(hIcon, out var iconInfo))
                return null;

            try
            {
                bool monochrome = iconInfo.hbmColor == IntPtr.Zero;
                IntPtr primaryBmp = monochrome ? iconInfo.hbmMask : iconInfo.hbmColor;
                if (primaryBmp == IntPtr.Zero)
                    return null;

                if (GetObjectW(primaryBmp, Marshal.SizeOf<BITMAP>(), out var bm) == 0)
                    return null;

                int w = bm.bmWidth;
                int h = monochrome ? bm.bmHeight / 2 : bm.bmHeight;
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
                    if (monochrome)
                    {
                        var maskFull = new byte[w * h * 2 * 4];
                        var monoBmi = bmi with { biHeight = -(h * 2) };
                        if (GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)(h * 2), maskFull, ref monoBmi, DIB_RGB_COLORS) == 0)
                            return null;

                        int xorOffset = w * h * 4;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            bool andBit = maskFull[i] != 0;
                            bool xorBit = maskFull[xorOffset + i] != 0;
                            byte c = xorBit ? (byte)255 : (byte)0;
                            pixels[i]     = c;
                            pixels[i + 1] = c;
                            pixels[i + 2] = c;
                            pixels[i + 3] = (!andBit || xorBit) ? (byte)255 : (byte)0;
                        }
                    }
                    else
                    {
                        int lines = GetDIBits(hdc, iconInfo.hbmColor, 0, (uint)h, pixels, ref bmi, DIB_RGB_COLORS);
                        if (lines == 0) return null;

                        bool hasAlpha = false;
                        for (int i = 3; i < pixels.Length; i += 4)
                        {
                            if (pixels[i] != 0) { hasAlpha = true; break; }
                        }

                        if (!hasAlpha)
                        {
                            bool maskApplied = false;
                            if (iconInfo.hbmMask != IntPtr.Zero)
                            {
                                var mask = new byte[w * h * 4];
                                var maskBmi = bmi;
                                if (GetDIBits(hdc, iconInfo.hbmMask, 0, (uint)h, mask, ref maskBmi, DIB_RGB_COLORS) > 0)
                                {
                                    for (int i = 0; i < pixels.Length; i += 4)
                                        pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
                                    maskApplied = true;
                                }
                            }
                            if (!maskApplied)
                            {
                                for (int i = 3; i < pixels.Length; i += 4)
                                    pixels[i] = 255;
                            }
                        }
                    }

                    // Verify non-transparent pixels exist and premultiply alpha for WinUI WriteableBitmap
                    bool anyOpaque = false;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte a = pixels[i + 3];
                        if (a > 0) anyOpaque = true;
                        if (a < 255)
                        {
                            pixels[i]     = (byte)(pixels[i]     * a / 255);
                            pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                            pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                        }
                    }

                    return anyOpaque ? new ExtractedIconData(null, w, h, pixels) : null;
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
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
