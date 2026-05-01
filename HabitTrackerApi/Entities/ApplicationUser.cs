using Microsoft.AspNetCore.Identity;

namespace HabitTrackerApi.Entities;


public class ApplicationUser : IdentityUser
{
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public List<Habit> Habits { get; set; }
    public bool IsPro { get; set; } = false;
}