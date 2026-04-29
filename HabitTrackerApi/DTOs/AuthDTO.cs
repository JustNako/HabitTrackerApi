using System.ComponentModel.DataAnnotations;

namespace HabitTrackerApi.DTOs;

public class LoginDto
{
    public string UserName { get; set; }
    public string Password { get; set; }
}

public class RegisterDto
{
    public string UserName { get; set; }
    
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    public string Email { get; set; }
    
    public string Password { get; set; }
}