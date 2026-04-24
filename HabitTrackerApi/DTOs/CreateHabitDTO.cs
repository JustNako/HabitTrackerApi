namespace HabitTrackerApi.DTOs;

public class CreateHabitDto
{
    public string Title { get; set; }
    public string? Description { get; set; }
    public int TargetDays { get; set; }
}