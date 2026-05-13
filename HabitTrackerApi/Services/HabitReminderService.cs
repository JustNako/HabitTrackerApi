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
    
    public async Task SendRemindersAsync(int hour, CancellationToken ct = default)
    {
        _logger.LogInformation("Sending habit reminders (slot {Hour}:00 UTC)…", hour);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            // Only users who have reminders enabled
            // 1. Get users whose local time matches the slot hour and have reminders enabled
            var allUsersWithReminders = await userManager.Users
                .Where(u => u.ReminderEnabled && u.FcmToken != null)
                .ToListAsync(ct);

            // Filter in-memory to handle timezone logic correctly (EF Core has trouble adding minutes to UtcNow in DB)
            var usersToRemind = allUsersWithReminders
                .Where(u => {
                    var userLocalTime = DateTime.UtcNow.AddMinutes(u.TimeZoneOffset);
                    return userLocalTime.Hour == hour;
                })
                .ToList();

            if (usersToRemind.Count == 0) return;

            var today = DateTime.UtcNow.Date;
            var userIds = usersToRemind.Select(u => u.Id).ToList();

            // 2. Batch fetch ALL incomplete habits for these users in ONE query
            var incompleteHabitsMap = await db.Habits
                .Where(h => userIds.Contains(h.UserId) && !h.IsDeleted)
                .Where(h => !h.Logs.Any(l => l.Date == today && l.IsCompleted))
                .GroupBy(h => h.UserId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(h => h.Title).ToList(), ct);

            var sent = 0;

            foreach (var user in usersToRemind)
            {
                // Skip the evening slot for users who only want 1 reminder per day
                // Slot 18 is hardcoded as evening in our system
                if (user.ReminderFrequency == 1 && hour == 18) continue;

                if (!incompleteHabitsMap.TryGetValue(user.Id, out var incompleteHabits) || incompleteHabits.Count == 0) 
                    continue; // All habits done

                string title = "⚡ Keep your streak alive!";
                string body = "You still have habits to complete today. Open Flow and knock them out!";

                // 🔥 Premium Feature: AI-Generated Personalized Push Notifications
                if (user.IsPro)
                {
                    try
                    {
                        var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                        string prompt = $"Uncompleted habits: {string.Join(", ", incompleteHabits)}";
                        body = await aiService.GenerateNotificationMessageAsync(prompt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to generate AI notification for user {UserId}: {Message}", user.Id, ex.Message);
                    }
                }

                await notifications.SendAsync(
                    fcmToken: user.FcmToken!,
                    title: title,
                    body: body,
                    data: new Dictionary<string, string> { ["type"] = "reminder" }
                );
                sent++;
            }

            _logger.LogInformation("Sent {Count} reminder(s) for slot {Hour}:00 based on local timezones.", sent, hour);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending habit reminders.");
        }
    }
}
