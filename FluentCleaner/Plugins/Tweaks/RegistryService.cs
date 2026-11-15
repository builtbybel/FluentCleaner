using Microsoft.Win32;
using System.Globalization;

namespace FluentCleaner.Plugins.Tweaks;

//everything the tweak engine knows about the registry. the scanner reads through it, the applier
//writes through it. Deliberately separate from Services/RegistryHelpers, which only opens hives
//for the cleaner: that one deletes keys, this one writes values and can put them back, and the
//two have different failure modes worth keeping apart.
internal static class RegistryService
{
    // --- what the catalogue may ask for --------------------------------------

    //whether the ini asked for the whole key to go, which is what <delete> means
    public static bool DeletesKey(string value) => value.Equals("<delete>", StringComparison.OrdinalIgnoreCase);

    //whether it asked for a removal at all, key or single value
    public static bool Deletes(string value) =>
        DeletesKey(value) || value.Equals("<deletevalue>", StringComparison.OrdinalIgnoreCase);

    //whether this text can be written as that type, so a typo fails before anything is touched
    public static bool Fits(string value, string type) => type.ToUpperInvariant() switch
    {
        "DWORD" => Number(value, out var number) && number <= uint.MaxValue,
        "QWORD" => Number(value, out _),
        "BINARY" => Hex(value),
        "STRING" or "EXPANDSTRING" => true,
        _ => false,
    };

    //whether what stands on the pc is what the catalogue wants; for a delete, absence is the match
    public static bool Matches(string? current, string expected, string type) =>
        Deletes(expected) ? current == null : current != null && Same(current, expected, type);

    // --- one value -----------------------------------------------------------

    //An unreadable location is unknown, not proof that a requested deletion is complete.
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
        //windows hands numbers back signed, the ini spells them unsigned
        if (value is int dword) return unchecked((uint)dword).ToString(CultureInfo.InvariantCulture);
        if (value is long qword) return unchecked((ulong)qword).ToString(CultureInfo.InvariantCulture);
        if (value is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", "");
        return value.ToString();
    }

    //writes one value in the type the ini named, creating the key if it is not there yet
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

    //removes one value; a key that is not there has nothing left to remove, so that counts as done
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

    //removes the key and everything under it, the same way regedit would
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

    //whether a key is there at all; a tweak for a program this pc has not got is never listed
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

    // --- reading the catalogue's wording -------------------------------------

    //cuts one ini path into its hive and the rest of the key
    private static (RegistryKey? Root, string Key) Split(string path)
    {
        var cut = path.IndexOf('\\');
        return cut <= 0 ? (null, "") : (Root(path.Substring(0, cut)), path.Substring(cut + 1));
    }

    //the hive behind its name, in either the short or the long spelling the ini may use
    private static RegistryKey? Root(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        "HKU" or "HKEY_USERS" => Registry.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        _ => null,
    };

    //the empty name is what windows calls the unnamed value, the ini writes (Default) for it
    private static string Name(string valueName) => valueName == "(Default)" ? "" : valueName;

    //compares two values: numbers as numbers, so 1 and 0x1 are equal, everything else as text
    private static bool Same(string current, string expected, string type) =>
        (type.Equals("DWORD", StringComparison.OrdinalIgnoreCase)
            || type.Equals("QWORD", StringComparison.OrdinalIgnoreCase))
        && Number(current, out var left) && Number(expected, out var right)
            ? left == right
            : type.Equals("BINARY", StringComparison.OrdinalIgnoreCase)
                ? string.Equals(Pairs(current), Pairs(expected), StringComparison.OrdinalIgnoreCase)
                : string.Equals(current.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    //parses a decimal or 0x number wide, because a dword spelled 4294967295 fits no int
    private static bool Number(string value, out ulong number)
    {
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number)
            : ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    //whether this is readable hex: two digits make one byte, so an odd count is a typo
    private static bool Hex(string value)
    {
        var clean = Pairs(value);
        return clean.Length > 0 && clean.Length % 2 == 0 && clean.All(Uri.IsHexDigit);
    }

    //turns that hex into the bytes windows wants
    private static byte[] Bytes(string value)
    {
        var clean = Pairs(value);
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    //drops the spaces, commas and dashes a binary value may be grouped with in the ini
    private static string Pairs(string value) =>
        value.Replace(" ", "").Replace(",", "").Replace("-", "");
}
