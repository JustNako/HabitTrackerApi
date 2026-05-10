using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace HabitTrackerApi.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/jobs")]
[ApiVersion("1.0")]
public class JobsController : ControllerBase
{
    private readonly HabitReminderService _reminderService;
    private readonly IConfiguration _config;

    public JobsController(HabitReminderService reminderService, IConfiguration config)
    {
        _reminderService = reminderService;
        _config = config;
    }

    [HttpGet("send-reminders")]
    [HttpPost("send-reminders")]
    public async Task<IActionResult> SendReminders([FromQuery] string apiKey)
    {
        // 1. Secure the endpoint so hackers can't trigger it manually
        var configuredKey = _config["AppConfig:CronApiKey"] ?? "default_secret_key_123";
        if (apiKey != configuredKey)
        {
            return Unauthorized(new { message = "Invalid API Key." });
        }

        // 2. Determine which slot to run (morning or evening)
        // Cron jobs will hit this at exactly 07:00 UTC and 18:00 UTC.
        int currentHour = DateTime.UtcNow.Hour;
        
        // Allow a small window (e.g., if cron hits at 07:01)
        int targetHour = currentHour < 12 ? 7 : 18;

        await _reminderService.SendRemindersAsync(targetHour);

        return Ok(new { message = $"Reminders triggered successfully for slot {targetHour}:00 UTC." });
    }
}
