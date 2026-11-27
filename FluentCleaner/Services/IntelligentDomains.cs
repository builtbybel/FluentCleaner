using System;
using System.Collections.Generic;
using System.Linq;

namespace FluentCleaner.Services;

/* Knowledge base of canonical authentication, session, and identity provider domains.
   Benchmark: CCleaner 'Cookies to Keep' preset + modern developer/cloud SSO providers. */
internal static class IntelligentDomains
{
    // Canonical authentication and identity domains to auto-preserve
    public static readonly IReadOnlyList<string> KnownAuthDomains =
    [
        // Google & YouTube Ecosystem
        "google.com",
        "accounts.google.com",
        "google.co.in",
        "google.co.uk",
        "google.ca",
        "google.de",
        "google.fr",
        "google.co.jp",
        "gstatic.com",
        "youtube.com",

        // Microsoft & Office Ecosystem
        "microsoft.com",
        "login.microsoftonline.com",
        "login.live.com",
        "live.com",
        "outlook.com",
        "office.com",
        "office365.com",
        "microsoftonline.com",
        "bing.com",

        // Apple & iCloud
        "apple.com",
        "idmsa.apple.com",
        "icloud.com",

        // Developer, Cloud & AI Infrastructure
        "github.com",
        "githubstatus.com",
        "gitlab.com",
        "stackoverflow.com",
        "cloudflare.com",
        "openai.com",
        "chatgpt.com",
        "anthropic.com",

        // Professional & Social Networks
        "twitter.com",
        "x.com",
        "linkedin.com",
        "reddit.com",
        "facebook.com",
        "instagram.com",
        "threads.net",
        "discord.com",

        // Shopping, Financial & Payments
        "amazon.com",
        "amazon.in",
        "amazon.co.uk",
        "amazon.de",
        "paypal.com",
        "stripe.com",

        // Webmail & Secure Messaging
        "yahoo.com",
        "login.yahoo.com",
        "proton.me",
        "protonmail.com"
    ];

    private static readonly HashSet<string> ExactDomainSet = new(
        KnownAuthDomains.Select(CookieService.NormalizeDomain).Where(d => d.Length > 0),
        StringComparer.OrdinalIgnoreCase);

    // Checks whether a given cookie domain represents a recognized authentication provider
    public static bool IsAuthDomain(string domain)
    {
        var normalized = CookieService.NormalizeDomain(domain);
        if (normalized.Length == 0) return false;

        if (ExactDomainSet.Contains(normalized)) return true;

        // Check if the domain is a subdomain of any known auth domain (e.g. auth.github.com -> github.com)
        foreach (var auth in KnownAuthDomains)
        {
            if (normalized.EndsWith("." + auth, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
