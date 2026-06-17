using HabitTrackerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HabitTrackerApi.Controllers;

[Authorize]
[ApiController]
[Route("api/v{version:apiVersion}/friends")]
[ApiVersion("1.0")]
public class FriendsController : ControllerBase
{
    private readonly FriendService _service;

    public FriendsController(FriendService service)
    {
        _service = service;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Get your own invite code.</summary>
    [HttpGet("my-code")]
    public async Task<IActionResult> GetMyCode()
    {
        var code = await _service.GetMyInviteCode(UserId);
        return code is null ? NotFound() : Ok(new { inviteCode = code });
    }

    /// <summary>Send a friend request using someone's invite code.</summary>
    [HttpPost("add/{code}")]
    public async Task<IActionResult> SendRequest(string code)
    {
        var (success, error) = await _service.SendRequest(UserId, code);
        return success ? Ok(new { message = "Friend request sent!" }) : BadRequest(new { message = error });
    }

    /// <summary>Accept a pending friend request.</summary>
    [HttpPost("accept/{id:guid}")]
    public async Task<IActionResult> AcceptRequest(Guid id)
    {
        var (success, error) = await _service.AcceptRequest(UserId, id);
        return success ? Ok(new { message = "Friend request accepted!" }) : BadRequest(new { message = error });
    }

    /// <summary>Remove a friend or decline a request.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid id)
    {
        var (success, error) = await _service.RemoveFriend(UserId, id);
        return success ? Ok(new { message = "Removed." }) : BadRequest(new { message = error });
    }

    /// <summary>Get accepted friends with their public habits.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        var friends = await _service.GetFriends(UserId);
        return Ok(friends);
    }

    /// <summary>Get pending incoming friend requests.</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var requests = await _service.GetPendingRequests(UserId);
        return Ok(requests);
    }

    /// <summary>Get suggested friends.</summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions()
    {
        var suggestions = await _service.GetSuggestions(UserId);
        return Ok(suggestions);
    }

    /// <summary>Send a friend request using user ID.</summary>
    [HttpPost("request/{userId}")]
    public async Task<IActionResult> SendRequestById(string userId)
    {
        var (success, error) = await _service.SendRequestById(UserId, userId);
        return success ? Ok(new { message = "Friend request sent!" }) : BadRequest(new { message = error });
    }
}
