using System;
using System.Collections.Generic;

namespace HabitTrackerApi.DTOs;

public class UpdateHabitDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? TargetDays { get; set; }
    public string? TimeOfDay { get; set; }
}
