using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApi.Common;
using HabitTrackerApi.Data;
using HabitTrackerApi.DTOs.Sync;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.Services;

public class SyncService
{
    private readonly AppDbContext _context;
    private readonly AchievementEvaluatorService _achievements;

    public SyncService(AppDbContext context, AchievementEvaluatorService achievements)
    {
        _context = context;
        _achievements = achievements;
    }

    public async Task<Result<SyncPullResponse>> PullAsync(string userId, SyncPullRequest request)
    {
        var queryHabits = _context.Habits.IgnoreQueryFilters().Where(h => h.UserId == userId);
        var queryLogs = _context.HabitLogs.IgnoreQueryFilters().Where(l => l.Habit.UserId == userId);

        if (request.LastSyncTime.HasValue)
        {
            queryHabits = queryHabits.Where(h => h.UpdatedAt > request.LastSyncTime.Value);
            queryLogs = queryLogs.Where(l => l.UpdatedAt > request.LastSyncTime.Value);
        }

        var habits = await queryHabits.ToListAsync();
        var logs = await queryLogs.ToListAsync();

        var response = new SyncPullResponse
        {
            ServerSyncTime = DateTime.UtcNow,
            Habits = habits.Select(h => new SyncHabitDto
            {
                Id = h.Id,
                Title = h.Title,
                Description = h.Description,
                Notes = h.Notes,
                CreatedAt = h.CreatedAt,
                TargetDays = h.TargetDays,
                RepeatType = h.RepeatType,
                RepeatDaysOfWeek = h.RepeatDaysOfWeek,
                RepeatDaysOfMonth = h.RepeatDaysOfMonth,
                RepeatTimesPerPeriod = h.RepeatTimesPerPeriod,
                RepeatPeriod = h.RepeatPeriod,
                UpdatedAt = h.UpdatedAt,
                IsDeleted = h.IsDeleted,
                IsPublic = h.IsPublic,
                Unit = h.Unit,
                TimeOfDay = h.TimeOfDay
            }).ToList(),
            HabitLogs = logs.Select(l => new SyncHabitLogDto
            {
                Id = l.Id,
                HabitId = l.HabitId,
                Date = l.Date,
                IsCompleted = l.IsCompleted,
                Note = l.Note,
                UpdatedAt = l.UpdatedAt,
                IsDeleted = l.IsDeleted
            }).ToList()
        };

        return Result.Success(response);
    }

    public async Task<Result> PushAsync(string userId, SyncPushRequest request)
    {
        var habitIds = request.Habits.Select(h => h.Id).ToList();
        var existingHabits = await _context.Habits.IgnoreQueryFilters()
            .Where(h => h.UserId == userId && habitIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id);

        foreach (var syncHabit in request.Habits)
        {
            if (existingHabits.TryGetValue(syncHabit.Id, out var existingHabit))
            {
                if (syncHabit.UpdatedAt > existingHabit.UpdatedAt)
                {
                    existingHabit.Title = syncHabit.Title;
                    existingHabit.Description = syncHabit.Description;
                    existingHabit.Notes = syncHabit.Notes;
                    existingHabit.TargetDays = syncHabit.TargetDays;
                    existingHabit.RepeatType = syncHabit.RepeatType;
                    existingHabit.RepeatDaysOfWeek = syncHabit.RepeatDaysOfWeek;
                    existingHabit.RepeatDaysOfMonth = syncHabit.RepeatDaysOfMonth;
                    existingHabit.RepeatTimesPerPeriod = syncHabit.RepeatTimesPerPeriod;
                    existingHabit.RepeatPeriod = syncHabit.RepeatPeriod;
                    existingHabit.UpdatedAt = syncHabit.UpdatedAt;
                    existingHabit.IsDeleted = syncHabit.IsDeleted;
                    existingHabit.IsPublic = syncHabit.IsPublic;
                    existingHabit.Unit = syncHabit.Unit;
                    existingHabit.TimeOfDay = syncHabit.TimeOfDay;
                    _context.Entry(existingHabit).State = EntityState.Modified;
                }
            }
            else
            {
                var newHabit = new Habit
                {
                    Id = syncHabit.Id,
                    UserId = userId,
                    Title = syncHabit.Title,
                    Description = syncHabit.Description,
                    Notes = syncHabit.Notes,
                    CreatedAt = syncHabit.CreatedAt,
                    TargetDays = syncHabit.TargetDays,
                    RepeatType = syncHabit.RepeatType,
                    RepeatDaysOfWeek = syncHabit.RepeatDaysOfWeek,
                    RepeatDaysOfMonth = syncHabit.RepeatDaysOfMonth,
                    RepeatTimesPerPeriod = syncHabit.RepeatTimesPerPeriod,
                    RepeatPeriod = syncHabit.RepeatPeriod,
                    UpdatedAt = syncHabit.UpdatedAt,
                    IsDeleted = syncHabit.IsDeleted,
                    IsPublic = syncHabit.IsPublic,
                    Unit = syncHabit.Unit,
                    TimeOfDay = syncHabit.TimeOfDay
                };
                _context.Habits.Add(newHabit);
            }
        }

        var logIds = request.HabitLogs.Select(l => l.Id).ToList();
        var existingLogs = await _context.HabitLogs.IgnoreQueryFilters()
            .Where(l => l.Habit.UserId == userId && logIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);

        var habitIdsInRequest = request.HabitLogs.Select(l => l.HabitId).Distinct().ToList();
        var habitsInDb = await _context.Habits.IgnoreQueryFilters()
            .Where(h => h.UserId == userId && habitIdsInRequest.Contains(h.Id))
            .Select(h => h.Id)
            .ToListAsync();
        var habitIdSet = new HashSet<Guid>(habitsInDb);
        var newHabitIdsInRequest = new HashSet<Guid>(request.Habits.Select(h => h.Id));

        foreach (var syncLog in request.HabitLogs)
        {
            if (existingLogs.TryGetValue(syncLog.Id, out var existingLog))
            {
                if (syncLog.UpdatedAt > existingLog.UpdatedAt)
                {
                    existingLog.Date = syncLog.Date;
                    existingLog.IsCompleted = syncLog.IsCompleted;
                    existingLog.Note = syncLog.Note;
                    existingLog.UpdatedAt = syncLog.UpdatedAt;
                    existingLog.IsDeleted = syncLog.IsDeleted;
                    _context.Entry(existingLog).State = EntityState.Modified;
                }
            }
            else
            {
                bool habitExists = habitIdSet.Contains(syncLog.HabitId);
                bool newHabitIncluded = newHabitIdsInRequest.Contains(syncLog.HabitId);

                if (habitExists || newHabitIncluded)
                {
                    var newLog = new HabitLog
                    {
                        Id = syncLog.Id,
                        HabitId = syncLog.HabitId,
                        Date = syncLog.Date,
                        IsCompleted = syncLog.IsCompleted,
                        Note = syncLog.Note,
                        UpdatedAt = syncLog.UpdatedAt,
                        IsDeleted = syncLog.IsDeleted
                    };
                    _context.HabitLogs.Add(newLog);
                }
            }
        }

        await _context.SaveChangesAsync();

        // Evaluate achievements after sync
        await _achievements.EvaluateUserAsync(userId);

        return Result.Success();
    }
}
