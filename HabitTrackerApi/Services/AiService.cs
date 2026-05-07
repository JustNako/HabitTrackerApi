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
                    content = @"You are an Elite Performance Psychologist and Habit Specialist. Your goal is to provide deep, neurobiology-based insights and unconventional productivity strategies.
                    
                    CRITICAL RULES:
                    1. PATTERN RECOGNITION: Don't just list stats. Identify hidden trends (e.g., 'The Wednesday Slump', 'Over-commitment burnout', 'Success-chain momentum').
                    2. BEHAVIORAL SCIENCE: Use concepts like 'Habit Stacking', 'Implementation Intentions', 'Dopamine reward loops', or 'Identity-shifting'.
                    3. ACTIONABLE DEPTH: Every insight must contain a 'Why' (psychology) and a 'How' (concrete action).
                    4. NO ECHOING: Never repeat the data provided in the prompt. If I say 'completion is 20%', don't say 'your completion is low'. Say 'Your consistency suggests a friction in your morning environment; try pre-loading your cues'.
                    5. TONE: Professional, sophisticated, encouraging but firm. Like a $500/hour executive coach.
                    
                    FORMAT: Return ONLY a raw JSON array of 3 strings. Each string should be 2-3 sentences long for maximum impact."
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

            int startIndex = cleanText.IndexOf('[');
            int endIndex = cleanText.LastIndexOf(']');

            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                cleanText = cleanText.Substring(startIndex, endIndex - startIndex + 1);
            }

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
