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
    public bool ReminderEnabled { get; set; } = true;
    public int ReminderFrequency { get; set; } = 2;  // 1 = once a day, 2 = twice a day (Morning & Evening)
    public int TimeZoneOffset { get; set; } = 0;    // Offset in minutes from UTC (e.g., +120 for GMT+2)
    
    // AI Generation Tracking
    public int AiGenerationsCount { get; set; } = 0;
    public DateTime? LastAiGenerationDate { get; set; }

    public List<Friendship> SentRequests { get; set; } = [];
    public List<Friendship> ReceivedRequests { get; set; } = [];
}