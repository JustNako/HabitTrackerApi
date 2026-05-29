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

    /// <summary>
    /// Shares a monthly recap and generates a unique link.
    /// </summary>
    [HttpPost("share-recap")]
    [Authorize]
    public async Task<IActionResult> ShareRecap([FromBody] SharedRecapDto dto, [FromServices] HabitTrackerApi.Data.AppDbContext dbContext)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var sharedRecap = new HabitTrackerApi.Models.SharedRecap
        {
            UserId = userId,
            Year = dto.Year,
            Month = dto.Month,
            TotalRate = dto.TotalRate,
            BestStreak = dto.BestStreak,
            BestHabit = dto.BestHabit,
            ArchetypeTitle = dto.ArchetypeTitle,
            ArchetypeDesc = dto.ArchetypeDesc,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.SharedRecaps.Add(sharedRecap);
        await dbContext.SaveChangesAsync();

        var link = $"https://flowtracks.app/recap/{sharedRecap.Id}";
        return Ok(new { link });
    }

    /// <summary>
    /// Gets a shared recap by its unique Id. Public route.
    /// </summary>
    [HttpGet("shared-recap/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSharedRecap(Guid id, [FromServices] HabitTrackerApi.Data.AppDbContext dbContext)
    {
        var recap = await dbContext.SharedRecaps.FindAsync(id);
        if (recap == null)
            return NotFound();

        var user = await dbContext.Users.FindAsync(recap.UserId);
        var userName = user?.UserName ?? "Someone";

        return Ok(new
        {
            recap.Id,
            userName,
            recap.Year,
            recap.Month,
            recap.TotalRate,
            recap.BestStreak,
            recap.BestHabit,
            recap.ArchetypeTitle,
            recap.ArchetypeDesc,
            recap.CreatedAt
        });
    }
}
