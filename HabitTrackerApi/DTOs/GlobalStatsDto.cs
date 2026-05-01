namespace HabitTrackerApi.DTOs;

public class MonthlyProgressDto
{
    public string Month { get; set; } = string.Empty;
    public int Completions { get; set; }
}

public class WeeklyProgressDto
{
    public string Week { get; set; } = string.Empty;
    public double Rate { get; set; }
}

public class GlobalStatsDto
{
    public int TotalHabits { get; set; }
    public int GlobalStreak { get; set; }
    public int TotalCompletions { get; set; }
    public string BestHabitName { get; set; } = "None";
    public int BestHabitCompletions { get; set; }
    public double CompletionRate { get; set; }
    public int BestStreak { get; set; }
    public int ActiveDays { get; set; }
    public bool IsPro { get; set; }

    public List<MonthlyProgressDto> MonthlyProgress { get; set; } = new();
    public List<WeeklyProgressDto> WeeklyCompletionRate { get; set; } = new();
}
