using HabitTrackerApi.Data;
using HabitTrackerApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApi.Services;

/// <summary>
/// Background service that sends daily/twice-daily habit reminders to all
/// users who have reminders enabled and still have incomplete habits today.
/// </summary>
public class HabitReminderService : BackgroundService
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HabitReminderService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = GetNextScheduledTime(now);
            var delay = nextRun - now;

            _logger.LogInformation("Next reminder batch at {NextRun} UTC (in {Delay})", nextRun, delay);

            await Task.Delay(delay, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            await SendRemindersAsync(nextRun.Hour, stoppingToken);
        }
    }

    /// <summary>Returns the next scheduled reminder time after <paramref name="now"/>.</summary>
    private static DateTime GetNextScheduledTime(DateTime now)
    {
        var today = now.Date;

        foreach (var schedule in _schedules)
        {
            var candidate = today + schedule;
            if (candidate > now) return candidate;
        }

        // All today's slots passed — first slot of tomorrow
        return today.AddDays(1) + _schedules[0];
    }

    private async Task SendRemindersAsync(int hour, CancellationToken ct)
    {
        _logger.LogInformation("Sending habit reminders (slot {Hour}:00 UTC)…", hour);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            // Only users who have reminders enabled
            var users = await userManager.Users
                .Where(u => u.ReminderEnabled && u.FcmToken != null)
                .ToListAsync(ct);

            var today = DateTime.UtcNow.Date;
            var sent = 0;

            foreach (var user in users)
            {
                // Skip the evening slot for users who only want 1 reminder per day
                if (user.ReminderFrequency == 1 && hour == 18) continue;

                // Check if the user has any habit that is NOT completed today
                var hasIncomplete = await db.Habits
                    .Where(h => h.UserId == user.Id && !h.IsDeleted)
                    .AnyAsync(h => !h.Logs.Any(l => l.Date == today && l.IsCompleted), ct);

                if (!hasIncomplete) continue; // All habits done — no need to annoy them

                await notifications.SendAsync(
                    fcmToken: user.FcmToken!,
                    title: "⚡ Keep your streak alive!",
                    body: "You still have habits to complete today. Open Flow and knock them out!",
                    data: new Dictionary<string, string> { ["type"] = "reminder" }
                );
                sent++;
            }

            _logger.LogInformation("Sent {Count} reminder(s) for slot {Hour}:00.", sent, hour);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending habit reminders.");
        }
    }
}
