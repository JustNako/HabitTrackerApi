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

public class EditProfileDto
{
    public string? UserName { get; set; }
    
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    public string? Email { get; set; }
    
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

public class ResendConfirmationDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
