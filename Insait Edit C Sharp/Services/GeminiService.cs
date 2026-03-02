using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Lightweight Google Gemini API client.
/// The model is not hard-coded — it is supplied per request so that
/// both "pro" and "flash" variants (and any future models) can be used.
///
/// API key is loaded from <see cref="SettingsDbService.LoadGeminiApiKey"/>.
///
/// Docs: https://ai.google.dev/gemini-api/docs/text-generation
/// </summary>
public static class GeminiService
{
    // No fixed timeout — translation of large AXAML files can take several minutes.
    // Cancellation is controlled exclusively via CancellationToken passed per-request.
    private static readonly HttpClient _http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a chat/generate request to Gemini and returns the text reply.
    /// </summary>
    /// <param name="model">
    /// Gemini model string, e.g. <c>"gemini-1.5-pro"</c>, <c>"gemini-2.0-flash"</c>.
    /// </param>
    /// <param name="prompt">The user prompt text.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>Model text reply, or an error message starting with "Error:".</returns>
    public static async Task<string> GenerateAsync(
        string model,
        string prompt,
        CancellationToken ct = default)
    {
        var apiKey = SettingsDbService.LoadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Error: Gemini API key is not set. Open Menu → Settings → Gemini and enter your API key.";

        if (string.IsNullOrWhiteSpace(model))
            return "Error: No Gemini model specified.";

        try
        {
            var url = $"{BaseUrl}/{model.Trim()}:generateContent?key={apiKey}";

            var body = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = prompt } } }
                }
            };

            var json    = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await _http.PostAsync(url, content, ct);
            var raw  = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return $"Error {(int)resp.StatusCode}: {ExtractApiError(raw)}";

            return ExtractText(raw) ?? "(empty response)";
        }
        catch (TaskCanceledException)
        {
            return "Error: Request cancelled or timed out.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Translates the English dictionary AXAML content into the target language.
    /// Sends the full AXAML content to Gemini and returns translated AXAML.
    /// </summary>
    public static async Task<string> TranslateAxamlAsync(
        string model,
        string languageName,
        string englishAxamlContent,
        CancellationToken ct = default)
    {
        var prompt =
            $"You are a professional software localizer.\n" +
            $"Translate the string values inside the following AXAML ResourceDictionary from English to {languageName}.\n" +
            $"Rules:\n" +
            $"- Keep all XML tags, x:Key attributes, and structure exactly the same.\n" +
            $"- Only translate the TEXT CONTENT of <x:String> elements.\n" +
            $"- Do NOT translate x:Key names, emoji, format placeholders like {{0}}, shortcut strings like Ctrl+S, or technical tokens.\n" +
            $"- Return ONLY the translated AXAML, no explanations.\n\n" +
            $"AXAML to translate:\n{englishAxamlContent}";

        return await GenerateAsync(model, prompt, ct);
    }

    // ── JSON helpers ─────────────────────────────────────────────────────

    private static string? ExtractText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var candidates = doc.RootElement
                .GetProperty("candidates");
            foreach (var cand in candidates.EnumerateArray())
            {
                var content = cand.GetProperty("content");
                var parts   = content.GetProperty("parts");
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t))
                        return t.GetString();
                }
            }
        }
        catch { /* malformed */ }
        return null;
    }

    private static string ExtractApiError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? json;
        }
        catch { /* not json */ }
        return json.Length > 200 ? json[..200] + "..." : json;
    }
}

