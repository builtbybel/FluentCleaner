namespace FluentCleaner.Services;

// All my external URLs in one place
public static class AppLinks
{
    public const string GitHub        = "https://github.com/builtbybel/FluentCleaner";
    public const string Issues        = "https://github.com/builtbybel/FluentCleaner/issues";
    public const string Releases      = "https://github.com/builtbybel/FluentCleaner/releases";
    public const string Donate        = "https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN";

    public static async Task OpenAsync(string url)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }
}
