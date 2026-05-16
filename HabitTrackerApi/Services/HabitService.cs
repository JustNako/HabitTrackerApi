using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HabitTrackerApi.Common;
using HabitTrackerApi.Data;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Entities;
using HabitTrackerApi.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApi.Services;

public class HabitService
{
    private readonly IHabitRepository _repo;
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AiService _aiService;
    private readonly AchievementEvaluatorService _achievements;

    public HabitService(IHabitRepository repo, AppDbContext context, UserManager<ApplicationUser> userManager, AiService aiService, AchievementEvaluatorService achievements)
    {
        _repo = repo;
        _context = context;
        _userManager = userManager;
        _aiService = aiService;
        _achievements = achievements;
    }

    public async Task<Result<List<HabitDto>>> GetUserHabits(string userId)
    {
        var habits = await _repo.GetUserHabitsAsync(userId);

        var dtos = habits.Select(h => new HabitDto
        {
            Id = h.Id,
            Title = h.Title,
            Description = h.Description,
            Notes = h.Notes,
            TargetDays = h.TargetDays,
            CompletedDays = h.Logs.Count(l => l.IsCompleted),
            RepeatType = h.RepeatType,
            RepeatDaysOfWeek = ParseDayOfWeekCsv(h.RepeatDaysOfWeek),
            RepeatDaysOfMonth = ParseIntCsv(h.RepeatDaysOfMonth),
            RepeatTimesPerPeriod = h.RepeatTimesPerPeriod,
            RepeatPeriod = h.RepeatPeriod,
            IsCompletedToday = h.Logs.Any(l => l.IsCompleted && l.Date.Date == DateTime.UtcNow.Date),
            IsPublic = h.IsPublic,
            Unit = h.Unit
        }).ToList();

        return Result.Success(dtos);
    }

    private const int FreeHabitLimit = 10;

    public async Task<Result> CreateHabit(string userId, CreateHabitDto dto)
    {
        var user = await SyncProStatusAndGetUser(userId);
        var validationResult = ValidateRepeatSettings(dto);
        if (validationResult.IsFailure)
            return validationResult;

        // ── Pro tier check ─────────────────────────────────────────────────
        bool isCurrentlyPro = user != null && user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

        if (!isCurrentlyPro)
        {
            var existingCount = await _context.Habits.CountAsync(h => h.UserId == userId && !h.IsDeleted);
            if (existingCount >= FreeHabitLimit)
                return Result.Failure(new Error("ProRequired", $"Free plan is limited to {FreeHabitLimit} habits. Upgrade to Pro to add more."));
        }

        var habit = new Habit
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            Notes = dto.Notes,
            TargetDays = dto.TargetDays,
            RepeatType = dto.RepeatType,
            RepeatDaysOfWeek = ToDayOfWeekCsv(dto.RepeatDaysOfWeek),
            RepeatDaysOfMonth = ToIntCsv(dto.RepeatDaysOfMonth),
            RepeatTimesPerPeriod = dto.RepeatTimesPerPeriod,
            RepeatPeriod = dto.RepeatPeriod,
            IsPublic = dto.IsPublic,
            UserId = userId,
            Unit = dto.Unit
        };

        await _repo.Add(habit);
        await _repo.Save();

        // Evaluate achievements
        await _achievements.EvaluateUserAsync(userId);

        return Result.Success();
    }

    public async Task<Result> UpdateHabitNotes(Guid id, string userId, string? notes)
    {
        var habit = await _context.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
        if (habit == null)
            return Result.Failure(Error.NotFound);

        habit.Notes = notes;
        habit.UpdatedAt = DateTime.UtcNow;

        _context.Entry(habit).State = EntityState.Modified;
        await _repo.Save();

        return Result.Success();
    }

    public async Task<Result> DeleteHabit(Guid id, string userId)
    {
        var habit = await _context.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
        
        if (habit == null)
            return Result.Failure(Error.NotFound);

        habit.IsDeleted = true;
        _context.Entry(habit).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return Result.Success();
    }


    //HabitLogs

    public async Task<Result> ToggleVisibility(string userId, Guid habitId)
    {
        var habit = await _context.Habits.FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);
        if (habit == null)
            return Result.Failure(Error.NotFound);

        habit.IsPublic = !habit.IsPublic;
        habit.UpdatedAt = DateTime.UtcNow;
        _context.Entry(habit).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> ToggleHabit(string userId, Guid habitId, string? note)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure(Error.NotFound);

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

        // Evaluate achievements
        await _achievements.EvaluateUserAsync(userId);

        return Result.Success();
    }


    public async Task<Result<int>> GetStreak(Guid habitId, string userId)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure<int>(Error.NotFound);

        var logsDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.HabitId == habitId && l.IsCompleted)
            .OrderByDescending(l => l.Date)
            .Select(l => l.Date)
            .ToListAsync();

        int streak = 0;
        var expected = DateTime.UtcNow.Date;

        foreach (var logDate in logsDates)
        {
            if (logDate == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (logDate < expected)
            {
                // Пропуск в серии — стрик прерван
                break;
            }
            // logDate > expected — дубликат за тот же день, пропускаем
        }

        return Result.Success(streak);
    }

    public async Task<Result<int>> GetGlobalStreak(string userId)
    {
        var completedDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.Habit.UserId == userId && l.IsCompleted)
            .Select(l => l.Date.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        int streak = 0;
        var expected = DateTime.UtcNow.Date;

        foreach (var date in completedDates)
        {
            if (date == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (date == expected.AddDays(-1) && streak == 0)
            {
                streak++;
                expected = expected.AddDays(-2);
            }
            else if (date < expected)
            {
                break;
            }
        }

        return Result.Success(streak);
    }

    public async Task<Result<GlobalStatsDto>> GetGlobalStats(string userId)
    {
        var user = await SyncProStatusAndGetUser(userId);
        
        int totalHabits = await _context.Habits.CountAsync(h => h.UserId == userId);
        int totalCompletions = await _context.HabitLogs.CountAsync(l => l.Habit.UserId == userId && l.IsCompleted);
        int globalStreak = (await GetGlobalStreak(userId)).Value;

        // Fetch minimal data for calculations
        var completedDatesByHabit = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.Habit.UserId == userId && l.IsCompleted)
            .Select(l => new { l.HabitId, l.Date })
            .ToListAsync();

        // Completion Rate (simple % of completions per habit)
        double completionRate = totalHabits == 0 ? 0 : Math.Round((double)totalCompletions / totalHabits, 2);

        // Active Days – distinct dates with any completed habit
        int activeDays = completedDatesByHabit.Select(l => l.Date.Date).Distinct().Count();

        // Best Streak – longest streak among all habits
        int bestStreak = 0;
        var groupedByHabit = completedDatesByHabit.GroupBy(l => l.HabitId);
        foreach (var group in groupedByHabit)
        {
            var datesOnly = group.Select(x => new HabitLog { Date = x.Date }).ToList();
            var longest = CalculateLongestStreak(datesOnly);
            if (longest > bestStreak) bestStreak = longest;
        }

        var bestHabitGroup = groupedByHabit
                                 .OrderByDescending(g => g.Count())
                                 .FirstOrDefault();

        string bestHabitName = "None";
        int bestHabitCompletions = 0;

        if (bestHabitGroup != null)
        {
            bestHabitCompletions = bestHabitGroup.Count();
            bestHabitName = await _context.Habits
                .AsNoTracking()
                .Where(h => h.Id == bestHabitGroup.Key)
                .Select(h => h.Title)
                .FirstOrDefaultAsync() ?? "None";
        }

        // Monthly Progress (Last 6 months)
        var monthlyProgress = new List<MonthlyProgressDto>();
        for (int i = 5; i >= 0; i--)
        {
            var targetMonth = DateTime.UtcNow.AddMonths(-i);
            var monthName = targetMonth.ToString("MMM");
            var count = completedDatesByHabit.Count(l => l.Date.Year == targetMonth.Year && l.Date.Month == targetMonth.Month);
            monthlyProgress.Add(new MonthlyProgressDto { Month = monthName, Completions = count });
        }

        // Weekly Completion Rate (Last 4 weeks)
        var weeklyCompletionRate = new List<WeeklyProgressDto>();
        var now = DateTime.UtcNow.Date;
        
        // Calculate the start of the current week (Monday)
        int daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentMonday = now.AddDays(-daysSinceMonday);

        for (int i = 3; i >= 0; i--)
        {
            var startOfWeek = currentMonday.AddDays(-(i * 7));
            var endOfWeek = startOfWeek.AddDays(6).AddHours(23).AddMinutes(59);
            
            var weeklyLogsCount = completedDatesByHabit.Count(l => l.Date >= startOfWeek && l.Date <= endOfWeek);
            
            // Total possible completions = habits count * 7 days
            int possibleCompletions = totalHabits * 7;
            
            double rate = possibleCompletions == 0 
                ? 0 
                : Math.Min(100, Math.Round((double)weeklyLogsCount / possibleCompletions * 100, 1));
            
            // Label like "22-28 May" or "W1"
            string weekLabel = $"{startOfWeek:ddMMM}"; 
            
            weeklyCompletionRate.Add(new WeeklyProgressDto { Week = weekLabel, Rate = rate });
        }

        bool isCurrentlyPro = user != null && user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

        // Calculate AI limits for the response
        int aiRemaining = 0;
        int aiTotalLimit = isCurrentlyPro ? 5 : 2;

        if (user != null)
        {
            if (isCurrentlyPro)
            {
                var today = DateTime.UtcNow.Date;
                int currentCount = user.AiGenerationsCount;
                if (user.LastAiGenerationDate?.Date != today)
                {
                    currentCount = 0; // It would be reset on next call, but let's show correctly now
                }
                aiRemaining = Math.Max(0, 5 - currentCount);
            }
            else
            {
                aiRemaining = Math.Max(0, 2 - user.AiGenerationsCount);
            }
        }

        return Result.Success(new GlobalStatsDto
        {
            TotalHabits = totalHabits,
            GlobalStreak = globalStreak,
            TotalCompletions = totalCompletions,
            BestHabitName = bestHabitName,
            BestHabitCompletions = bestHabitCompletions,
            CompletionRate = completionRate,
            BestStreak = bestStreak,
            ActiveDays = activeDays,
            MonthlyProgress = monthlyProgress,
            WeeklyCompletionRate = weeklyCompletionRate,
            IsPro = isCurrentlyPro,
            AiGenerationsRemaining = aiRemaining,
            AiTotalLimit = aiTotalLimit
        });
    }

    public async Task<Result<List<string>>> GetAiInsights(string userId)
    {
        var user = await SyncProStatusAndGetUser(userId);
        bool isCurrentlyPro = user != null && user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

        if (!isCurrentlyPro)
        {
            return Result.Failure<List<string>>(new Error("ProRequired", "AI Insights are available to Pro users only."));
        }

        var startDate = DateTime.UtcNow.Date.AddDays(-7);
        
        var habits = await _context.Habits
            .AsNoTracking()
            .Include(h => h.Logs.Where(l => l.Date >= startDate))
            .Where(h => h.UserId == userId && !h.IsDeleted)
            .ToListAsync();

        if (!habits.Any())
        {
            return Result.Failure<List<string>>(new Error("NoData", "Not enough habit data to generate insights."));
        }

        var totalHabits = habits.Count;
        var totalCompleted = habits.Sum(h => h.Logs.Count(l => l.IsCompleted));
        var totalPossible = totalHabits * 7;
        var overallRate = totalPossible > 0 ? (int)Math.Round((double)totalCompleted / totalPossible * 100) : 0;

        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine($"The user has {totalHabits} active habits tracked over the last 7 days.");
        promptBuilder.AppendLine($"They completed {totalCompleted} check-ins out of {totalPossible} possible check-ins (7 days × {totalHabits} habits), giving an overall weekly completion rate of {overallRate}%.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Breakdown per habit (each habit can be completed up to 7 times this week):");

        foreach (var habit in habits)
        {
            var logsLast7Days = habit.Logs.Where(l => l.Date >= startDate).OrderBy(l => l.Date).ToList();
            var completedDays = logsLast7Days.Count(l => l.IsCompleted);
            
            // Create a string pattern like [✅, ❌, ✅, ✅, ❌, ❌, ✅]
            var patternParts = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                var d = startDate.AddDays(i);
                var isDone = logsLast7Days.Any(l => l.Date.Date == d.Date && l.IsCompleted);
                patternParts.Add(isDone ? "✅" : "❌");
            }
            var patternString = string.Join("", patternParts);

            var rate = (int)Math.Round((double)completedDays / 7 * 100);
            var ageInDays = (int)(DateTime.UtcNow - habit.CreatedAt).TotalDays;
            var repeatInfo = habit.RepeatType switch
            {
                HabitRepeatType.EveryDay => "daily",
                _ => "custom schedule"
            };

            promptBuilder.AppendLine($"- Habit: \"{habit.Title}\" ({repeatInfo})");
            promptBuilder.AppendLine($"  7-day pattern: {patternString} ({completedDays}/7 days)");
            if (ageInDays < 14) promptBuilder.AppendLine("  Note: This is a NEW habit, fragile phase.");
        }

        var insights = await _aiService.GenerateInsightsAsync(promptBuilder.ToString());
        return Result.Success(insights);
    }

    public async Task<Result<HabitStatsDto>> GetHabitStats(Guid habitId, string userId)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure<HabitStatsDto>(Error.NotFound);

        int totalDays = await _context.HabitLogs.CountAsync(l => l.HabitId == habitId);
        int completedDays = await _context.HabitLogs.CountAsync(l => l.HabitId == habitId && l.IsCompleted);

        var completedDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.HabitId == habitId && l.IsCompleted)
            .Select(l => l.Date)
            .OrderBy(d => d)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;

        double completionRate = totalDays == 0 ? 0 : (double)completedDays / totalDays * 100;

        //Current streak
        var currentStreak = await GetStreak(habitId, userId);

        //Longest streak
        var logsForStreak = completedDates.Select(d => new HabitLog { Date = d }).ToList();
        var longestStreak = CalculateLongestStreak(logsForStreak);

        //Today
        bool isCompletedToday = completedDates.Any(d => d == today);

        return Result.Success(new HabitStatsDto
        {
            TotalDaysTracked = totalDays,
            CompletedDays = completedDays,
            CompletionRate = Math.Round(completionRate, 2),
            CurrentStreak = currentStreak.IsSuccess ? currentStreak.Value : 0,
            LongestStreak = longestStreak,
            IsCompletedToday = isCompletedToday
        });
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


    public async Task<Result<List<HabitHeatmapDto>>> GetHeatmap(Guid habitId, string userId)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure<List<HabitHeatmapDto>>(Error.NotFound);

        var startDate = DateTime.UtcNow.Date.AddDays(-30);
        var endDate = DateTime.UtcNow.Date;

        var completedDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.HabitId == habitId && l.Date >= startDate && l.Date <= endDate && l.IsCompleted)
            .Select(l => l.Date)
            .ToListAsync();

        var result = new List<HabitHeatmapDto>();

        for (var date = startDate; date  <= endDate; date = date.AddDays(1))
        {
            result.Add(new HabitHeatmapDto
            {
                Date = date,
                Value = completedDates.Contains(date) ? 1 : 0
            });
        }

        return Result.Success(result);
    }

    private static Result ValidateRepeatSettings(CreateHabitDto dto)
    {
        switch (dto.RepeatType)
        {
            case HabitRepeatType.EveryDay:
                break;


            case HabitRepeatType.SpecificDaysOfWeek:
                if (dto.RepeatDaysOfWeek == null || dto.RepeatDaysOfWeek.Count == 0)
                    return Result.Failure(new Error("ValidationError", "RepeatDaysOfWeek is required."));

                if (dto.RepeatDaysOfWeek.Count > 7)
                    return Result.Failure(new Error("ValidationError", "Cannot select more than 7 days of week."));

                if (dto.RepeatDaysOfWeek.Distinct().Count() != dto.RepeatDaysOfWeek.Count)
                    return Result.Failure(new Error("ValidationError", "Duplicate days of week are not allowed."));

                break;


            case HabitRepeatType.SpecificDaysOfMonth:
                if (dto.RepeatDaysOfMonth == null || dto.RepeatDaysOfMonth.Count == 0)
                    return Result.Failure(new Error("ValidationError", "RepeatDaysOfMonth is required."));

                if (dto.RepeatDaysOfMonth.Any(day => day < 1 || day > 31))
                    return Result.Failure(new Error("ValidationError", "Days of month must be between 1 and 31."));

                if (dto.RepeatDaysOfMonth.Distinct().Count() != dto.RepeatDaysOfMonth.Count)
                    return Result.Failure(new Error("ValidationError", "Duplicate days of month are not allowed."));

                break;



            case HabitRepeatType.XTimesPerPeriod:

                if (dto.RepeatTimesPerPeriod is null || dto.RepeatTimesPerPeriod <= 0)
                    return Result.Failure(new Error("ValidationError", "RepeatTimesPerPeriod must be > 0."));

                if (dto.RepeatPeriod is null)
                    return Result.Failure(new Error("ValidationError", "RepeatPeriod is required."));

                if (dto.RepeatPeriod == HabitRepeatPeriod.Week)
                {
                    if (dto.RepeatTimesPerPeriod > 7 || dto.RepeatTimesPerPeriod < 1)
                        return Result.Failure(new Error("ValidationError", "Cannot be more than 7 times per week."));
                }

                if (dto.RepeatPeriod == HabitRepeatPeriod.Month)
                {
                    if (dto.RepeatTimesPerPeriod > 31 || dto.RepeatTimesPerPeriod < 1)
                        return Result.Failure(new Error("ValidationError", "Cannot be more than 31 times per month."));
                }

                break;


            default:
                return Result.Failure(new Error("ValidationError", "Unsupported RepeatType."));
        }
        
        return Result.Success();
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
    private async Task<ApplicationUser?> SyncProStatusAndGetUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null && user.IsPro && user.ProExpiration != null && user.ProExpiration < DateTime.UtcNow)
        {
            user.IsPro = false;
            await _userManager.UpdateAsync(user);
        }
        return user;
    }
    public async Task<Result<MonthlyRecapDto>> GetMonthlyRecapAsync(string userId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var habits = await _context.Habits
            .AsNoTracking()
            .Include(h => h.Logs.Where(l => l.Date >= startDate && l.Date <= endDate && !l.IsDeleted))
            .Where(h => h.UserId == userId && !h.IsDeleted)
            .ToListAsync();

        if (habits.Count == 0)
            return Result.Failure<MonthlyRecapDto>(new Error("NoData", "No habits found for this period."));

        var recap = new MonthlyRecapDto
        {
            MonthName = startDate.ToString("MMMM yyyy"),
            CompletedDaysCount = habits.Sum(h => h.Logs.Count(l => l.IsCompleted))
        };

        foreach (var habit in habits)
        {
            var logs = habit.Logs.OrderBy(l => l.Date).ToList();
            var completedLogs = logs.Where(l => l.IsCompleted).ToList();
            
            // Basic completion rate calculation (Completions / Total expected days in month)
            // For simplicity, we compare to the total days in month, but ideally, we should check RepeatType logic.
            int totalDaysInMonth = DateTime.DaysInMonth(year, month);
            int rate = (int)((double)completedLogs.Count / totalDaysInMonth * 100);

            // Streak calculation within the month
            int bestStreak = 0;
            int currentStreak = 0;
            DateTime? lastDate = null;

            foreach (var log in completedLogs)
            {
                if (lastDate == null || (log.Date.Date - lastDate.Value.Date).Days == 1)
                {
                    currentStreak++;
                }
                else
                {
                    currentStreak = 1;
                }
                lastDate = log.Date;
                if (currentStreak > bestStreak) bestStreak = currentStreak;
            }

            recap.HabitSummaries.Add(new HabitSummaryDto
            {
                Title = habit.Title,
                CompletionRate = rate,
                BestStreak = bestStreak
            });
        }

        // Best & Worst habit of the month
        if (recap.HabitSummaries.Count > 0)
        {
            recap.TotalCompletionRate = (int)recap.HabitSummaries.Average(s => s.CompletionRate);
            recap.BestHabit  = recap.HabitSummaries.OrderByDescending(s => s.CompletionRate).First().Title;
            recap.WorstHabit = recap.HabitSummaries.OrderBy(s => s.CompletionRate).First().Title;

            // Only set WorstHabit if it differs from BestHabit (i.e. more than one habit)
            if (recap.BestHabit == recap.WorstHabit)
                recap.WorstHabit = null;
        }

        // Previous month comparison
        var prevStart = startDate.AddMonths(-1);
        var prevEnd   = startDate.AddDays(-1);
        var prevHabits = await _context.Habits
            .AsNoTracking()
            .Include(h => h.Logs.Where(l => l.Date >= prevStart && l.Date <= prevEnd && !l.IsDeleted))
            .Where(h => h.UserId == userId && !h.IsDeleted)
            .ToListAsync();

        if (prevHabits.Count > 0)
        {
            int prevDays = DateTime.DaysInMonth(prevStart.Year, prevStart.Month);
            var prevRates = prevHabits.Select(h =>
                (int)((double)h.Logs.Count(l => l.IsCompleted) / prevDays * 100));
            recap.PreviousMonthRate = (int)prevRates.Average();
        }

        // AI Insight
        var aiPrompt = $"Monthly Progress: {recap.TotalCompletionRate}% overall completion. " +
                       $"Total completions: {recap.CompletedDaysCount}. " +
                       $"Top streaks: {string.Join(", ", recap.HabitSummaries.Select(s => $"{s.Title}: {s.BestStreak} days"))}.";

        recap.AiInsight = await _aiService.GenerateMonthlyRecapInsightAsync(aiPrompt);

        return Result.Success(recap);
    }
}