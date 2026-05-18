using System.ComponentModel.DataAnnotations;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.DTOs;

public class CreateHabitDto
{
    [Required(ErrorMessage = "Title is required.")]
    [MaxLength(30, ErrorMessage = "Title must be 30 characters or fewer.")]
    public string Title { get; set; }    
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public int TargetDays { get; set; }
    public string? Unit { get; set; }
    public string? TimeOfDay { get; set; }
    public HabitRepeatType RepeatType { get; set; } = HabitRepeatType.EveryDay;
    public List<DayOfWeek>? RepeatDaysOfWeek { get; set; }
    public List<int>? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
    public bool IsPublic { get; set; } = true;
}