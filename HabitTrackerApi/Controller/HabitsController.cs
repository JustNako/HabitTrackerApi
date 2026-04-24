using HabitTrackerApi.DTOs;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HabitTrackerApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
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

        await _service.CreateHabit(userId, dto);

        return Ok();
    }



    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> ToggleHabit(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        await _service.ToggleTodayHabit(id, userId);

        return Ok(new {message = "Toggled"});
    }



    [HttpGet("{id}/streak")]
    public async Task<IActionResult> GetStreak(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var streak = await _service.GetStreak(id, userId);

        return Ok(new { streak });
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