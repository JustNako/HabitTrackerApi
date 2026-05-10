using HabitTrackerApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace HabitTrackerApi.Controllers;

/// <summary>
/// Receives webhook events from RevenueCat and updates user Pro status.
/// Docs: https://www.revenuecat.com/docs/webhooks
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/webhooks")]
[ApiVersion("1.0")]
public class WebhooksController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger<WebhooksController> logger)
    {
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    [HttpPost("revenuecat")]
    public async Task<IActionResult> RevenueCatWebhook()
    {
        // 1. Verify the shared secret so random people can't fake events
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        var expectedSecret = _config["AppConfig:RevenueCatWebhookSecret"] ?? "change_me_in_production";

        if (authHeader != expectedSecret)
        {
            _logger.LogWarning("RevenueCat webhook received with invalid secret.");
            return Unauthorized();
        }

        // 2. Read the raw body
        string body;
        using (var reader = new System.IO.StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse RevenueCat webhook body.");
            return BadRequest();
        }

        if (!root.TryGetProperty("event", out var eventObj))
            return Ok(); // not a valid event structure, ignore

        var eventType = eventObj.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
        var appUserId = eventObj.TryGetProperty("app_user_id", out var uidProp) ? uidProp.GetString() : null;
        var expiresAtMs = eventObj.TryGetProperty("expiration_at_ms", out var expProp) && expProp.ValueKind == JsonValueKind.Number
            ? expProp.GetInt64() : (long?)null;

        _logger.LogInformation("RevenueCat event: {Type} for user {UserId}", eventType, appUserId);

        if (string.IsNullOrEmpty(appUserId)) return Ok();

        var user = await _userManager.FindByIdAsync(appUserId);
        if (user == null)
        {
            _logger.LogWarning("RevenueCat webhook: user {UserId} not found.", appUserId);
            return Ok(); // Return 200 anyway so RevenueCat doesn't retry endlessly
        }

        switch (eventType)
        {
            // ── Active subscription events ─────────────────────────────────
            case "INITIAL_PURCHASE":
            case "RENEWAL":
            case "PRODUCT_CHANGE":
            case "UNCANCELLATION":
                user.IsPro = true;
                user.ProExpiration = expiresAtMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs.Value).UtcDateTime
                    : DateTime.UtcNow.AddMonths(1);
                _logger.LogInformation("User {UserId} upgraded to Pro until {Exp}", user.Id, user.ProExpiration);
                break;

            // ── Inactive subscription events ───────────────────────────────
            case "CANCELLATION":
            case "EXPIRATION":
            case "BILLING_ISSUE":
                user.IsPro = false;
                user.ProExpiration = DateTime.UtcNow;
                _logger.LogInformation("User {UserId} Pro access revoked (event: {Type})", user.Id, eventType);
                break;

            default:
                // Unknown event type — just ignore it
                return Ok();
        }

        await _userManager.UpdateAsync(user);
        return Ok();
    }
}
