namespace HabitTrackerApi.DTOs;

public class FriendDto
{
    public Guid FriendshipId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsPro { get; set; }
    public int GlobalStreak { get; set; }
    public int TotalPublicHabits { get; set; }
    public List<PublicHabitDto> PublicHabits { get; set; } = [];
}

public class PublicHabitDto
{
    public string Title { get; set; } = string.Empty;
    public int CompletedThisWeek { get; set; }
    public int CurrentStreak { get; set; }
}

public class FriendRequestDto
{
    public Guid FriendshipId { get; set; }
    public string RequesterId { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
