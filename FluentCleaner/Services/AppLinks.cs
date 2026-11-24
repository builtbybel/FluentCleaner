namespace FluentCleaner.Services;

// All my external URLs in one place
public static class AppLinks
{
    public const string GitHub        = "https://github.com/builtbybel/FluentCleaner";
    public const string LatestRelease = "https://github.com/builtbybel/FluentCleaner/releases/latest";
    public const string Releases      = "https://github.com/builtbybel/FluentCleaner/releases";
    public const string Issues        = "https://github.com/builtbybel/FluentCleaner/issues";
    public const string Donate        = "https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN";
    public const string KoFi          = "https://ko-fi.com/builtbybel";
    public const string Faq           = "https://github.com/builtbybel/FluentCleaner/blob/main/README.md#faq";
    public const string IconCredit    = "https://github.com/naderi";
    public const string Site          = "https://builtbybel.github.io/FluentCleaner/";
    public const string VersionCheck  = "https://raw.githubusercontent.com/builtbybel/FluentCleaner/main/version.txt";

    // Custom cleaner community & sharing
    public const string ShareCleaner  = "https://github.com/builtbybel/FluentCleaner/issues";
    public const string Winapp2Repo   = "https://github.com/MoscaDotTo/Winapp2";
    public const string Reddit        = "https://www.reddit.com/r/windows/";
    public const string Neowin        = "https://www.neowin.net/forum/";
    public const string Deskmodder    = "https://www.deskmodder.de/";

    // The page compares what it is told against versions.json and offers the download.
    public static string CreateUpdateUrl(string edition, string version) =>
        $"{Site}?edition={Uri.EscapeDataString(edition)}&version={Uri.EscapeDataString(version)}";

    public static string CreatePayPalDonationUrl(int amount, string currency)
    {
        var item = Uri.EscapeDataString("Support development of FluentCleaner");
        var business = Uri.EscapeDataString("belim@builtbybel.com");
        return $"https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business={business}" +
               $"&amount={amount}&currency_code={Uri.EscapeDataString(currency)}&item_name={item}";
    }

    public static async Task OpenAsync(string url)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }
}
