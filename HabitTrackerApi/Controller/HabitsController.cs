using HabitTrackerApi.Common;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
            return Unauthorized(new { error = new Error("Auth", "User identifier not found in token.") });

        var result = await _service.GetUserHabits(userId);

        return Ok(result.Value);
    }

    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.GetHabitStats(id, userId);

        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateHabitDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { error = new Error("Auth", "User identifier not found in token.") });

        

        var result = await _service.CreateHabit(userId, dto);
        
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok();
    }



    [Authorize]
    [HttpPost("{id}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, [FromBody] ToggleHabitLogDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.ToggleHabit(userId, id, dto.Note);
        
        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [Authorize]
    [HttpPost("{id}/toggle-visibility")]
    public async Task<IActionResult> ToggleVisibility(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.ToggleVisibility(userId, id);
        
        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }



    [HttpGet("{id}/streak")]
    public async Task<IActionResult> GetStreak(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.GetStreak(id, userId);
        
        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok(new { streak = result.Value });
    }

    [Authorize]
    [EnableRateLimiting("AiLimit")]
    [HttpGet("ai-insights")]
    public async Task<IActionResult> GetAiInsights()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var result = await _service.GetAiInsights(userId);

        if (result.IsFailure)
        {
            if (result.Error.Code == "ProRequired")
                return StatusCode(403, new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [Authorize]
    [EnableRateLimiting("AiLimit")]
    [HttpGet("behavioral-analysis")]
    public async Task<IActionResult> GetBehavioralAnalysis()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var result = await _service.GetBehavioralAnalysis(userId);

        if (result.IsFailure)
        {
            if (result.Error.Code == "ProRequired")
                return StatusCode(403, new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpGet("global-streak")]
    public async Task<IActionResult> GetGlobalStreak()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.GetGlobalStreak(userId);
        
        return Ok(new { streak = result.Value });
    }

    [HttpGet("global-stats")]
    public async Task<IActionResult> GetGlobalStats()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.GetGlobalStats(userId);
        
        return Ok(result.Value);
    }

    [HttpGet("{id}/heatmap")]
    public async Task<IActionResult> HeatMap(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.GetHeatmap(id, userId);
        
        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }


    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHabitDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var result = await _service.UpdateHabitSettings(id, userId, dto);

        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateHabitNotesDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var result = await _service.UpdateHabitNotes(id, userId, dto.Notes);

        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var result = await _service.DeleteHabit(id, userId);

        if (result.IsFailure)
        {
            if (result.Error == Error.NotFound)
                return NotFound(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }
}