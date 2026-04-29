using HabitTrackerApi.Common;
using HabitTrackerApi.DTOs.Sync;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HabitTrackerApi.Controllers;

[Authorize]
[ApiController]
[Route("api/v{version:apiVersion}/sync")]
[ApiVersion("1.0")]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;

    public SyncController(SyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] SyncPullRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { error = new Error("Auth", "User identifier not found in token.") });

        var result = await _syncService.PullAsync(userId, request);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { error = new Error("Auth", "User identifier not found in token.") });

        if (request == null || (request.Habits.Count == 0 && request.HabitLogs.Count == 0))
        {
            return Ok(); // Нечего синхронизировать — это валидная ситуация
        }

        var result = await _syncService.PushAsync(userId, request);

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok();
    }
}
