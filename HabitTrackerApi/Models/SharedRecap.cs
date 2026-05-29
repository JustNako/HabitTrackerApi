using System;
using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApi.Models
{
    public class SharedRecap
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string UserId { get; set; } = string.Empty;
        
        public int Year { get; set; }
        public int Month { get; set; }
        
        public int TotalRate { get; set; }
        public int BestStreak { get; set; }
        public string BestHabit { get; set; } = string.Empty;
        
        public string ArchetypeTitle { get; set; } = string.Empty;
        public string ArchetypeDesc { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
