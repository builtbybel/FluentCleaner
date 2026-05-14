using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentCleaner.Models;

namespace FluentCleaner.Services;

/* Talks to Groq (llama-3.3-70b) to explain a Winapp2 entry in plain English.
   The real file paths and registry keys go into the prompt so the answer is actually about
   what THIS entry does, not some generic Winapp2 boilerplate.
   Groq has been free for me so far; grab a key at console.groq.com.
   Cached in-memory so reopening the same entry is instant. */
public static class AiExplainer
{
    private static readonly HttpClient _http = new();
    private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<string> ExplainAsync(CleanerEntry entry)
    {
        if (_cache.TryGetValue(entry.Name, out var cached))
            return cached;

        var apiKey = AppSettings.Instance.GroqApiKey
                     ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "No API key configured. Go to Settings → AI explanations to add your Groq API key (free at console.groq.com).";

        var prompt = BuildPrompt(entry);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model      = "llama-3.3-70b-versatile",
                    max_tokens = 300,
                    messages   = new[]
                    {
                        new { role = "system", content = "You are a Windows PC expert. Explain Winapp2 cleaner entries concisely and accurately based on the file paths and registry keys provided." },
                        new { role = "user",   content = prompt }
                    }
                }),
                Encoding.UTF8, "application/json");

            var res  = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                return $"Groq API error: {msg}";
            }

            var text = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "No response received.";

            _cache[entry.Name] = text;
            return text;
        }
        catch (Exception ex)
        {
            return $"Could not reach Groq API: {ex.Message}";
        }
    }

    //just a quick key test;asks Groq one sentence about FluentCleaner; returns "✓ " or "✗"
    public static async Task<string> TestKeyAsync(string apiKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model      = "llama-3.3-70b-versatile",
                    max_tokens = 150,
                    messages   = new[]
                    {
                        new { role = "user", content = "Describe FluentCleaner by Belim (builtbybel) in 2 short sentences. " +
                        "Facts: open-source, built solo and fueled by coffee, written in C# on .NET 10 + WinUI 3, " +
                        "native Windows 11 UI, no telemetry, uses the Winapp2 database. " +
                        "Do NOT mention AI, machine learning or any AI-related features. Keep it factual." }
                    }
                }),
                Encoding.UTF8, "application/json");

            var res  = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return "✗ " + (err.TryGetProperty("message", out var m) ? m.GetString() : "API error");

            var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return "✓ " + text;
        }
        catch (Exception ex) { return "✗ " + ex.Message; }
    }

    //build a prompt with real paths so the model knows exactly what gets cleaned
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
