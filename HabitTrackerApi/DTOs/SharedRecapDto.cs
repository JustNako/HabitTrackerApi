namespace HabitTrackerApi.DTOs
{
    public class SharedRecapDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int TotalRate { get; set; }
        public int BestStreak { get; set; }
        public string BestHabit { get; set; } = string.Empty;
        public string ArchetypeTitle { get; set; } = string.Empty;
        public string ArchetypeDesc { get; set; } = string.Empty;
    }
}
