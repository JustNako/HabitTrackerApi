using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HabitTrackerApi.Services;

public class AiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string Model = "llama-3.3-70b-versatile";

    public AiService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["Groq:ApiKey"] ?? throw new InvalidOperationException("Groq API Key is missing.");
    }

    public async Task<List<string>> GenerateInsightsAsync(string prompt)
    {
        var url = "https://api.groq.com/openai/v1/chat/completions";

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a sharp, no-nonsense habit coach. Analyze the user's weekly habit data and return 3 insights. Each insight must cover a DIFFERENT angle — do NOT repeat the same idea in different words. Pick from: specific habit callout, root cause hypothesis, concrete micro-action, scheduling tip, or momentum observation. Be direct. No filler phrases like 'it seems', 'it looks like', 'indicating'. Each insight is max 15 words. Vary your sentence structure. Return ONLY a raw JSON array of 3 strings."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.7,
            max_tokens = 400
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to generate insights: {response.StatusCode} - {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseString);

        var text = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrEmpty(text))
            return DefaultInsights();

        try
        {
            var cleanText = text.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("```", "")
                                .Trim();

            var insights = JsonSerializer.Deserialize<List<string>>(cleanText);
            return insights?.Count > 0 ? insights : DefaultInsights();
        }
        catch
        {
            return DefaultInsights();
        }
    }

    private static List<string> DefaultInsights() => new()
    {
        "Keep building momentum — every day counts!",
        "Consistency beats perfection. Show up daily.",
        "Track one more habit this week to grow further."
    };
}
