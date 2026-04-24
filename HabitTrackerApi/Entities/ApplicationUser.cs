using Microsoft.AspNetCore.Identity;

namespace HabitTrackerApi.Entities;


public class ApplicationUser : IdentityUser
{
    public List<Habit> Habits { get; set; }
}