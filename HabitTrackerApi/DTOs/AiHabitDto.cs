namespace HabitTrackerApi.DTOs;

public class AiHabitDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "star";
    
    // Enum match with frontend: 0=everyDay, 1=specificDaysOfWeek, 2=specificDaysOfMonth, 3=xTimesPerPeriod
    public int RepeatType { get; set; } = 0; 
    
    // DayOfWeek values (0=Sun, 1=Mon, ..., 6=Sat)
    public List<int>? RepeatDaysOfWeek { get; set; }
    
    // For xTimesPerPeriod
    public int? RepeatTimesPerPeriod { get; set; }
    
    public int TargetDays { get; set; } = 1;
    public string? Unit { get; set; }
    public string? TimeOfDay { get; set; }
}
