using System.Text;
using System.Text.RegularExpressions;

namespace FluentCleaner.Services;

// FluentSeed = cleaner selection as text, FC1-K7Q2XA-9MZ3PD-..., 6 chars per ticked entry.
// each group is a hash of the entry name;no names or paths in it, receiver matches against its own Winapp2.
// was "setup code" until 26.11.108, same format, old codes still work
public static class FluentSeed
{
    private const string Prefix = "FC1";

    // Crockford base32, no I, L, O, U (easy to misread)
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // 6 chars = 30 bits;with ~4000 entries a collision is very unlikely, and if so both get ticked (harmless)
    private const int GroupLength = 6;

    // anywhere in the text, so pasting a whole post works too
    private static readonly Regex Pattern =
        new(@"FC1(?:-[0-9A-HJKMNP-TV-Z]{6})+", RegexOptions.IgnoreCase);

    // sorted, same selection = same code
    public static string Encode(IEnumerable<string> names) =>
        string.Join("-", names.Select(Hash).Distinct().Order().Select(ToGroup).Prepend(Prefix));

    // hashes from the text, null if there's no code in it
    public static HashSet<uint>? TryParse(string text)
    {
        var match = Pattern.Match(text);
        return match.Success
            ? match.Value.Split('-').Skip(1).Select(FromGroup).ToHashSet()
            : null;
    }

    public sealed record Resolution(List<string> Found, int NotInstalled, int Unknown, int Total);

    // splits a code into: ticked here, app not installed, unknown (local Winapp2 is older than the sender's)
    public static Resolution Resolve(HashSet<uint> code, IEnumerable<string> installed, HashSet<uint> catalogue)
    {
        var found = installed.Where(name => code.Contains(Hash(name))).ToList();
        var foundHashes = found.Select(Hash).ToHashSet();

        int notInstalled = 0, unknown = 0;
        foreach (var hash in code.Where(h => !foundHashes.Contains(h)))
        {
            if (catalogue.Contains(hash)) notInstalled++;
            else unknown++;
        }
        return new Resolution(found, notInstalled, unknown, code.Count);
    }

    // hashes of all entries in the databases, installed or not
    public static Task<HashSet<uint>> CatalogueAsync(IEnumerable<string> databasePaths) => Task.Run(async () =>
    {
        var parser = new Winapp2Parser();
        var hashes = new HashSet<uint>();
        foreach (var path in databasePaths)
        {
            try
            {
                foreach (var entry in await parser.ParseFileAsync(path))
                    hashes.Add(Hash(entry.Name));
            }
            catch { /* a missing database just means fewer known names */ }
        }
        return hashes;
    });

    // FNV-1a over the lowercase UTF-8 name. not string.GetHashCode, that changes every run
    private static uint Hash(string name)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()))
            hash = (hash ^ b) * 16777619;
        return (hash ^ (hash >> 30)) & 0x3FFFFFFF;
    }

    private static string ToGroup(uint value)
    {
        var chars = new char[GroupLength];
        for (int i = GroupLength - 1; i >= 0; i--, value >>= 5)
            chars[i] = Alphabet[(int)(value & 31)];
        return new string(chars);
    }

    private static uint FromGroup(string group)
    {
        uint value = 0;
        foreach (char c in group.ToUpperInvariant())
            value = (value << 5) | (uint)Alphabet.IndexOf(c);
        return value;
    }
}
