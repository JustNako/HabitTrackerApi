using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HabitTrackerApi.Entities;
using HabitTrackerApi.Services;

namespace HabitTrackerApi.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/admin")]
[ApiVersion("1.0")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        EmailService emailService,
        IConfiguration config,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    [HttpPost("send-update-emails")]
    public async Task<IActionResult> SendUpdateNotification([FromBody] SendUpdateEmailDto dto)
    {
        // 1. Verify administrative API key
        var configuredApiKey = _config["AppConfig:AdminApiKey"];
        if (string.IsNullOrEmpty(configuredApiKey) || dto.ApiKey != configuredApiKey)
        {
            return Unauthorized(new { message = "Invalid API key." });
        }

        if (string.IsNullOrWhiteSpace(dto.Version) || string.IsNullOrWhiteSpace(dto.ReleaseNotes))
        {
            return BadRequest(new { message = "Version and ReleaseNotes are required." });
        }

        // 2. Fetch target users
        var users = new System.Collections.Generic.List<UserTarget>();
        
        if (!string.IsNullOrWhiteSpace(dto.TestEmail))
        {
            // Try to find the user in the database to get their real username
            var existingUser = await _userManager.FindByEmailAsync(dto.TestEmail);
            var targetName = existingUser?.UserName ?? "Tester";

            users.Add(new UserTarget { Email = dto.TestEmail, UserName = targetName });
            _logger.LogInformation("Testing update email campaign. Targeted email: {Email} (resolved name: {Name})", dto.TestEmail, targetName);
        }
        else
        {
            // Fetch all registered users with valid emails
            var dbUsers = await _userManager.Users
                .Where(u => u.Email != null && u.Email.Contains("@"))
                .Select(u => new { u.Email, u.UserName })
                .ToListAsync();

            users.AddRange(dbUsers.Select(u => new UserTarget { Email = u.Email, UserName = u.UserName }));
        }

        if (!users.Any())
        {
            return Ok(new { message = "No targets found for the email campaign." });
        }

        // 3. Fire and forget the email sending loop in background
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Starting update email campaign for version {Version}. Target count: {Count}", dto.Version, users.Count);
            
            int successCount = 0;
            foreach (var user in users)
            {
                try
                {
                    await _emailService.SendUpdateNotificationEmailAsync(
                        user.Email!, 
                        user.UserName ?? "User", 
                        dto.Version, 
                        dto.ReleaseNotes
                    );
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send update email to {Email}", user.Email);
                }

                await Task.Delay(300);
            }

            _logger.LogInformation("Update email campaign for version {Version} completed. Sent successfully: {Success}/{Total}", dto.Version, successCount, users.Count);
        });

        return Ok(new { message = $"Email broadcast campaign started in background. Targeted targets: {users.Count}." });
    }
}

public class UserTarget
{
    public string Email { get; set; }
    public string UserName { get; set; }
}

public class SendUpdateEmailDto
{
    public required string ApiKey { get; set; }
    public required string Version { get; set; }
    public required string ReleaseNotes { get; set; }
    public string? TestEmail { get; set; }
}
