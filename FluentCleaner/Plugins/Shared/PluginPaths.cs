namespace FluentCleaner.Plugins;

/// <summary>
/// Plugin data folder at runtime: <c>Plugins\&lt;Name&gt;\</c> next to the exe, same as in the source.
/// </summary>
internal static class PluginPaths
{
    public static string Dir(string plugin) =>
        Path.Combine(AppContext.BaseDirectory, "Plugins", plugin);

    // opens Explorer with the rules file selected (footer link)
    public static void Reveal(string plugin, string file) =>
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Path.Combine(Dir(plugin), file)}\"");
}
