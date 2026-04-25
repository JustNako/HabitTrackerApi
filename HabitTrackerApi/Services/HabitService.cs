using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HabitTrackerApi.Data;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Entities;
using HabitTrackerApi.Repositories;
using Microsoft.EntityFrameworkCore;

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
            CompletedDays = h.Logs.Count(l => l.IsCompleted),
            RepeatType = h.RepeatType,
            RepeatDaysOfWeek = ParseDayOfWeekCsv(h.RepeatDaysOfWeek),
            RepeatDaysOfMonth = ParseIntCsv(h.RepeatDaysOfMonth),
            RepeatTimesPerPeriod = h.RepeatTimesPerPeriod,
            RepeatPeriod = h.RepeatPeriod
        }).ToList();
    }

    public async Task CreateHabit(string userId, CreateHabitDto dto)
    {
        ValidateRepeatSettings(dto);

        var habit = new Habit
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            TargetDays = dto.TargetDays,
            RepeatType = dto.RepeatType,
            RepeatDaysOfWeek = ToDayOfWeekCsv(dto.RepeatDaysOfWeek),
            RepeatDaysOfMonth = ToIntCsv(dto.RepeatDaysOfMonth),
            RepeatTimesPerPeriod = dto.RepeatTimesPerPeriod,
            RepeatPeriod = dto.RepeatPeriod,
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

    public async Task<bool> ToggleHabit(string userId, Guid habitId, string? note)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return false;

        var today = DateTime.UtcNow.Date;

        var log = await _context.HabitLogs
            .FirstOrDefaultAsync(x => x.HabitId == habitId && x.Date == today);

        if (log == null)
        {
            log = new HabitLog
            {
                Id = Guid.NewGuid(),
                HabitId = habitId,
                Date = today,
                IsCompleted = true,
                Note = note
            };

            _context.HabitLogs.Add(log);
        }
        else
        {
            log.IsCompleted = !log.IsCompleted;

            // 🔥 обновляем note
            if (!string.IsNullOrWhiteSpace(note))
                log.Note = note;
        }

        await _context.SaveChangesAsync();
        return true;
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


    public async Task<List<HabitHeatmapDto>> GetHeatmap(Guid habitId, string userId)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-30);
        var endDate = DateTime.UtcNow.Date;

        var logs = await _context.HabitLogs
            .Where(l => l.HabitId == habitId && l.Date >= startDate && l.Date <= endDate)
            .ToListAsync();

        var result = new List<HabitHeatmapDto>();

        for (var date = startDate; date  <= endDate; date = date.AddDays(1))
        {
            var log = logs.FirstOrDefault(l => l.Date == date);

            result.Add(new HabitHeatmapDto
            {
                Date = date,
                Value = log != null && log.IsCompleted ? 1 : 0
            });
        }

        return result;
    }

    private static void ValidateRepeatSettings(CreateHabitDto dto)
    {
        switch (dto.RepeatType)
        {
            case HabitRepeatType.EveryDay:
                break;


            case HabitRepeatType.SpecificDaysOfWeek:
                if (dto.RepeatDaysOfWeek == null || dto.RepeatDaysOfWeek.Count == 0)
                    throw new ArgumentException("RepeatDaysOfWeek is required.");

                if (dto.RepeatDaysOfWeek.Count > 7)
                    throw new ArgumentException("Cannot select more than 7 days of week.");

                if (dto.RepeatDaysOfWeek.Distinct().Count() != dto.RepeatDaysOfWeek.Count)
                    throw new ArgumentException("Duplicate days of week are not allowed.");

                break;


            case HabitRepeatType.SpecificDaysOfMonth:
                if (dto.RepeatDaysOfMonth == null || dto.RepeatDaysOfMonth.Count == 0)
                    throw new ArgumentException("RepeatDaysOfMonth is required.");

                if (dto.RepeatDaysOfMonth.Any(day => day < 1 || day > 31))
                    throw new ArgumentException("Days of month must be between 1 and 31.");

                ValidateDaysInMonth(dto.RepeatDaysOfMonth);

                if (dto.RepeatDaysOfMonth.Distinct().Count() != dto.RepeatDaysOfMonth.Count)
                    throw new ArgumentException("Duplicate days of month are not allowed.");

                break;



            case HabitRepeatType.XTimesPerPeriod:

                if (dto.RepeatTimesPerPeriod is null || dto.RepeatTimesPerPeriod <= 0)
                    throw new ArgumentException("RepeatTimesPerPeriod must be > 0.");

                if (dto.RepeatPeriod is null)
                    throw new ArgumentException("RepeatPeriod is required.");

                if (dto.RepeatPeriod == HabitRepeatPeriod.Week)
                {
                    if (dto.RepeatTimesPerPeriod > 7 || dto.RepeatTimesPerPeriod < 1)
                        throw new ArgumentException("Cannot be more than 7 times per week.");
                }

                if (dto.RepeatPeriod == HabitRepeatPeriod.Month)
                {
                    if (dto.RepeatTimesPerPeriod > 31 || dto.RepeatTimesPerPeriod < 1)
                        throw new ArgumentException("Cannot be more than 31 times per month.");
                }

                break;


            default:
                throw new ArgumentException("Unsupported RepeatType.");
        }
    }


    private static void ValidateDaysInMonth(List<int> days)
    {
        var currentYear = DateTime.UtcNow.Year;
        var currentMonth = DateTime.UtcNow.Month;

        int daysInMonth = DateTime.DaysInMonth(currentYear, currentMonth);

        if (days.Any(d => d > daysInMonth))
            throw new ArgumentException($"This month has only {daysInMonth} days.");
    }
    private static string? ToDayOfWeekCsv(IEnumerable<DayOfWeek>? values)
    {
        if (values == null)
            return null;

        var items = values
            .Distinct()
            .OrderBy(v => (int)v)
            .Select(v => ((int)v).ToString());

        var result = string.Join(",", items);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static List<DayOfWeek>? ParseDayOfWeekCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .Select(v => (DayOfWeek)v)
            .ToList();
    }

    private static string? ToIntCsv(IEnumerable<int>? values)
    {
        if (values == null)
            return null;

        var items = values
            .Distinct()
            .OrderBy(v => v)
            .Select(v => v.ToString());

        var result = string.Join(",", items);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static List<int>? ParseIntCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToList();
    }
}