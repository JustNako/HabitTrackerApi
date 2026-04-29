using System;

namespace HabitTrackerApi.Entities;

public class HabitLog
{
    public Guid Id { get; set; }
    public Guid HabitId {get;set;}
    public Habit Habit {get;set;}
    
    public DateTime Date {get;set;}
    public bool IsCompleted {get;set;}
    public string? Note {get;set;}

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
}
