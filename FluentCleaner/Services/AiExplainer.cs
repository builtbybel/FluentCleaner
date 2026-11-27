using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

// Explains Winapp2 entries and generates Custom Cleaners through the provider
// selected in Settings. Results are cached per provider, language and rule.
public static class AiExplainer
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static (string BaseUrl, string Model) ProviderInfo(string provider) => provider switch
    {
        "OpenAI"    => ("https://api.openai.com/v1/chat/completions", "gpt-4o-mini"),
        "Anthropic" => ("https://api.anthropic.com/v1/messages", "claude-haiku-4-5-20251001"),
        _           => ("https://api.groq.com/openai/v1/chat/completions", "openai/gpt-oss-120b"),
    };

    private static string? ApiKey(string provider) => provider switch
    {
        "OpenAI"    => AppSettings.Instance.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        "Anthropic" => AppSettings.Instance.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        _           => AppSettings.Instance.GroqApiKey ?? Environment.GetEnvironmentVariable("GROQ_API_KEY"),
    };

    private static bool IsAnthropic(string provider) => provider == "Anthropic";

    public static bool HasConfiguredKey =>
        !string.IsNullOrWhiteSpace(ApiKey(AppSettings.Instance.AiProvider));

    private static string ProviderMessage(string key, string provider, string detail)
    {
        var template = ResourceService.Get(key);
        return template.Contains("{1}", StringComparison.Ordinal)
            ? string.Format(template, provider, detail)
            : $"{provider}: {detail}";
    }

    public static async Task<string> ExplainAsync(CleanerEntry entry)
    {
        var provider = AppSettings.Instance.AiProvider;
        var cacheKey = $"{provider}\n{AppSettings.Instance.Language}\n{entry.RawText}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var apiKey = ApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return $"{provider}: {ResourceService.Get("AI_NoKeyShort")}";

        var systemPrompt = "You are a Windows PC expert. Explain Winapp2 cleaner entries concisely and accurately based on the file paths and registry keys provided." + LangInstruction();
        var (text, error) = await SendChatAsync(provider, apiKey!, systemPrompt, BuildPrompt(entry), 300);
        if (error != null)
            return ProviderMessage("AI_ApiError", provider, error);

        _cache[cacheKey] = text!;
        return text!;
    }

    // Generates a Winapp2 INI entry from a plain-English description.
    public static Task<string> GenerateEntryAsync(string description) =>
        GenerateAsync(
            userMsg: $"Generate a Winapp2 cleaner entry for: {description}",
            errorPrefix: "; ",
            systemPrompt:
                "You are a Winapp2 database expert. Generate a valid Winapp2 INI cleaner entry from the user description. " +
                "Output ONLY raw INI — no explanation, no markdown, no code fences.\n" +
                "STRUCTURE: [App Name]\n" +
                "DETECTION (include at least one):\n" +
                "  DetectKey=HKLM\\Software\\App  or  HKLM\\Software\\App|ValueName\n" +
                "  DetectFile=%LocalAppData%\\App\\*\n" +
                "  SpecialDetect=DET_CHROME|DET_FIREFOX|DET_EDGE|DET_OPERA|DET_THUNDERBIRD|DET_IE|DET_WINSTORE\n" +
                "  Multiple detect lines use OR logic.\n" +
                "FILE KEYS:\n" +
                "  FileKey1=PATH|PATTERN  or  PATH|PATTERN|RECURSE  or  PATH|PATTERN|REMOVESELF\n" +
                "  Multiple patterns: FileKey1=PATH|*.log;*.tmp;*.bak\n" +
                "REGISTRY KEYS:\n" +
                "  RegKey1=HKCU\\Software\\App  or  HKCU\\Software\\App|ValueName\n" +
                "  Hives: HKCU HKLM HKCR HKU HKCC\n" +
                "EXCLUDE KEYS:\n" +
                "  ExcludeKey1=FILE|%AppData%\\App\\|important.dat\n" +
                "  ExcludeKey2=PATH|%AppData%\\App\\Keep\\\n" +
                "PATH VARIABLES:\n" +
                "  %AppData% %LocalAppData% %LocalLowAppData% %ProgramData%\n" +
                "  %ProgramFiles% %ProgramFiles(x86)% %UserProfile%\n" +
                "  %SystemRoot% %System% %SystemDrive% %Temp%\n" +
                "  %Documents% %Desktop% %Music% %Pictures% %Videos%\n" +
                "OTHER FIELDS: Section=  Warning=  Default=True|False");

    // Sends generation requests through the same provider selected for explanations.
    private static async Task<string> GenerateAsync(string userMsg, string systemPrompt, string errorPrefix)
    {
        var provider = AppSettings.Instance.AiProvider;
        var apiKey = ApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
            return $"{errorPrefix}{provider}: {ResourceService.Get("AI_NoKeyShort")}";

        var (text, error) = await SendChatAsync(provider, apiKey!, systemPrompt, userMsg, 500);
        return error == null
            ? text!
            : $"{errorPrefix}{ProviderMessage("AI_ApiError", provider, error)}";
    }

    // A small real request verifies both the key and the selected provider.
    public static async Task<string> TestKeyAsync(string apiKey, string provider)
    {
        var userMsg = "Describe FluentCleaner by Belim (builtbybel) in 2 short sentences. " +
            "Facts: open-source, built solo, written in C# on .NET 10 and WinUI 3, native Windows UI, " +
            "no telemetry, uses the Winapp2 database. Do NOT mention AI, machine learning or any " +
            "AI-related features. Keep it factual." + LangInstruction();

        var (text, error) = await SendChatAsync(provider, apiKey, systemPrompt: null, userMsg, maxTokens: 512);
        return error != null ? $"✗ {error}" : $"✓ {text}";
    }

    // Groq and OpenAI use chat completions; Anthropic uses its own message shape.
    private static async Task<(string? Text, string? Error)> SendChatAsync(
        string provider, string apiKey, string? systemPrompt, string userMsg, int maxTokens)
    {
        var (baseUrl, model) = ProviderInfo(provider);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);

            // Built as a JsonObject rather than anonymous types: those are serialized through
            // reflection, which the AOT build doesn't have.
            static JsonNode Message(string role, string content) => new JsonObject { ["role"] = role, ["content"] = content };

            var messages = new JsonArray();
            var body = new JsonObject { ["model"] = model };

            if (IsAnthropic(provider))
            {
                req.Headers.Add("x-api-key", apiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                body["max_tokens"] = maxTokens;
                if (systemPrompt is not null) body["system"] = systemPrompt;
            }
            else
            {
                req.Headers.Add("Authorization", $"Bearer {apiKey}");
                if (systemPrompt is not null) messages.Add(Message("system", systemPrompt));

                if (provider == "Groq")
                {
                    body["max_completion_tokens"] = maxTokens;
                    body["reasoning_effort"] = "low";
                    body["reasoning_format"] = "hidden";
                }
                else
                {
                    body["max_tokens"] = maxTokens;
                }
            }

            messages.Add(Message("user", userMsg));
            body["messages"] = messages;

            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)
                    ? m.GetString() : err.GetString();
                return (null, msg ?? "Unknown error");
            }

            var text = IsAnthropic(provider)
                ? root.GetProperty("content")[0].GetProperty("text").GetString()
                : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            return string.IsNullOrWhiteSpace(text)
                ? (null, ResourceService.Get("AI_NoResponse"))
                : (text, null);
        }
        catch (Exception ex)
        {
            return (null, ProviderMessage("AI_NetworkError", provider, ex.Message));
        }
    }

    // Match the answer language to the active Modern UI language.
    private static string LangInstruction()
    {
        var lang = AppSettings.Instance.Language;
        if (string.IsNullOrWhiteSpace(lang))
            lang = CultureInfo.CurrentUICulture.Name;

        if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        try
        {
            var name = CultureInfo.GetCultureInfo(lang).Parent.EnglishName;
            return $" Please respond in {name}.";
        }
        catch { return string.Empty; }
    }

    // Include the real paths so the explanation is about this rule, not its title alone.
    private static string BuildPrompt(CleanerEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Explain what the Winapp2 cleaner entry \"{entry.Name}\" cleans and whether it is safe to delete.");

        if (!string.IsNullOrWhiteSpace(entry.Warning))
            sb.AppendLine($"Warning from the database: {entry.Warning}");

        if (entry.FileKeys.Count > 0)
        {
            sb.AppendLine("It deletes files from these locations:");
            foreach (var fk in entry.FileKeys.Take(6))
                sb.AppendLine($"  - {fk.Path}  (pattern: {fk.Pattern})");
        }

        if (entry.RegKeys.Count > 0)
        {
            sb.AppendLine("It removes these registry keys:");
            foreach (var rk in entry.RegKeys.Take(4))
                sb.AppendLine($"  - {rk.KeyPath}");
        }

        sb.AppendLine("Answer in 2-3 sentences. Be specific and practical.");
        return sb.ToString();
    }
}
