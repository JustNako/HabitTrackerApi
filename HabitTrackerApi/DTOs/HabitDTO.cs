using System;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.DTOs;

public class HabitDto
{
    public Guid Id {get;set;}
    public string Title {get;set;}
    public int TargetDays {get;set;}
    public int CompletedDays {get;set;}
    public HabitRepeatType RepeatType { get; set; }
    public List<DayOfWeek>? RepeatDaysOfWeek { get; set; }
    public List<int>? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
}