

public class HabitStatsDto
{
    public int TotalDaysTracked { get; set; }
    public int CompletedDays { get; set; }
    public double CompletionRate { get; set; }

    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    public bool IsCompletedToday { get; set; }

}