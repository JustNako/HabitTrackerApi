using System;
using System.Collections.Generic;

namespace HabitTrackerApi.DTOs;

public class MonthlyRecapDto
{
    public string MonthName { get; set; } = string.Empty;
    public int TotalCompletionRate { get; set; }
    public int CompletedDaysCount { get; set; }
    public List<HabitSummaryDto> HabitSummaries { get; set; } = new();
    public string AiInsight { get; set; } = string.Empty;

    /// <summary>Habit with the highest completion rate this month.</summary>
    public string? BestHabit { get; set; }

    /// <summary>Habit with the lowest completion rate this month.</summary>
    public string? WorstHabit { get; set; }

    /// <summary>Overall completion rate from the previous month (for delta comparison).</summary>
    public int? PreviousMonthRate { get; set; }
}

public class HabitSummaryDto
{
    public string Title { get; set; } = string.Empty;
    public int CompletionRate { get; set; }
    public int BestStreak { get; set; }
    public string? TimeOfDay { get; set; }
    public int TargetDays { get; set; }
    public string? Unit { get; set; }
}