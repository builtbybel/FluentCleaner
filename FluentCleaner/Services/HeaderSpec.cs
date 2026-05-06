using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace FluentCleaner.Services;

//Reads Windows version, CPU name, and RAM amount for display in TerminalPage
public static class HeaderSpec
{
    public static string WindowsVersion { get; } = ReadWindowsVersion();
    public static string CpuName        { get; } = ReadCpuName();
    public static string RamLabel       { get; } = ReadRam();

    private static string ReadWindowsVersion()
    {
        using var key = Registry.LocalMachine
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null) return "Windows";

        var product = key.GetValue("ProductName") as string ?? "Windows";
        var display = key.GetValue("DisplayVersion") as string; // "23H2", "24H2"

        //productName
        if (int.TryParse(key.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22000)
            product = product.Replace("Windows 10", "Windows 11");

        return display is not null ? $"{product} {display}" : product;
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine
            .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown CPU";
    }

    private static string ReadRam()
    {
        if (GetPhysicallyInstalledSystemMemory(out ulong kb))
            return $"{(int)Math.Round((double)kb / (1024 * 1024))} GB RAM";
        return string.Empty;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong TotalMemoryInKilobytes);
}
