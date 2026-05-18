using HabitTrackerApi.Data;
using HabitTrackerApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApi.Services;

/// <summary>
/// Scoped service that sends habit reminders to all
/// users who have reminders enabled and still have incomplete habits.
/// Triggered via JobsController.
/// </summary>
public class HabitReminderService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HabitReminderService> _logger;

    // Times at which we attempt to send reminders (UTC)
    private static readonly TimeSpan[] _schedules =
    [
        new TimeSpan(7, 0, 0),   // 07:00 UTC — morning reminder
        new TimeSpan(18, 0, 0),  // 18:00 UTC — evening reminder
    ];

    public HabitReminderService(IServiceScopeFactory scopeFactory, ILogger<HabitReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    public async Task SendRemindersAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Processing hourly habit reminders check...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            // 1. Get all active users with reminders enabled
            var allUsersWithReminders = await userManager.Users
                .Where(u => u.ReminderEnabled && u.FcmToken != null)
                .ToListAsync(ct);

            // 2. Filter users whose LOCAL hour is currently 7 (morning) or 18 (evening)
            var usersToRemind = allUsersWithReminders
                .Select(u => {
                    var localTime = DateTime.UtcNow.AddMinutes(u.TimeZoneOffset);
                    return new { User = u, LocalTime = localTime };
                })
                .Where(x => {
                    int hour = x.LocalTime.Hour;
                    // Morning (7 AM) or Evening (6 PM) unless they only want 1 reminder per day
                    return hour == 7 || (hour == 18 && x.User.ReminderFrequency != 1);
                })
                .ToList();

            if (usersToRemind.Count == 0) return;

            var sent = 0;

            foreach (var item in usersToRemind)
            {
                var user = item.User;
                var localTime = item.LocalTime;
                var utcToday = DateTime.UtcNow.Date;
                int dayOfWeekDart = localTime.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)localTime.DayOfWeek;
                int dayOfMonth = localTime.Day;

                // Fetch habits and their completion status for today (using UTC today as stored in db)
                var activeHabits = await db.Habits
                    .Where(h => h.UserId == user.Id && !h.IsDeleted)
                    .Select(h => new { 
                        h.Id, 
                        h.Title, 
                        h.RepeatType, 
                        h.RepeatDaysOfWeek, 
                        h.RepeatDaysOfMonth,
                        IsCompletedToday = h.Logs.Any(l => l.Date == utcToday && l.IsCompleted)
                    })
                    .ToListAsync(ct);

                // Filter by schedule in-memory
                var incompleteHabits = activeHabits.Where(h => 
                {
                    if (h.IsCompletedToday) return false;
                    
                    if (h.RepeatType == HabitRepeatType.EveryDay) return true;
                    if (h.RepeatType == HabitRepeatType.SpecificDaysOfWeek && !string.IsNullOrEmpty(h.RepeatDaysOfWeek))
                    {
                        var days = h.RepeatDaysOfWeek.Split(',').Select(s => int.TryParse(s, out int d) ? d : 0);
                        return days.Contains(dayOfWeekDart);
                    }
                    if (h.RepeatType == HabitRepeatType.SpecificDaysOfMonth && !string.IsNullOrEmpty(h.RepeatDaysOfMonth))
                    {
                        var days = h.RepeatDaysOfMonth.Split(',').Select(s => int.TryParse(s, out int d) ? d : 0);
                        return days.Contains(dayOfMonth);
                    }
                    return false;
                }).ToList();

                if (incompleteHabits.Count == 0) 
                    continue; // All scheduled habits completed for today

                var habitTitles = incompleteHabits.Select(h => h.Title).ToList();

                string title = "⚡ Keep your streak alive!";
                string body = "You still have habits to complete today. Open Flow and knock them out!";

                // 🔥 Premium Feature: AI-Generated Personalized Push Notifications
                if (user.IsPro)
                {
                    try
                    {
                        var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                        string prompt = $"Uncompleted habits: {string.Join(", ", habitTitles)}";
                        body = await aiService.GenerateNotificationMessageAsync(prompt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to generate AI notification for user {UserId}: {Message}", user.Id, ex.Message);
                    }
                }

                // Add up to 3 habits to the payload for interactive buttons
                var topHabits = incompleteHabits.Take(3).ToList();
                var habitIdsString = string.Join(",", topHabits.Select(h => h.Id.ToString()));
                var habitTitlesString = string.Join(",", topHabits.Select(h => h.Title));

                await notifications.SendAsync(
                    fcmToken: user.FcmToken!,
                    title: title,
                    body: body,
                    data: new Dictionary<string, string> 
                    { 
                        ["type"] = "reminder",
                        ["habitIds"] = habitIdsString,
                        ["habitTitles"] = habitTitlesString
                    }
                );
                sent++;
            }

            _logger.LogInformation("Sent {Count} reminder(s) based on local timezones.", sent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending habit reminders.");
        }
    }
}
