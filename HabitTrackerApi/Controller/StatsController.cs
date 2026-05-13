using System.Security.Claims;
using HabitTrackerApi.Common;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HabitTrackerApi.Controller;

[ApiController]
[Route("api/v{version:apiVersion}/stats")]
[ApiVersion("1.0")]
public class StatsController : ControllerBase
{
    private readonly HabitService _habitService;

    public StatsController(HabitService habitService)
    {
        _habitService = habitService;
    }

    /// <summary>
    /// Returns a comprehensive monthly progress recap with AI insights.
    /// </summary>
    [HttpGet("monthly")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyRecap([FromQuery] int year, [FromQuery] int month)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var result = await _habitService.GetMonthlyRecapAsync(userId, year, month);

        if (result.IsFailure)
        {
            if (result.Error.Code == "NoData")
                return NotFound(new { message = result.Error.Message });

            return BadRequest(new { message = result.Error.Message });
        }

        return Ok(result.Value);
    }
}
