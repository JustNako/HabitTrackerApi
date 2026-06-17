namespace HabitTrackerApi.Services;

public sealed class AiGenerationResult
{
    public List<string> Insights { get; init; } = new();
    public bool IsPersonalized { get; init; }
    public string? FailureReason { get; init; }
}
