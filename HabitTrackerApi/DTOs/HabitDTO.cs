using System;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.DTOs;

public class HabitDto
{
    public Guid Id {get;set;}
    public string Title {get;set;}
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public int TargetDays {get;set;}
    public string? Unit { get; set; }
    public string? TimeOfDay { get; set; }
    public int CompletedDays {get;set;}
    public HabitRepeatType RepeatType { get; set; }
    public List<DayOfWeek>? RepeatDaysOfWeek { get; set; }
    public List<int>? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
    public bool IsCompletedToday { get; set; }
    public bool IsPublic { get; set; }
    public int CurrentStreak { get; set; }
    public int CompletionRate30Day { get; set; }
}