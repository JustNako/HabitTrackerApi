using System.Collections.Generic;
using System;

namespace HabitTrackerApi.Entities;

public class Habit
{
    public Guid Id { get; set; }

    
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TargetDays { get; set; }
    public HabitRepeatType RepeatType { get; set; } = HabitRepeatType.EveryDay;
    public string? RepeatDaysOfWeek { get; set; }
    public string? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public List<HabitLog> Logs { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public bool IsPublic { get; set; } = true;
}
