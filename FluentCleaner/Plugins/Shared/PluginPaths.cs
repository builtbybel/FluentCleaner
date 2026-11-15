namespace FluentCleaner.Plugins;

/// <summary>
/// A plugin's own data folder at runtime: <c>Plugins\&lt;Name&gt;\</c> beside the exe, the
/// same layout as the source tree.
/// </summary>
internal static class PluginPaths
{
    public static string Dir(string plugin) =>
        Path.Combine(AppContext.BaseDirectory, "Plugins", plugin);
}
