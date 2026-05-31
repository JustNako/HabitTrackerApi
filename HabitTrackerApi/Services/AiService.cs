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
                    content = @"You are an Empathetic Lifestyle Teammate and Habit Partner. Your goal is to help the user stay consistent without feeling overwhelmed or guilty. You focus on 'Progress over Perfection'.
                    
                    CRITICAL RULES:
                    1. EMPATHY FIRST: If consistency is low, do NOT criticize. Instead, suggest 'Scaling Down' (e.g., 'If 30 mins is too much today, just do 2 mins to keep the rhythm').
                    2. PATTERN RECOGNITION: Identify trends like 'The Wednesday Slump' or 'Weekend Recovery' with a supportive tone.
                    3. FRICTION REDUCTION: Suggest ways to make habits easier (e.g., 'Lay out your clothes the night before').
                    4. TONE: Warm, supportive, clever, and humble. Like a best friend who is also a productivity nerd. Never 'firm' or 'nagging'.
                    5. NO ECHOING: Don't repeat stats. Provide a 'Why' (psychology) and a 'Small Win' (next action).
                    6. TIME-BASED PATTERNS: Analyze the scheduled times of habits (if provided). Identify if the user is more successful with morning, afternoon, or evening habits, and suggest optimizations (e.g., 'Moving your Gym session to 18:30 since your evening routines are so solid!').
                    
                    FORMAT: Return ONLY a raw JSON array of 3 strings. Each string should be 2-3 sentences long."
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
                    content = "You are a supportive, clever, and encouraging AI habit teammate. The user has some uncompleted habits. Write a SHORT, frictionless push notification (max 100 chars) to gently remind them that you're in this together.\nCRITICAL RULES:\n1. NO GUILT-TRIPPING: Never use shame or sass. Focus on the positive feeling of finishing.\n2. ACTIONABLE: Suggest 'The 2-Minute Rule' if it feels like a lot.\n3. Tone: Like a friendly 'nudge' from a teammate. Do NOT use any emojis.\n4. DO NOT use quotation marks or mention specific habit names unless it fits perfectly."
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
6. TargetDays (int): This is the DAILY GOAL (quantity). If the habit is quantitative (e.g. ""Drink water"", ""Reading"", ""Exercise""), provide a reasonable daily target (e.g. 2000 for ml, 8 for glasses, 20 for pages, 30 for minutes). If the habit is binary (Done/Not Done), set it to 1. BE SMART: don't set 0.
7. Unit (string): YOU MUST PROVIDE A UNIT if the habit is quantitative (e.g., ""ml"", ""glasses"", ""pages"", ""mins"", ""times""). Never leave it empty if TargetDays > 1. If binary (TargetDays = 1), you can omit it.
8. TimeOfDay (string): Provide a 24-hour time string in the format ""HH:mm"" (e.g., ""08:00"", ""14:30"", ""21:00"") if a habit belongs to a specific routine time of day. This is optional.

FORMAT: Return ONLY a raw JSON array of objects.
Example for flexible schedule:
[
  { ""title"": ""Gym Session"", ""description"": ""Intense workout"", ""icon"": ""fitness_center"", ""repeatType"": 1, ""repeatDaysOfWeek"": [1, 3, 5], ""targetDays"": 60, ""unit"": ""mins"", ""timeOfDay"": ""18:30"" },
  { ""title"": ""Hydration"", ""description"": ""Drink enough water"", ""icon"": ""water_drop"", ""repeatType"": 0, ""targetDays"": 2000, ""unit"": ""ml"", ""timeOfDay"": ""08:00"" },
  { ""title"": ""Reading"", ""description"": ""Read a book"", ""icon"": ""book"", ""repeatType"": 0, ""targetDays"": 20, ""unit"": ""pages"", ""timeOfDay"": ""22:00"" }
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
                    content = "You are an empathetic Habit Teammate. You are summarizing a user's progress for the past month. Your goal is to provide a warm, supportive, and concise insight (2-3 sentences). Highlight one small win (e.g., morning routine consistency or a great streak) and remind them that slip-ups are just part of the journey. Do NOT mention specific numbers. Focus on the 'identity' they are building and patterns (like how well they do with morning habits vs evening habits, if scheduled times are provided). Tone: Supportive teammate, not a teacher."
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

    private static string DefaultNotification() => "You still have habits to complete today. Open Flow and knock them out!";
}
