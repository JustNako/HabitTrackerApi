using Microsoft.AspNetCore.Mvc;

namespace HabitTrackerApi.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/app")]
[ApiVersion("1.0")]
public class AppConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public AppConfigController(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns the latest app version, store links, and runtime SDK keys.
    /// </summary>
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var section = _config.GetSection("AppConfig");
        return Ok(new {
            latestVersion = section["LatestVersion"] ?? "1.0.0",
            minRequiredVersion = section["MinRequiredVersion"] ?? "1.0.0",
            updateUrlAndroid = section["UpdateUrlAndroid"] ?? "https://play.google.com/store/apps/details?id=com.codex.flow",
            updateUrlIOS = section["UpdateUrlIOS"] ?? "https://apps.apple.com/app/id123456789",
            revenueCatApiKeyAndroid = section["RevenueCatApiKeyAndroid"] ?? "",
            revenueCatApiKeyIOS = section["RevenueCatApiKeyIOS"] ?? ""
        });
    }
}
