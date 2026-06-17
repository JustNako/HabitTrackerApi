using System;
using System.Collections.Generic;

namespace HabitTrackerApi.DTOs;

public class BehavioralAnalysisDto
{
    public DonutRhythmDto Rhythm { get; set; } = new();
    public List<FrictionPatternDto> FrictionPatterns { get; set; } = new();
    public AiPrescriptionDto AiPrescription { get; set; } = new();
    public List<int> WeeklyRhythm { get; set; } = new();
}

public class DonutRhythmDto
{
    public int Completed { get; set; }
    public int Partial { get; set; }
    public int Missed { get; set; }
}

public class FrictionPatternDto
{
    public string Icon { get; set; } = string.Empty; // "moon", "friction", "alarm"
    public string Pattern { get; set; } = string.Empty;
}

public class AiPrescriptionDto
{
    public string Text { get; set; } = string.Empty;
    public List<PrescriptionActionDto> Actions { get; set; } = new();
}

public class PrescriptionActionDto
{
    public Guid HabitId { get; set; }
    public string HabitTitle { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "ReduceTarget", "Reschedule"
    public string CurrentValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public ActionPayloadDto Payload { get; set; } = new();
}

public class ActionPayloadDto
{
    public int? TargetDays { get; set; }
    public string? TimeOfDay { get; set; }
}
