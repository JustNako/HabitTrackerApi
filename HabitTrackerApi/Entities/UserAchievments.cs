using HabitTrackerApi.Entities;

namespace HabitTrackerApi.Entities;

public class UserAchievement
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AchievementType Type { get; set; }
    public DateTime UnlockedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

public enum AchievementType
{
    FirstHabitCreated,
    FirstHabitCompleted,
    SevenDayStreak,
    ThirtyDayStreak,
    HundredDayStreak,
    FirstPublicHabit,
    FirstFriendAdded,
    TenFriendsAdded,
    FiftyFriendsAdded,
    HundredFriendsAdded
}