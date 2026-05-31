using HabitTrackerApi.Data;
using HabitTrackerApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApi.Services;

public class AchievementEvaluatorService
{
    private readonly AppDbContext _context;
    private readonly NotificationService _notifications;

    public AchievementEvaluatorService(AppDbContext context, NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task EvaluateUserAsync(string userId)
    {
        var user = await _context.Users
            .Include(u => u.Habits)
                .ThenInclude(h => h.Logs)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return;

        var existingAchievements = await _context.UserAchievements
            .Where(a => a.UserId == userId)
            .Select(a => a.Type)
            .ToListAsync();

        var newAchievements = new List<AchievementType>();

        // Habit Creation
        if (!existingAchievements.Contains(AchievementType.FirstHabitCreated) && user.Habits.Any())
        {
            newAchievements.Add(AchievementType.FirstHabitCreated);
        }

        // Public Habit
        if (!existingAchievements.Contains(AchievementType.FirstPublicHabit) && user.Habits.Any(h => h.IsPublic))
        {
            newAchievements.Add(AchievementType.FirstPublicHabit);
        }

        // Habit Completion & Streaks
        bool hasCompletedLog = false;
        int maxStreak = 0;

        foreach (var habit in user.Habits)
        {
            var logs = habit.Logs.OrderByDescending(l => l.Date).ToList();
            if (logs.Any(l => l.IsCompleted)) hasCompletedLog = true;

            int currentStreak = CalculateStreak(logs);
            if (currentStreak > maxStreak) maxStreak = currentStreak;
        }

        if (!existingAchievements.Contains(AchievementType.FirstHabitCompleted) && hasCompletedLog)
        {
            newAchievements.Add(AchievementType.FirstHabitCompleted);
        }

        if (!existingAchievements.Contains(AchievementType.SevenDayStreak) && maxStreak >= 7)
        {
            newAchievements.Add(AchievementType.SevenDayStreak);
        }

        if (!existingAchievements.Contains(AchievementType.ThirtyDayStreak) && maxStreak >= 30)
        {
            newAchievements.Add(AchievementType.ThirtyDayStreak);
        }

        if (!existingAchievements.Contains(AchievementType.HundredDayStreak) && maxStreak >= 100)
        {
            newAchievements.Add(AchievementType.HundredDayStreak);
        }

        // Friends
        var friendsCount = await _context.Friendships
            .CountAsync(f => (f.RequesterId == userId || f.AddresseeId == userId) && f.Status == FriendshipStatus.Accepted);

        if (!existingAchievements.Contains(AchievementType.FirstFriendAdded) && friendsCount >= 1)
        {
            newAchievements.Add(AchievementType.FirstFriendAdded);
        }

        if (!existingAchievements.Contains(AchievementType.TenFriendsAdded) && friendsCount >= 10)
        {
            newAchievements.Add(AchievementType.TenFriendsAdded);
        }

        if (!existingAchievements.Contains(AchievementType.FiftyFriendsAdded) && friendsCount >= 50)
        {
            newAchievements.Add(AchievementType.FiftyFriendsAdded);
        }

        if (!existingAchievements.Contains(AchievementType.HundredFriendsAdded) && friendsCount >= 100)
        {
            newAchievements.Add(AchievementType.HundredFriendsAdded);
        }

        // Save new achievements and notify
        if (newAchievements.Any())
        {
            foreach (var achievementType in newAchievements)
            {
                _context.UserAchievements.Add(new UserAchievement
                {
                    UserId = userId,
                    Type = achievementType,
                    UnlockedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // Notify user
            if (!string.IsNullOrEmpty(user.FcmToken))
            {
                var title = newAchievements.Count == 1 ? "Achievement Unlocked!" : "Multiple Achievements Unlocked!";
                var body = newAchievements.Count == 1 
                    ? $"You just unlocked: {GetAchievementTitle(newAchievements.First())}" 
                    : $"You just unlocked {newAchievements.Count} new achievements!";

                await _notifications.SendAsync(
                    user.FcmToken,
                    title,
                    body,
                    new Dictionary<string, string> { ["type"] = "achievement" }
                );
            }
        }
    }

    private static int CalculateStreak(List<HabitLog> logs)
    {
        var streak = 0;
        var date = DateTime.UtcNow.Date;
        foreach (var log in logs)
        {
            if (log.Date.Date == date && log.IsCompleted)
            {
                streak++;
                date = date.AddDays(-1);
            }
            else break;
        }
        return streak;
    }

    private static string GetAchievementTitle(AchievementType type)
    {
        return type switch
        {
            AchievementType.FirstHabitCreated => "First Step",
            AchievementType.FirstHabitCompleted => "Action Taker",
            AchievementType.SevenDayStreak => "7-Day Streak",
            AchievementType.ThirtyDayStreak => "30-Day Streak",
            AchievementType.HundredDayStreak => "Centurion",
            AchievementType.FirstPublicHabit => "Going Public",
            AchievementType.FirstFriendAdded => "Social Butterfly",
            AchievementType.TenFriendsAdded => "Popular",
            AchievementType.FiftyFriendsAdded => "Influencer",
            AchievementType.HundredFriendsAdded => "Leader",
            _ => "Secret Achievement"
        };
    }
}
