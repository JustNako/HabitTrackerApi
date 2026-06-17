using HabitTrackerApi.DTOs;

namespace HabitTrackerApi.Services;

public sealed class BehavioralAnalysisResult
{
    public BehavioralAnalysisDto Analysis { get; init; } = new();
    public bool IsPersonalized { get; init; }
    public string? FailureReason { get; init; }
}
