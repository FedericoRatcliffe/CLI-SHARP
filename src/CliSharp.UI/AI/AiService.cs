using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CliSharp.UI.AI;

/// <summary>
/// Claude API client using direct HttpClient (no extra NuGet)).
/// Config: apiKey + model in AppConfig.
/// </summary>
public sealed class AiService
{
    private readonly HttpClient _http = new();
    private string _apiKey = "";
    private string _model = "claude-sonnet-4-20250514";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public void Configure(string apiKey, string model)
    {
        _apiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(model)) _model = model;
    }

    public async Task<string> AskAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured) return "[AI no configurada — setear ai.apiKey en config.json]";

        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        try
        {
            var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                return $"[Error API {(int)res.StatusCode}]";

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";
        }
        catch (TaskCanceledException) { return "[Cancelled]"; }
        catch (Exception ex) { return $"[Error: {ex.Message}]"; }
    }
}

/// <summary>
/// System prompts for each AI use case.
/// </summary>
public static class AiPrompts
{
    public const string GenerateCommand =
        "You are a terminal command assistant. The user describes what they want to do. " +
        "Respond with ONLY the shell command, no explanation, no markdown fences. " +
        "The user is on Windows using PowerShell. " +
        "If multiple commands are needed, join them with ; or &&.";

    public const string ExplainCommand =
        "Explain this shell command in simple Spanish. " +
        "Break down each part briefly. Use bullet points. Be concise (max 8 lines).";

    public const string DebugError =
        "A shell command failed with a non-zero exit code. " +
        "Analyze the command and output below, then suggest a fix. " +
        "Be concise (max 10 lines). Respond in Spanish.";

    public const string SummarizeOutput =
        "Summarize the following terminal output concisely. " +
        "Highlight the most important information in 3-5 bullet points. " +
        "Respond in Spanish.";
}
