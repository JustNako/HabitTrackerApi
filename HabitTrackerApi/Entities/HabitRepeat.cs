namespace HabitTrackerApi.Entities;

public enum HabitRepeatType
{
    EveryDay = 1,
    SpecificDaysOfWeek = 2,
    SpecificDaysOfMonth = 3,
    XTimesPerPeriod = 4
}

public enum HabitRepeatPeriod
{
    Week = 1,
    Month = 2
}
