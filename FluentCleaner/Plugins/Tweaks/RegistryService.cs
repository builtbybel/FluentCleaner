using Microsoft.Win32;
using System.Globalization;

namespace FluentCleaner.Plugins.Tweaks;

//registry access for the tweak engine;Scanner reads, Applier writes.
//separate from Services/RegistryHelpers (that one only deletes for the cleaner)
internal static class RegistryService
{
    // --- what the ini may ask for --------------------------------------------

    //<delete> = remove the whole key
    public static bool DeletesKey(string value) => value.Equals("<delete>", StringComparison.OrdinalIgnoreCase);

    //<delete> or <deletevalue>
    public static bool Deletes(string value) =>
        DeletesKey(value) || value.Equals("<deletevalue>", StringComparison.OrdinalIgnoreCase);

    //can this text be written as that type? catches typos before writing
    public static bool Fits(string value, string type) => type.ToUpperInvariant() switch
    {
        "DWORD" => Number(value, out var number) && number <= uint.MaxValue,
        "QWORD" => Number(value, out _),
        "BINARY" => Hex(value),
        "STRING" or "EXPANDSTRING" => true,
        _ => false,
    };

    //current value == expected? for a delete, missing = match
    public static bool Matches(string? current, string expected, string type) =>
        Deletes(expected) ? current == null : current != null && Same(current, expected, type);

    // --- one value -----------------------------------------------------------

    //can't read it = unknown, not deleted
    public static bool Matches(Entry entry, string expected)
    {
        try
        {
            var (root, path) = Split(entry.Path);
            if (root == null || path.Length == 0) return false;
            using var key = root.OpenSubKey(path);
            if (DeletesKey(expected)) return key == null;
            var value = key?.GetValue(Name(entry.ValueName), null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return Matches(Text(value), expected, entry.ValueType);
        }
        catch { return false; }
    }

    private static string? Text(object? value)
    {
        if (value == null) return null;
        //windows returns signed numbers, the ini has them unsigned
        if (value is int dword) return unchecked((uint)dword).ToString(CultureInfo.InvariantCulture);
        if (value is long qword) return unchecked((ulong)qword).ToString(CultureInfo.InvariantCulture);
        if (value is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", "");
        return value.ToString();
    }

    //writes one value, creates the key if needed
    public static bool Write(Entry entry, string value)
    {
        if (!Fits(value, entry.ValueType)) return false;
        try
        {
            var (root, path) = Split(entry.Path);
            if (root == null || path.Length == 0) return false;
            using var key = root.CreateSubKey(path, true);
            if (key == null) return false;
            var name = Name(entry.ValueName);
            switch (entry.ValueType.ToUpperInvariant())
            {
                case "DWORD":
                    if (!Number(value, out var dword)) return false;
                    key.SetValue(name, unchecked((int)(uint)dword), RegistryValueKind.DWord);
                    break;
                case "QWORD":
                    if (!Number(value, out var qword)) return false;
                    key.SetValue(name, unchecked((long)qword), RegistryValueKind.QWord);
                    break;
                case "EXPANDSTRING":
                    key.SetValue(name, value, RegistryValueKind.ExpandString);
                    break;
                case "BINARY":
                    key.SetValue(name, Bytes(value), RegistryValueKind.Binary);
                    break;
                default:
                    key.SetValue(name, value, RegistryValueKind.String);
                    break;
            }
            return true;
        }
        catch { return false; }
    }

    //deletes one value;missing key counts as done
    public static bool DeleteValue(Entry entry)
    {
        try
        {
            var (root, path) = Split(entry.Path);
            if (root == null || path.Length == 0) return false;
            using var key = root.OpenSubKey(path, true);
            key?.DeleteValue(Name(entry.ValueName), false);
            return true;
        }
        catch { return false; }
    }

    //deletes the key with everything below
    public static bool DeleteKey(Entry entry)
    {
        try
        {
            var (root, path) = Split(entry.Path);
            if (root == null || path.Length == 0) return false;
            root.DeleteSubKeyTree(path, false);
            return true;
        }
        catch { return false; }
    }

    //key exists? used for RequiresKey
    public static bool Exists(string path)
    {
        try
        {
            var (root, key) = Split(path);
            using var found = root?.OpenSubKey(key);
            return found != null;
        }
        catch { return false; }
    }

    // --- helpers -------------------------------------------------------------

    //"HKCU\Software\X" -> hive + rest
    private static (RegistryKey? Root, string Key) Split(string path)
    {
        var cut = path.IndexOf('\\');
        return cut <= 0 ? (null, "") : (Root(path.Substring(0, cut)), path.Substring(cut + 1));
    }

    //short or long hive name
    private static RegistryKey? Root(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        "HKU" or "HKEY_USERS" => Registry.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        _ => null,
    };

    //(Default) in the ini = "" in the registry
    private static string Name(string valueName) => valueName == "(Default)" ? "" : valueName;

    //numbers as numbers (1 == 0x1), binary without separators, rest as text
    private static bool Same(string current, string expected, string type) =>
        (type.Equals("DWORD", StringComparison.OrdinalIgnoreCase)
            || type.Equals("QWORD", StringComparison.OrdinalIgnoreCase))
        && Number(current, out var left) && Number(expected, out var right)
            ? left == right
            : type.Equals("BINARY", StringComparison.OrdinalIgnoreCase)
                ? string.Equals(Pairs(current), Pairs(expected), StringComparison.OrdinalIgnoreCase)
                : string.Equals(current.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    //decimal or 0x, as ulong;4294967295 doesn't fit an int
    private static bool Number(string value, out ulong number)
    {
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number)
            : ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    //valid hex? odd count = typo
    private static bool Hex(string value)
    {
        var clean = Pairs(value);
        return clean.Length > 0 && clean.Length % 2 == 0 && clean.All(Uri.IsHexDigit);
    }

    //hex -> bytes
    private static byte[] Bytes(string value)
    {
        var clean = Pairs(value);
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    //strips spaces, commas and dashes from a binary value
    private static string Pairs(string value) =>
        value.Replace(" ", "").Replace(",", "").Replace("-", "");
}
