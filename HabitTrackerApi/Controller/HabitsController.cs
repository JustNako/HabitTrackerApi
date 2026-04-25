using HabitTrackerApi.DTOs;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HabitTrackerApi.Controllers;

[Authorize]
[ApiController]
[Route("api/v{version:apiVersion}/habits")]
[ApiVersion("1.0")]
public class HabitsController : ControllerBase
{
    private readonly HabitService _service;

    public HabitsController(HabitService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyHabits()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "User identifier not found in token." });

        var habits = await _service.GetUserHabits(userId);

        return Ok(habits);
    }

    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var stats = await _service.GetHabitStats(id, userId);

        if (stats == null)
            return NotFound(new { message = "Habit not found or you don't have permission to view it." });

        return Ok(stats);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateHabitDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "User identifier not found in token." });

        try
        {
            await _service.CreateHabit(userId, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok();
    }



    [Authorize]
    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, [FromBody] ToggleHabitLogDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var success = await _service.ToggleHabit(userId, id, dto.Note);
        if (!success)
            return NotFound(new { message = "Habit not found or access denied." });

        return Ok();
    }



    [HttpGet("{id}/streak")]
    public async Task<IActionResult> GetStreak(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var streak = await _service.GetStreak(id, userId);

        return Ok(new { streak });
    }

    [HttpGet("{id}/heatmap")]
    public async Task<IActionResult> HeatMap(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var heatmap = await _service.GetHeatmap(id, userId);

        return Ok(heatmap);
    }


    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.DeleteHabit(id, userId);

        if (!result)
            return NotFound(new { message = "Habit not found or you don't have permission to delete it." });

        return Ok();
    }


    
}