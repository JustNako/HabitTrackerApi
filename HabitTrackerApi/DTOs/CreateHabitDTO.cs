using HabitTrackerApi.Entities;

namespace HabitTrackerApi.DTOs;

public class CreateHabitDto
{
    public string Title { get; set; }
    public string? Description { get; set; }
    public int TargetDays { get; set; }
    public HabitRepeatType RepeatType { get; set; } = HabitRepeatType.EveryDay;
    public List<DayOfWeek>? RepeatDaysOfWeek { get; set; }
    public List<int>? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
}