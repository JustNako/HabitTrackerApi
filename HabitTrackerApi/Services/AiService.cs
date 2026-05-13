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
            throw new Exception($"Groq API failed: {await response.Content.ReadAsStringAsync()}");

        var responseString = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseString);

        var contentString = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "[]";

        // Groq sometimes wraps json in ```json ... ``` blocks.
        if (contentString.StartsWith("```json"))
        {
            contentString = contentString.Substring(7);
            if (contentString.EndsWith("```"))
                contentString = contentString.Substring(0, contentString.Length - 3);
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(contentString, jsonOptions) ?? new List<string>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI response: {contentString}", ex);
        }
    }


    public async Task<string> GenerateNotificationMessageAsync(string prompt)
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
                    content = "You are a creative, slightly sassy, and highly motivating AI habit coach. The user has some uncompleted habits. Write a SHORT, punchy push notification (max 100 chars) to get them moving.\nCRITICAL RULES:\n1. NEVER just list the habits. Pick one or write a clever general message inspired by them.\n2. Be creative: use humor, light guilt-tripping, or hype.\n3. Max 1 sentence. 1-2 emojis.\n4. DO NOT use quotation marks."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.95,
            max_tokens = 50
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return DefaultNotification();

            var responseString = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseString);

            var text = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(text)) return DefaultNotification();

            return text.Replace("\"", "").Trim();
        }
        catch
        {
            return DefaultNotification();
        }
    }

    public async Task<List<HabitTrackerApi.DTOs.AiHabitDto>> GenerateHabitRoutineAsync(string userPrompt)
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
                    content = @"You are an elite habit designer and lifestyle engineer. The user will provide a goal or problem they want to solve.
Your job is to generate a comprehensive, highly effective daily or weekly habit routine (3 to 5 habits) to help them achieve this goal.

CRITICAL RULES:
1. Make habits specific, actionable, and trackable.
2. Icon should be a simple Material Icon name (e.g., 'fitness_center', 'water_drop', 'book', 'self_improvement', 'code', 'local_fire_department', 'monitor_heart', 'spa', 'directions_run', 'bolt', 'directions_walk', 'bedtime').
3. RepeatType (int): 0 = Every Day, 1 = Specific Days of Week, 2 = Specific Days of Month (rare), 3 = X times per week.
4. If RepeatType is 1, you MUST provide ""repeatDaysOfWeek"": [1, 3, 5] (where 0=Sun, 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat).
5. If RepeatType is 3, you MUST provide ""repeatTimesPerPeriod"": X (e.g. 3).
6. TargetDays (int): usually 1.

FORMAT: Return ONLY a raw JSON array of objects.
Example for flexible schedule:
[
  { ""title"": ""Gym Session"", ""description"": ""Full body workout"", ""icon"": ""fitness_center"", ""repeatType"": 1, ""repeatDaysOfWeek"": [1, 3, 5], ""targetDays"": 1 },
  { ""title"": ""Reading"", ""description"": ""Read 20 pages"", ""icon"": ""book"", ""repeatType"": 0, ""targetDays"": 1 }
]"
                },
                new
                {
                    role = "user",
                    content = userPrompt
                }
            },
            temperature = 0.6,
            max_tokens = 800
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = content;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to generate routine: {response.StatusCode} - {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseString);

        var text = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(text)) return new List<HabitTrackerApi.DTOs.AiHabitDto>();

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

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<HabitTrackerApi.DTOs.AiHabitDto>>(cleanText, options) ?? new List<HabitTrackerApi.DTOs.AiHabitDto>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI habit response: {text}", ex);
        }
    }

    public async Task<string> GenerateMonthlyRecapInsightAsync(string prompt)
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
                    content = "You are a world-class High-Performance Coach. You are summarizing a user's progress for the past month. Your goal is to provide a concise, powerful, and empathetic insight (2-3 sentences) that highlights a win and suggests one area for growth. Do NOT mention specific numbers from the prompt. Focus on behavioral patterns and mindset. Use a professional yet encouraging tone."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.8,
            max_tokens = 200
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return "You did a great job this month! Keep pushing towards your goals.";

            var responseString = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseString);

            var text = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return text?.Trim() ?? "Focus on consistency to unlock your true potential next month.";
        }
        catch
        {
            return "Focus on consistency to unlock your true potential next month.";
        }
    }

    private static List<string> DefaultInsights() => new()
    {
        "Keep building momentum — every day counts!",
        "Consistency beats perfection. Show up daily.",
        "Track one more habit this week to grow further."
    };

    private static string DefaultNotification() => "You still have habits to complete today. Open Flow and knock them out! ⚡";
}
