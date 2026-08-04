using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeverageLabelAgent.Server.Services;

/// <summary>
/// Service for interacting with Google Gemini API.
/// Uses the REST API directly (no SDK dependency).
/// </summary>
public class GeminiLLMService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiLLMService> _logger;

    public GeminiLLMService(IConfiguration configuration, ILogger<GeminiLLMService> logger, HttpClient httpClient)
    {
        _apiKey = configuration["Gemini:ApiKey"] ?? "";
        _model = configuration["Gemini:Model"] ?? "gemini-2.0-flash";
        _logger = logger;
        _httpClient = httpClient;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Sends a chat request to Gemini and returns the response text.
    /// </summary>
    public async Task<string> ChatAsync(string systemPrompt, List<GeminiMessage> history, string userMessage)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Gemini API key is not configured. Using fallback agent.");
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        // Build the request body
        var contents = new List<object>();

        // Add conversation history
        foreach (var msg in history)
        {
            contents.Add(new
            {
                role = msg.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = msg.Content } }
            });
        }

        // Add current user message
        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = userMessage } }
        });

        var requestBody = new Dictionary<string, object>
        {
            ["system_instruction"] = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            ["contents"] = contents,
            ["generationConfig"] = new Dictionary<string, object>
            {
                ["temperature"] = 0.7,
                ["topP"] = 0.95,
                ["maxOutputTokens"] = 4096,
                ["responseMimeType"] = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
        { 
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        _logger.LogDebug("Sending request to Gemini API");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API error {StatusCode}: {Response}", response.StatusCode, responseContent);
            throw new HttpRequestException($"Gemini API returned {response.StatusCode}: {responseContent}");
        }

        // Parse the response
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
            {
                return parts[0].GetProperty("text").GetString() ?? "";
            }
        }

        _logger.LogWarning("Unexpected Gemini response structure: {Response}", responseContent);
        return "";
    }
}

public class GeminiMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
