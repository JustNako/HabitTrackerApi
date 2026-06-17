using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;
        var todayLocal = DateTime.UtcNow.AddMinutes(offset).Date;

        var dtos = habits.Select(h => {
            var completedCountLast30 = h.Logs.Count(l => l.IsCompleted && l.Date.Date >= todayLocal.AddDays(-30) && l.Date.Date <= todayLocal);
            var habitAgeDays = (todayLocal - h.CreatedAt.Date).Days + 1;
            var expectedDays = Math.Min(30, Math.Max(1, habitAgeDays));
            var rate = (int)Math.Min(100, Math.Round((double)completedCountLast30 / expectedDays * 100));

            return new HabitDto
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
                IsCompletedToday = h.Logs.Any(l => l.IsCompleted && l.Date.Date == todayLocal),
                IsPublic = h.IsPublic,
                Unit = h.Unit,
                TimeOfDay = h.TimeOfDay,
                CurrentStreak = CalculateCurrentStreak(
                    h.Logs.Where(l => l.IsCompleted).Select(l => l.Date.Date).ToList(),
                    todayLocal),
                CompletionRate30Day = rate
            };
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
            Unit = dto.Unit,
            TimeOfDay = dto.TimeOfDay
        };

        await _repo.Add(habit);
        await _repo.Save();

        // Evaluate achievements
        await _achievements.EvaluateUserAsync(userId);

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

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

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

        return Result.Success();
    }

    public async Task<Result> UpdateHabitSettings(Guid id, string userId, UpdateHabitDto dto)
    {
        var habit = await _context.Habits.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);
        if (habit == null)
            return Result.Failure(Error.NotFound);

        var hasTargetUpdate = dto.TargetDays.HasValue;
        var hasTimeUpdate = dto.TimeOfDay != null;
        var hasTitleUpdate = dto.Title != null && !string.IsNullOrWhiteSpace(dto.Title);
        var hasDescriptionUpdate = dto.Description != null;

        if (!hasTargetUpdate && !hasTimeUpdate && !hasTitleUpdate && !hasDescriptionUpdate)
            return Result.Failure(new Error("ValidationError", "No habit settings were provided to update."));

        if (dto.TargetDays.HasValue)
        {
            if (dto.TargetDays.Value <= 0)
                return Result.Failure(new Error("ValidationError", "Target must be greater than 0."));
            habit.TargetDays = dto.TargetDays.Value;
        }

        if (dto.TimeOfDay != null)
        {
            habit.TimeOfDay = string.IsNullOrWhiteSpace(dto.TimeOfDay) ? null : dto.TimeOfDay.Trim();
        }

        if (dto.Title != null && !string.IsNullOrWhiteSpace(dto.Title))
        {
            habit.Title = dto.Title.Trim();
        }

        if (dto.Description != null)
        {
            habit.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        }

        habit.UpdatedAt = DateTime.UtcNow;
        _context.Entry(habit).State = EntityState.Modified;
        await _repo.Save();

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

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

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

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

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

        return Result.Success();
    }

    public async Task<Result> ToggleHabit(string userId, Guid habitId, string? note)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure(Error.NotFound);

        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;
        var today = DateTime.UtcNow.AddMinutes(offset).Date;

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

        _insightsCache.TryRemove(userId, out _);
        _analysisCache.TryRemove(userId, out _);

        return Result.Success();
    }


    public async Task<Result<int>> GetStreak(Guid habitId, string userId)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure<int>(Error.NotFound);

        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;

        var logsDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.HabitId == habitId && l.IsCompleted)
            .OrderByDescending(l => l.Date)
            .Select(l => l.Date)
            .ToListAsync();

        int streak = 0;
        var today = DateTime.UtcNow.AddMinutes(offset).Date;

        // Allow streak to start from yesterday if today hasn't been completed yet.
        // This prevents showing 0 streak on an active ongoing streak before the user marks today.
        var todayCompleted = logsDates.Contains(today);
        var expected = todayCompleted ? today : today.AddDays(-1);

        foreach (var logDate in logsDates)
        {
            if (logDate == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (logDate < expected)
            {
                // Gap in the chain — streak broken
                break;
            }
            // logDate > expected — duplicate for the same day, skip
        }

        return Result.Success(streak);
    }

    public async Task<Result<int>> GetGlobalStreak(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;

        var completedDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.Habit.UserId == userId && l.IsCompleted)
            .Select(l => l.Date.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        int streak = 0;
        var expected = DateTime.UtcNow.AddMinutes(offset).Date;

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
        var offset = user?.TimeZoneOffset ?? 0;
        
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
            var targetMonth = DateTime.UtcNow.AddMinutes(offset).AddMonths(-i);
            var monthName = targetMonth.ToString("MMM");
            var count = completedDatesByHabit.Count(l => l.Date.Year == targetMonth.Year && l.Date.Month == targetMonth.Month);
            monthlyProgress.Add(new MonthlyProgressDto { Month = monthName, Completions = count });
        }

        // Weekly Completion Rate (Last 4 weeks)
        var weeklyCompletionRate = new List<WeeklyProgressDto>();
        var now = DateTime.UtcNow.AddMinutes(offset).Date;
        
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
                var today = DateTime.UtcNow.AddMinutes(offset).Date;
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

        // Real completions for the Insights Completions Graph
        var currentWeekCompletions = new List<int>();
        for (int i = 0; i < 7; i++)
        {
            var date = currentMonday.AddDays(i).Date;
            var count = completedDatesByHabit.Count(l => l.Date.Date == date);
            currentWeekCompletions.Add(count);
        }

        var last30DaysCompletions = new List<int>();
        var dateToday = DateTime.UtcNow.AddMinutes(offset).Date;
        for (int i = 23; i >= 0; i--) // The app expects 24 bars for the non-weekly graph based on _buildArtisticBarChart
        {
            var date = dateToday.AddDays(-i);
            var count = completedDatesByHabit.Count(l => l.Date.Date == date);
            last30DaysCompletions.Add(count);
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
            CurrentWeekCompletions = currentWeekCompletions,
            Last30DaysCompletions = last30DaysCompletions,
            IsPro = isCurrentlyPro,
            AiGenerationsRemaining = aiRemaining,
            AiTotalLimit = aiTotalLimit
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime GeneratedAt, AiInsightsResponseDto Response)> _insightsCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime GeneratedAt, BehavioralAnalysisDto Response)> _analysisCache = new();

    public async Task<Result<AiInsightsResponseDto>> GetAiInsights(string userId)
    {
        var user = await SyncProStatusAndGetUser(userId);
        bool isCurrentlyPro = user != null && user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

        if (!isCurrentlyPro)
        {
            return Result.Failure<AiInsightsResponseDto>(new Error("ProRequired", "AI Insights are available to Pro users only."));
        }

        // Cache: 1 hour for personalized, 5 minutes for fallback (prevents Groq spam during outages)
        var now = DateTime.UtcNow;
        if (_insightsCache.TryGetValue(userId, out var cached))
        {
            var maxAge = cached.Response.IsPersonalized
                ? TimeSpan.FromHours(1)
                : TimeSpan.FromMinutes(5);
            if (now - cached.GeneratedAt < maxAge)
            {
                return Result.Success(cached.Response);
            }
        }

        var offset = user?.TimeZoneOffset ?? 0;
        var startDate = DateTime.UtcNow.AddMinutes(offset).Date.AddDays(-7);
        
        var habits = await _context.Habits
            .AsNoTracking()
            .Include(h => h.Logs.Where(l => l.Date >= startDate))
            .Where(h => h.UserId == userId && !h.IsDeleted)
            .ToListAsync();

        if (!habits.Any())
        {
            return Result.Failure<AiInsightsResponseDto>(new Error("NoData", "Not enough habit data to generate insights."));
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
            var timeInfo = string.IsNullOrEmpty(habit.TimeOfDay) ? "no scheduled time" : $"scheduled at {habit.TimeOfDay}";
            var goalInfo = habit.TargetDays > 1 && !string.IsNullOrEmpty(habit.Unit) 
                ? $", Daily Goal: {habit.TargetDays} {habit.Unit}" 
                : habit.TargetDays > 1 ? $", Daily Goal: {habit.TargetDays}" : "";

            promptBuilder.AppendLine($"- Habit: \"{habit.Title}\" ({repeatInfo}, {timeInfo}{goalInfo})");
            promptBuilder.AppendLine($"  7-day pattern: {patternString} ({completedDays}/7 days)");
            if (ageInDays < 14) promptBuilder.AppendLine("  Note: This is a NEW habit, fragile phase.");
        }

        var generation = await _aiService.GenerateInsightsAsync(promptBuilder.ToString());

        var response = new AiInsightsResponseDto
        {
            Insights = generation.Insights,
            IsPersonalized = generation.IsPersonalized,
            Message = generation.FailureReason
        };

        // Always cache (personalized or fallback) to prevent repeated Groq calls
        _insightsCache[userId] = (DateTime.UtcNow, response);

        return Result.Success(response);
    }

    public async Task<Result<BehavioralAnalysisDto>> GetBehavioralAnalysis(string userId)
    {
        var user = await SyncProStatusAndGetUser(userId);
        bool isCurrentlyPro = user != null && user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

        if (!isCurrentlyPro)
        {
            return Result.Failure<BehavioralAnalysisDto>(new Error("ProRequired", "Behavioral analysis is available to Pro users only."));
        }

        var now = DateTime.UtcNow;
        if (_analysisCache.TryGetValue(userId, out var cached)
            && now - cached.GeneratedAt < TimeSpan.FromHours(1))
        {
            return Result.Success(cached.Response);
        }

        var offset = user?.TimeZoneOffset ?? 0;
        var today = DateTime.UtcNow.AddMinutes(offset).Date;
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentMonday = today.AddDays(-daysSinceMonday);
        var startDate = currentMonday.AddDays(-42); // 6 weeks ago Monday (gives exactly 49 days to end of current week)

        var habits = await _context.Habits
            .AsNoTracking()
            .Include(h => h.Logs.Where(l => l.Date >= startDate && l.Date <= today))
            .Where(h => h.UserId == userId && !h.IsDeleted)
            .ToListAsync();

        // 1. Build a robust fallback matching the user's actual habits
        var fallback = new BehavioralAnalysisDto();
        
        // Completion rate calculation
        double completionRate = 0;
        if (habits.Count > 0)
        {
            int totalCompletions = habits.Sum(h => h.Logs.Count(l => l.IsCompleted));
            int possibleCompletions = habits.Count * 14;
            completionRate = possibleCompletions == 0 ? 0 : Math.Round((double)totalCompletions / possibleCompletions * 100, 0);
        }
        else
        {
            completionRate = 19; // Default matching mockup
        }

        fallback.Rhythm.Completed = (int)completionRate;
        fallback.Rhythm.Partial = (int)Math.Round((100 - completionRate) * 0.3, 0);
        fallback.Rhythm.Missed = 100 - fallback.Rhythm.Completed - fallback.Rhythm.Partial;

        // Populate fallback friction patterns & actions based on actual user habits
        var habitList = habits.ToList();
        var readingHabit = habitList.FirstOrDefault(h => h.Title.Contains("read", StringComparison.OrdinalIgnoreCase) || h.Title.Contains("book", StringComparison.OrdinalIgnoreCase));
        var activeHabit = habitList.FirstOrDefault(h => h.Title.Contains("walk", StringComparison.OrdinalIgnoreCase) || h.Title.Contains("run", StringComparison.OrdinalIgnoreCase) || h.Title.Contains("gym", StringComparison.OrdinalIgnoreCase) || h.Title.Contains("exercise", StringComparison.OrdinalIgnoreCase));
        
        var primaryHabit = readingHabit ?? (habitList.Count > 0 ? habitList[0] : null);
        var secondaryHabit = activeHabit ?? (habitList.Count > 1 ? habitList[1] : null);

        var primaryTitle = primaryHabit?.Title ?? "Book reading";
        var primaryId = primaryHabit?.Id ?? Guid.Empty;
        var primaryTarget = primaryHabit?.TargetDays ?? 20;
        var primaryUnit = primaryHabit?.Unit ?? "pages";

        var secondaryTitle = secondaryHabit?.Title ?? "Evening walk";
        var secondaryId = secondaryHabit?.Id ?? Guid.Empty;
        var secondaryTime = secondaryHabit?.TimeOfDay ?? "20:00";

        fallback.FrictionPatterns.Add(new FrictionPatternDto
        {
            Icon = "moon",
            Pattern = $"Missed habits spike 3x on Thursday & Friday evenings."
        });
        fallback.FrictionPatterns.Add(new FrictionPatternDto
        {
            Icon = "friction",
            Pattern = $"\"{primaryTitle}\" breaks after 2-3 days when target is set above {primaryTarget} {primaryUnit}."
        });
        fallback.FrictionPatterns.Add(new FrictionPatternDto
        {
            Icon = "alarm",
            Pattern = $"\"{secondaryTitle}\" is skipped when reminder is set after {(secondaryTime.StartsWith("20") ? "8" : secondaryTime.StartsWith("21") ? "9" : "8")} pm."
        });

        fallback.AiPrescription.Text = $"Your morning habits are your superpower — protect them. For \"{primaryTitle}\", try reducing target to {(primaryTarget / 2 > 0 ? primaryTarget / 2 : 1)} {primaryUnit} on weekdays and let momentum do the rest. Move \"{secondaryTitle}\" reminder to 7:00 pm for a 3x better follow-through.";

        if (primaryHabit != null)
        {
            fallback.AiPrescription.Actions.Add(new PrescriptionActionDto
            {
                HabitId = primaryId,
                HabitTitle = primaryTitle,
                Type = "ReduceTarget",
                CurrentValue = $"{primaryTarget} {primaryUnit}",
                NewValue = $"{(primaryTarget / 2 > 0 ? primaryTarget / 2 : 1)} {primaryUnit}",
                ActionText = "Reduce target",
                Payload = new ActionPayloadDto { TargetDays = primaryTarget / 2 > 0 ? primaryTarget / 2 : 1 }
            });
        }
        else
        {
            fallback.AiPrescription.Actions.Add(new PrescriptionActionDto
            {
                HabitId = Guid.NewGuid(),
                HabitTitle = "Book reading",
                Type = "ReduceTarget",
                CurrentValue = "20 pages",
                NewValue = "10 pages",
                ActionText = "Reduce target",
                Payload = new ActionPayloadDto { TargetDays = 10 }
            });
        }

        if (secondaryHabit != null)
        {
            fallback.AiPrescription.Actions.Add(new PrescriptionActionDto
            {
                HabitId = secondaryId,
                HabitTitle = secondaryTitle,
                Type = "Reschedule",
                CurrentValue = $"{secondaryTime}",
                NewValue = "19:00",
                ActionText = "Reschedule",
                Payload = new ActionPayloadDto { TimeOfDay = "19:00" }
            });
        }
        else
        {
            fallback.AiPrescription.Actions.Add(new PrescriptionActionDto
            {
                HabitId = Guid.NewGuid(),
                HabitTitle = "Evening walk",
                Type = "Reschedule",
                CurrentValue = "8 pm",
                NewValue = "7 pm",
                ActionText = "Reschedule",
                Payload = new ActionPayloadDto { TimeOfDay = "19:00" }
            });
        }

        // Calculate Weekly Rhythm completed logs counts (past 49 days)
        var weeklyRhythm = new List<int>();
        for (int i = 0; i < 49; i++)
        {
            var date = startDate.AddDays(i).Date;
            int count = habits.Sum(h => h.Logs.Count(l => l.Date.Date == date && l.IsCompleted));
            weeklyRhythm.Add(count);
        }
        fallback.WeeklyRhythm = weeklyRhythm;

        if (habits.Count == 0)
        {
            return Result.Success(fallback);
        }

        // 2. Build AI prompt
        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine($"Analyze the user's completions for {habits.Count} habits over the last 14 days to find friction patterns and suggest settings adjustments (reducing daily targets or rescheduling reminders).");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Habits List:");

        var last14DaysStart = today.AddDays(-14);
        foreach (var habit in habits)
        {
            var logsLast14 = habit.Logs.Where(l => l.Date >= last14DaysStart).OrderBy(l => l.Date).ToList();
            var completedCount = logsLast14.Count(l => l.IsCompleted);
            
            var patternParts = new List<string>();
            for (int i = 0; i < 14; i++)
            {
                var d = last14DaysStart.AddDays(i);
                var isDone = logsLast14.Any(l => l.Date.Date == d.Date && l.IsCompleted);
                patternParts.Add(isDone ? "✅" : "❌");
            }
            var patternString = string.Join("", patternParts);

            var timeInfo = string.IsNullOrEmpty(habit.TimeOfDay) ? "no scheduled time" : $"scheduled at {habit.TimeOfDay}";
            var goalInfo = habit.TargetDays > 1 && !string.IsNullOrEmpty(habit.Unit) 
                ? $", Daily Goal: {habit.TargetDays} {habit.Unit}" 
                : habit.TargetDays > 1 ? $", Daily Goal: {habit.TargetDays}" : "";

            promptBuilder.AppendLine($"- Habit ID: {habit.Id}, Title: \"{habit.Title}\" ({timeInfo}{goalInfo})");
            promptBuilder.AppendLine($"  14-day history (newest on right): {patternString} ({completedCount}/14 days completed)");
        }

        // Call the AI Service
        var result = await _aiService.GenerateBehavioralAnalysisAsync(promptBuilder.ToString(), fallback);

        SanitizePrescriptionActions(result.Analysis.AiPrescription.Actions, habits);

        result.Analysis.WeeklyRhythm = weeklyRhythm;
        if (result.IsPersonalized)
        {
            _analysisCache[userId] = (DateTime.UtcNow, result.Analysis);
        }

        return Result.Success(result.Analysis);
    }

    private static void SanitizePrescriptionActions(List<PrescriptionActionDto> actions, List<Habit> habits)
    {
        var sanitized = new List<PrescriptionActionDto>();

        foreach (var action in actions)
        {
            var matched = habits.FirstOrDefault(h => h.Id == action.HabitId)
                ?? habits.FirstOrDefault(h => h.Title.Equals(action.HabitTitle, StringComparison.OrdinalIgnoreCase))
                ?? habits.FirstOrDefault(h => h.Title.Contains(action.HabitTitle, StringComparison.OrdinalIgnoreCase))
                ?? habits.FirstOrDefault(h => action.HabitTitle.Contains(h.Title, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
                continue;

            action.HabitId = matched.Id;
            action.HabitTitle = matched.Title;

            var isTargetAction = action.Type.Contains("Target", StringComparison.OrdinalIgnoreCase)
                || action.ActionText.Contains("target", StringComparison.OrdinalIgnoreCase);
            var isRescheduleAction = action.Type.Contains("Reschedule", StringComparison.OrdinalIgnoreCase)
                || action.ActionText.Contains("Reschedule", StringComparison.OrdinalIgnoreCase);

            if (isTargetAction)
            {
                if (!action.Payload.TargetDays.HasValue || action.Payload.TargetDays <= 0)
                    action.Payload.TargetDays = ParseFirstInt(action.NewValue);

                if (!action.Payload.TargetDays.HasValue || action.Payload.TargetDays <= 0)
                {
                    action.Payload.TargetDays = action.Type.Contains("Increase", StringComparison.OrdinalIgnoreCase)
                        ? Math.Max(matched.TargetDays + 1, 1)
                        : Math.Max(matched.TargetDays > 1 ? matched.TargetDays / 2 : 1, 1);
                }
            }

            if (isRescheduleAction)
            {
                if (string.IsNullOrWhiteSpace(action.Payload.TimeOfDay))
                    action.Payload.TimeOfDay = ParseTimeOfDay(action.NewValue) ?? matched.TimeOfDay;
            }

            if (isTargetAction && (!action.Payload.TargetDays.HasValue || action.Payload.TargetDays <= 0))
                continue;

            if (isRescheduleAction && string.IsNullOrWhiteSpace(action.Payload.TimeOfDay))
                continue;

            sanitized.Add(action);
        }

        actions.Clear();
        actions.AddRange(sanitized);
    }

    private static int? ParseFirstInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value, @"\d+");
        return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    private static string? ParseTimeOfDay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match24 = Regex.Match(value, @"\b(\d{1,2}):(\d{2})\b");
        if (match24.Success
            && int.TryParse(match24.Groups[1].Value, out var hour24)
            && int.TryParse(match24.Groups[2].Value, out var minute24)
            && hour24 is >= 0 and <= 23
            && minute24 is >= 0 and <= 59)
        {
            return $"{hour24:D2}:{minute24:D2}";
        }

        var match12 = Regex.Match(value, @"\b(\d{1,2})\s*(am|pm)\b", RegexOptions.IgnoreCase);
        if (match12.Success && int.TryParse(match12.Groups[1].Value, out var hour12))
        {
            var isPm = match12.Groups[2].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
            if (hour12 == 12)
                hour12 = isPm ? 12 : 0;
            else if (isPm)
                hour12 += 12;

            return $"{hour12:D2}:00";
        }

        return null;
    }

    public async Task<Result<HabitStatsDto>> GetHabitStats(Guid habitId, string userId)
    {
        var habitExists = await _context.Habits.AnyAsync(h => h.Id == habitId && h.UserId == userId);
        if (!habitExists)
            return Result.Failure<HabitStatsDto>(Error.NotFound);

        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;

        int totalDays = await _context.HabitLogs.CountAsync(l => l.HabitId == habitId);
        int completedDays = await _context.HabitLogs.CountAsync(l => l.HabitId == habitId && l.IsCompleted);

        var completedDates = await _context.HabitLogs
            .AsNoTracking()
            .Where(l => l.HabitId == habitId && l.IsCompleted)
            .Select(l => l.Date)
            .OrderBy(d => d)
            .ToListAsync();

        var today = DateTime.UtcNow.AddMinutes(offset).Date;

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

        var user = await _userManager.FindByIdAsync(userId);
        var offset = user?.TimeZoneOffset ?? 0;

        var startDate = DateTime.UtcNow.AddMinutes(offset).Date.AddDays(-30);
        var endDate = DateTime.UtcNow.AddMinutes(offset).Date;

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
                BestStreak = bestStreak,
                TimeOfDay = habit.TimeOfDay,
                TargetDays = habit.TargetDays,
                Unit = habit.Unit
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
                       $"Habit stats (with scheduled times and goals): " +
                       string.Join(", ", recap.HabitSummaries.Select(s => 
                           $"\"{s.Title}\" ({s.CompletionRate}% completion, best streak: {s.BestStreak} days{(string.IsNullOrEmpty(s.TimeOfDay) ? "" : $", scheduled at {s.TimeOfDay}")}{(s.TargetDays > 1 && !string.IsNullOrEmpty(s.Unit) ? $", Daily Goal: {s.TargetDays} {s.Unit}" : s.TargetDays > 1 ? $", Daily Goal: {s.TargetDays}" : "")})"));

        recap.AiInsight = await _aiService.GenerateMonthlyRecapInsightAsync(aiPrompt);

        return Result.Success(recap);
    }

    /// <summary>
    /// Calculates the current streak from a list of completed dates.
    /// Mirrors the logic in GetStreak: allows streak to start from yesterday
    /// if today hasn't been completed yet.
    /// </summary>
    private static int CalculateCurrentStreak(List<DateTime> completedDates, DateTime today)
    {
        if (completedDates.Count == 0) return 0;

        var sorted = completedDates.Distinct().OrderByDescending(d => d).ToList();

        var todayCompleted = sorted.Contains(today);
        var expected = todayCompleted ? today : today.AddDays(-1);

        int streak = 0;
        foreach (var date in sorted)
        {
            if (date == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (date < expected)
            {
                break;
            }
        }
        return streak;
    }
}