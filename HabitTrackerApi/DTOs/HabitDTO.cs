using System;

namespace HabitTrackerApi.DTOs;

public class HabitDto
{
    public Guid Id {get;set;}
    public string Title {get;set;}
    public int TargetDays {get;set;}
    public int CompletedDays {get;set;}
}