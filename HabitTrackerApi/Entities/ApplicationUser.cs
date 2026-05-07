using Microsoft.AspNetCore.Identity;

namespace HabitTrackerApi.Entities;


public class ApplicationUser : IdentityUser
{
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public List<Habit> Habits { get; set; }
    public bool IsPro { get; set; } = false;
    public DateTime? ProExpiration { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public string? FcmToken { get; set; }         // Firebase push notification token
    public bool ReminderEnabled { get; set; } = false;
    public int ReminderFrequency { get; set; } = 1;  // 1 = once a day, 2 = twice a day
    public List<Friendship> SentRequests { get; set; } = [];
    public List<Friendship> ReceivedRequests { get; set; } = [];
}