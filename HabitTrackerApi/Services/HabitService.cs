using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HabitTrackerApi.Data;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Entities;
using HabitTrackerApi.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace HabitTrackerApi.Services;

public class HabitService
{
    private readonly IHabitRepository _repo;
    private readonly AppDbContext _context;
    

    public HabitService(IHabitRepository repo, AppDbContext context)
    {
        _repo = repo;
        _context = context;
    }

    public async Task<List<HabitDto>> GetUserHabits(string userId)
    {
        var habits = await _repo.GetUserHabitsAsync(userId);

        return habits.Select(h => new HabitDto
        {
            Id = h.Id,
            Title = h.Title,
            TargetDays = h.TargetDays,
            CompletedDays = h.Logs.Count(l => l.IsCompleted)
        }).ToList();
    }

    public async Task CreateHabit(string userId, CreateHabitDto dto)
    {
        var habit = new Habit
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            TargetDays = dto.TargetDays,
            UserId = userId
        };

        await _repo.Add(habit);
        await _repo.Save();

    }

    public async Task<bool> DeleteHabit(Guid id, string userId)
    {
        var habit = await _context.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
        
        if (habit == null)
            return false;

        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();

        return true;
    }


    //HabitLogs

    public async Task ToggleTodayHabit(Guid habitId, string userId)
    {
        var today = DateTime.UtcNow.Date;

        var habit = await _context.Habits
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
            throw new Exception("Habit not found");

        var log = await _context.HabitLogs
            .FirstOrDefaultAsync(l => l.HabitId == habitId && l.Date == today);

        if (log == null)
        {
            log = new HabitLog
            {
                Id = Guid.NewGuid(),
                HabitId = habitId,
                Date = today,
                IsCompleted = true
            };

            _context.HabitLogs.Add(log);
        }
        else
        {
            log.IsCompleted = !log.IsCompleted;
        }

        await _context.SaveChangesAsync();


    }


    public async Task<int> GetStreak(Guid habitId, string userId)
    {
        var logs = await _context.HabitLogs
            .Where(l => l.HabitId == habitId && l.IsCompleted)
            .OrderByDescending(l => l.Date)
            .ToListAsync();

        int streak = 0;
        var today = DateTime.UtcNow.Date;

        foreach (var log in logs)
        {
            if (log.Date == today)
            {
                streak++;
                today = today.AddDays(-1);
            }
                
            else if (log.Date == today.AddDays(-1))
            {
                streak++;
                today = today.AddDays(-1);
            }

            else
            {
                break;
            }
                
        }

        return streak;
    }


    public async Task<HabitStatsDto> GetHabitStats(Guid habitId, string userId)
    {
        var logs = await _context.HabitLogs
            .Where(l => l.HabitId == habitId)
            .OrderBy(l => l.Date)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;

        var completedLogs = logs.Where(l => l.IsCompleted).ToList();

        int totalDays = logs.Count;
        int completedDays = completedLogs.Count;

        double completionRate = totalDays == 0 ? 0 : (double)completedDays / totalDays * 100;

        //Current streak
        var currentStreak = await GetStreak(habitId, userId);

        //Longest streak
        var longestStreak = CalculateLongestStreak(completedLogs);

        //Today
        bool isCompletedToday = logs.Any(l => l.Date == today && l.IsCompleted);

        return new HabitStatsDto
        {
            TotalDaysTracked = totalDays,
            CompletedDays = completedDays,
            CompletionRate = Math.Round(completionRate, 2),
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak,
            IsCompletedToday = isCompletedToday
        };
    }



    private int CalculateLongestStreak(List<HabitLog> logs)
    {
        int longest = 0;
        int current = 0;

        var ordered = logs
            .OrderBy(x => x.Date)
            .ToList();

        DateTime? prevDate = null;

        foreach (var log in ordered)
        {
            if (prevDate == null)
            {
                current = 1;
            }
            else if (log.Date == prevDate.Value.AddDays(1))
            {
                current++;
            }
            else
            {
                current = 1;
            }

            if (current > longest)
                longest = current;

            prevDate = log.Date;
        }

        return longest;
    }
}