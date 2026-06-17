using System.Text;
using System.Text.Json;
using HabitTrackerApi.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HabitTrackerApi.Services;

public class AiService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<AiService> _logger;
    private const string Model = "llama-3.3-70b-versatile";
    private const string GroqChatUrl = "https://api.groq.com/openai/v1/chat/completions";

    public AiService(HttpClient httpClient, IConfiguration config, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var key = config["Groq:ApiKey"];
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (_apiKey == null)
            _logger.LogWarning("Groq:ApiKey is not configured. AI features will use fallback responses.");
    }

    public async Task<AiGenerationResult> GenerateInsightsAsync(string prompt)
    {
        if (_apiKey == null)
        {
            return new AiGenerationResult
            {
                Insights = DefaultInsights(),
                IsPersonalized = false,
                FailureReason = "AI is not configured on the server (missing Groq API key)."
            };
        }

        try
        {
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
            var (contentString, groqError) = await PostChatAndGetContentAsync(requestBody, jsonOptions);
            if (groqError != null)
            {
                return new AiGenerationResult
                {
                    Insights = DefaultInsights(),
                    IsPersonalized = false,
                    FailureReason = groqError
                };
            }

            if (string.IsNullOrWhiteSpace(contentString))
            {
                return new AiGenerationResult
                {
                    Insights = DefaultInsights(),
                    IsPersonalized = false,
                    FailureReason = "AI returned an empty response."
                };
            }

            var insights = ParseJsonStringArray(contentString, jsonOptions);
            if (insights.Count == 0)
            {
                _logger.LogWarning("Failed to parse AI insights JSON. Raw: {Raw}", contentString);
                return new AiGenerationResult
                {
                    Insights = DefaultInsights(),
                    IsPersonalized = false,
                    FailureReason = "Could not parse AI response. Try again in a moment."
                };
            }

            return new AiGenerationResult
            {
                Insights = insights,
                IsPersonalized = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateInsightsAsync failed");
            return new AiGenerationResult
            {
                Insights = DefaultInsights(),
                IsPersonalized = false,
                FailureReason = "AI service error. Please try again later."
            };
        }
    }

    private async Task<(string? Content, string? Error)> PostChatAndGetContentAsync(object requestBody, JsonSerializerOptions jsonOptions)
    {
        int maxRetries = 2;
        int delayMs = 1000;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, GroqChatUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(responseString);
                    var text = document.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    return (text, null);
                }

                _logger.LogWarning("Groq API returned {StatusCode} (Attempt {Attempt}/{Max}): {Body}", response.StatusCode, attempt + 1, maxRetries + 1, responseString);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
                {
                    await Task.Delay(delayMs);
                    delayMs *= 2; // exponential backoff
                    continue;
                }

                var hint = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Invalid or expired Groq API key."
                    : $"Groq API error ({(int)response.StatusCode}).";
                return (null, hint);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Transient exception in Groq call (Attempt {Attempt}/{Max})", attempt + 1, maxRetries + 1);
                await Task.Delay(delayMs);
                delayMs *= 2;
            }
        }

        return (null, "AI service is currently busy. Please try again in a moment.");
    }

    private static List<string> ParseJsonStringArray(string raw, JsonSerializerOptions jsonOptions)
    {
        var cleanText = raw.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "")
            .Trim();

        var startIndex = cleanText.IndexOf('[');
        var endIndex = cleanText.LastIndexOf(']');
        if (startIndex != -1 && endIndex > startIndex)
            cleanText = cleanText.Substring(startIndex, endIndex - startIndex + 1);

        return JsonSerializer.Deserialize<List<string>>(cleanText, jsonOptions) ?? new List<string>();
    }

    public async Task<string> GenerateNotificationMessageAsync(string prompt)
    {
        if (_apiKey == null) return DefaultNotification();

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
        var (text, error) = await PostChatAndGetContentAsync(requestBody, jsonOptions);

        if (string.IsNullOrWhiteSpace(text)) return DefaultNotification();

        return text.Replace("\"", "").Trim();
    }

    public async Task<List<HabitTrackerApi.DTOs.AiHabitDto>> GenerateHabitRoutineAsync(string userPrompt)
    {
        if (_apiKey == null)
            throw new InvalidOperationException("AI habit generation is not configured on the server.");

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
        var (text, error) = await PostChatAndGetContentAsync(requestBody, jsonOptions);

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
        if (_apiKey == null)
            return "You did a great job this month! Keep pushing towards your goals.";

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
        var (text, error) = await PostChatAndGetContentAsync(requestBody, jsonOptions);

        return text?.Trim() ?? "Focus on consistency to unlock your true potential next month.";
    }

    public async Task<BehavioralAnalysisResult> GenerateBehavioralAnalysisAsync(string prompt, BehavioralAnalysisDto fallback)
    {
        if (_apiKey == null)
        {
            return new BehavioralAnalysisResult
            {
                Analysis = fallback,
                IsPersonalized = false,
                FailureReason = "AI is not configured on the server (missing Groq API key)."
            };
        }

        try
        {
            var requestBody = new
            {
                model = Model,
                response_format = new { type = "json_object" },
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = @"You are a behavioral insights agent. Your task is to analyze the user's habit completion logs and identify clear patterns of friction, and provide a constructive AI Prescription with concrete settings adjustments.
                        
                        CRITICAL FORMAT: You MUST respond with a JSON object matching this schema exactly:
                        {
                          ""rhythm"": {
                            ""completed"": int, // Percentage of completed logs (0-100)
                            ""partial"": int,    // Percentage of partial check-ins (0-100, visual mockup concept, make this up so that completed + partial + missed = 100)
                            ""missed"": int     // Percentage of missed logs (0-100)
                          },
                          ""frictionPatterns"": [
                            {
                              ""icon"": ""string"", // MUST be one of: ""moon"" (for evening slumps/late night issues), ""friction"" (for broken streaks/too high goals), ""alarm"" (for reminder time optimizations)
                              ""pattern"": ""string"" // An empathetic, interesting insight in English (1 sentence). Do not start with capital letters for habit titles unless they are proper nouns (e.g. 'book reading' instead of 'Book reading').
                            }
                          ],
                          ""aiPrescription"": {
                            ""text"": ""string"", // An empathetic, friendly, and supportive recommendation paragraph in English (2-3 sentences max). Recommend reducing page counts or rescheduling reminders.
                            ""actions"": [
                              {
                                ""habitId"": ""string"", // The ID of the habit (Guid) as a string
                                ""habitTitle"": ""string"", // The exact title of the habit
                                ""type"": ""string"", // MUST be one of: ""ReduceTarget"", ""IncreaseTarget"", ""Reschedule""
                                ""currentValue"": ""string"", // e.g. ""20 pages"" or ""08:00""
                                ""newValue"": ""string"", // e.g. ""10 pages"" or ""07:30""
                                ""actionText"": ""string"", // MUST be ""Reduce target"", ""Increase target"", or ""Reschedule""
                                ""payload"": {
                                  ""targetDays"": int, // REQUIRED for target actions — the NEW numeric daily goal (e.g. 10 for ""10 pages"", not the delta)
                                  ""timeOfDay"": ""string"" // REQUIRED for reschedule — ""HH:mm"" 24-hour format (e.g. ""07:30"")
                                }
                              }
                            ]
                          }
                        }

                        Provide exactly 3 friction patterns. Provide exactly 1 or 2 settings adjustment actions under aiPrescription.actions. Use habit IDs from the prompt so we can match and perform settings adaptation.
                        
                        CRITICAL RULES:
                        1. If the user has a perfect or very high completion rate (e.g., 90%-100%), praise their consistency in the `aiPrescription.text` (saying that everything is already going perfectly), but still suggest optional micro-adjustments or challenge adjustments in the actions (e.g. scaling up a target, like increasing target pages from 10 to 15, or fine-tuning schedules) to help them progress further. For scaling up, set type to ""IncreaseTarget"" and actionText to ""Increase target"".
                        2. Do not suggest reducing a habit's target if the current target is already very low (e.g. less than 5 pages, 10 minutes, or 1 unit) or if the habit has a high completion rate (e.g. above 70%). Never suggest reducing any target to less than 1. If no actions are suitable, do not return any actions in the actions list."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = 0.5,
                max_tokens = 800
            };

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var (contentString, groqError) = await PostChatAndGetContentAsync(requestBody, jsonOptions);
            if (groqError != null)
            {
                return new BehavioralAnalysisResult
                {
                    Analysis = fallback,
                    IsPersonalized = false,
                    FailureReason = groqError
                };
            }

            if (string.IsNullOrWhiteSpace(contentString))
            {
                return new BehavioralAnalysisResult
                {
                    Analysis = fallback,
                    IsPersonalized = false,
                    FailureReason = "AI returned an empty response."
                };
            }

            // Parse JSON object
            var cleanText = contentString.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                                         .Replace("```", "")
                                         .Trim();
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var analysis = JsonSerializer.Deserialize<BehavioralAnalysisDto>(cleanText, options);
            if (analysis == null || analysis.FrictionPatterns.Count == 0)
            {
                return new BehavioralAnalysisResult
                {
                    Analysis = fallback,
                    IsPersonalized = false,
                    FailureReason = "Could not parse AI response or response was empty."
                };
            }

            return new BehavioralAnalysisResult
            {
                Analysis = analysis,
                IsPersonalized = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateBehavioralAnalysisAsync failed");
            return new BehavioralAnalysisResult
            {
                Analysis = fallback,
                IsPersonalized = false,
                FailureReason = "AI service error. Please try again later."
            };
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
