using System.Collections.Generic;
using System;

namespace HabitTrackerApi.Entities;

public class Habit
{
    public Guid Id { get; set; }
    public string Title {get;set;}
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TargetDays { get; set; }
    public HabitRepeatType RepeatType { get; set; } = HabitRepeatType.EveryDay;
    public string? RepeatDaysOfWeek { get; set; }
    public string? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
    public string UserId { get; set; }
    public ApplicationUser User { get; set; }
    public List<HabitLog> Logs { get; set; }
}
