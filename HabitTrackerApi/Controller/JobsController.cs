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

        await _reminderService.SendRemindersAsync();

        return Ok(new { message = "Hourly reminders check processed successfully." });
    }
}
