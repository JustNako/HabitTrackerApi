using System;
using System.Collections.Generic;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.DTOs.Sync;

public class SyncPullRequest
{
    public DateTime? LastSyncTime { get; set; }
}

public class SyncPullResponse
{
    public List<SyncHabitDto> Habits { get; set; } = new();
    public List<SyncHabitLogDto> HabitLogs { get; set; } = new();
    public DateTime ServerSyncTime { get; set; } = DateTime.UtcNow;
}

public class SyncPushRequest
{
    public List<SyncHabitDto> Habits { get; set; } = new();
    public List<SyncHabitLogDto> HabitLogs { get; set; } = new();
}

public class SyncHabitDto
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TargetDays { get; set; }
    public string? Unit { get; set; }
    public string? TimeOfDay { get; set; }
    public HabitRepeatType RepeatType { get; set; }
    public string? RepeatDaysOfWeek { get; set; }
    public string? RepeatDaysOfMonth { get; set; }
    public int? RepeatTimesPerPeriod { get; set; }
    public HabitRepeatPeriod? RepeatPeriod { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPublic { get; set; }
}

public class SyncHabitLogDto
{
    public Guid Id { get; set; }
    public Guid HabitId { get; set; }
    public DateTime Date { get; set; }
    public bool IsCompleted { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
