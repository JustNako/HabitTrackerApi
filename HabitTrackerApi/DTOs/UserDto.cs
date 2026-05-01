using System;

namespace HabitTrackerApi.DTOs;

public class UserDto
{
    public string Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsPro { get; set; }
}
