using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.RateLimiting;
using Google.Apis.Auth;

namespace HabitTrackerApi.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/auth")]
    [ApiVersion("1.0")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly UserManager<ApplicationUser> _userManager; 
        private readonly HabitTrackerApi.Data.AppDbContext _context;
        private readonly EmailService _emailService;
        private readonly NotificationService _notificationService;
        private readonly IMemoryCache _cache;

        public AuthController(
            IConfiguration config, 
            UserManager<ApplicationUser> userManager, 
            HabitTrackerApi.Data.AppDbContext context, 
            EmailService emailService, 
            NotificationService notificationService,
            IMemoryCache cache)
        {
            _userManager = userManager;
            _config = config;
            _context = context;
            _emailService = emailService;
            _notificationService = notificationService;
            _cache = cache;
        }



        [HttpPost("register")]
        [EnableRateLimiting("AuthLimit")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var userExists = await _userManager.FindByNameAsync(dto.UserName);
            var emailExists = await _userManager.FindByEmailAsync(dto.Email);
            if (userExists != null)
                return BadRequest(new { message = "Username already exists." });
            if (emailExists != null)
                return BadRequest(new { message = "Email already in use." });

            var user = new ApplicationUser
            {
                UserName = dto.UserName,
                Email = dto.Email,
                InviteCode = GenerateInviteCode(dto.UserName)
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return BadRequest(new { message = "User creation failed.", errors = result.Errors });

            // Generate email confirmation token and send email
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = Uri.EscapeDataString(token);
                var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var confirmLink = $"{baseUrl}/api/v1/auth/confirm-email?userId={user.Id}&token={encodedToken}";
                await _emailService.SendConfirmationEmailAsync(user.Email!, user.UserName!, confirmLink);
            }
            catch (Exception ex)
            {
                // Don't fail registration if email fails — log it
                Console.WriteLine($"[Email Error] {ex.Message}");
            }

            return Ok(new { message = "Registration successful! Please check your email to confirm your account." });
        }


        /// <summary>
        /// Returns a signed JWT for valid credentials.
        /// </summary>
        [HttpPost("login")]
        [EnableRateLimiting("AuthLimit")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);

            if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
                return Unauthorized(new { message = "Invalid credentials" });

            // Block login if email is not confirmed
            if (!user.EmailConfirmed)
                return Unauthorized(new { message = "email_not_confirmed", email = user.Email });

            if (request.TimeZoneOffset.HasValue)
            {
                user.TimeZoneOffset = request.TimeZoneOffset.Value;
                await _userManager.UpdateAsync(user);
            }

            var token = GenerateToken(user);

            return Ok(new { token });
        }

        [HttpPost("google")]
        [EnableRateLimiting("AuthLimit")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            if (string.IsNullOrEmpty(request.IdToken))
            {
                return BadRequest(new { message = "Google ID token is required." });
            }

            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings();
                var clientIds = _config.GetSection("GoogleAuth:ClientIds").Get<string[]>();
                if (clientIds != null && clientIds.Length > 0)
                {
                    settings.Audience = clientIds;
                }

                var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
                if (payload == null)
                {
                    return Unauthorized(new { message = "Invalid Google token payload." });
                }

                var email = payload.Email;
                if (string.IsNullOrEmpty(email))
                {
                    return BadRequest(new { message = "Email not provided in Google token." });
                }

                var user = await _userManager.FindByEmailAsync(email);
                bool isNewUser = false;

                if (user == null)
                {
                    isNewUser = true;
                    var username = await GenerateUniqueUsernameAsync(email);
                    user = new ApplicationUser
                    {
                        UserName = username,
                        Email = email,
                        EmailConfirmed = true, // Google verified the email address
                        InviteCode = GenerateInviteCode(username)
                    };

                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        return BadRequest(new { message = "User creation failed.", errors = result.Errors });
                    }
                }
                else
                {
                    // If existing user, mark email as confirmed since Google verified it
                    if (!user.EmailConfirmed)
                    {
                        user.EmailConfirmed = true;
                        await _userManager.UpdateAsync(user);
                    }
                }

                if (request.TimeZoneOffset.HasValue)
                {
                    user.TimeZoneOffset = request.TimeZoneOffset.Value;
                    await _userManager.UpdateAsync(user);
                }

                var token = GenerateToken(user);
                return Ok(new { token, isNewUser });
            }
            catch (InvalidJwtException ex)
            {
                Console.WriteLine($"[Google Auth Error] Invalid token: {ex.Message}");
                return Unauthorized(new { message = "Invalid Google token signature or expired." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Google Auth Error] {ex.Message}");
                return StatusCode(500, new { message = "An error occurred during Google authentication." });
            }
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Content(await GetHtmlPageAsync("❌ Invalid Link", "This confirmation link is invalid or has expired."), "text/html");

            var decodedToken = Uri.UnescapeDataString(token);
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

            if (!result.Succeeded)
                return Content(await GetHtmlPageAsync("❌ Confirmation Failed", "This link has already been used or has expired. Please request a new one."), "text/html");

            var safeUserName = System.Net.WebUtility.HtmlEncode(user.UserName);
            return Content(await GetHtmlPageAsync("Email Confirmed!", $"Hi {safeUserName}! Your email has been confirmed. You can now log in to Flow."), "text/html");
        }

        [HttpPost("resend-confirmation")]
        [EnableRateLimiting("AuthLimit")]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationDto dto)
        {
            var cacheKey = $"ResendLimit_{dto.Email}";
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return BadRequest(new { message = "Please wait a minute before requesting another email." });
            }

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                // Set cache even if user doesn't exist to prevent timing attacks
                _cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));
                return Ok(new { message = "If that email exists, we sent a new confirmation link." }); 
            }

            if (user.EmailConfirmed)
                return BadRequest(new { message = "This email is already confirmed." });

            try
            {
                var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = Uri.EscapeDataString(confirmToken);
                var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                var confirmLink = $"{baseUrl}/api/v1/auth/confirm-email?userId={user.Id}&token={encodedToken}";
                await _emailService.SendConfirmationEmailAsync(user.Email!, user.UserName!, confirmLink);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email Error] {ex.Message}");
                return StatusCode(500, new { message = "Failed to send email. Please try again later." });
            }

            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));
            return Ok(new { message = "Confirmation email sent. Please check your inbox." });
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("AuthLimit")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var cacheKey = $"ForgotLimit_{dto.Email}";
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return BadRequest(new { message = "Please wait a minute before requesting another password reset." });
            }

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null || !await _userManager.IsEmailConfirmedAsync(user))
            {
                _cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));
                // Don't reveal that the user does not exist or is not confirmed
                return Ok(new { message = "If your email is registered and confirmed, a password reset link has been sent." });
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            var resetLink = $"{baseUrl}/api/v1/auth/reset-password-page?email={Uri.EscapeDataString(user.Email!)}&token={encodedToken}";

            try
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email!, user.UserName!, resetLink);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email Error] {ex.Message}");
                return StatusCode(500, new { message = "Failed to send reset email. Please try again later." });
            }

            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));
            return Ok(new { message = "If your email is registered and confirmed, a password reset link has been sent." });
        }

        [HttpGet("reset-password-page")]
        public IActionResult ResetPasswordPage([FromQuery] string email, [FromQuery] string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return BadRequest("Invalid password reset link.");

            string html = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                  <meta charset="UTF-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1.0">
                  <title>Reset Password - Flow</title>
                  <style>
                    body { 
                      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; 
                      background: #121212; 
                      color: #E8EBE9; 
                      display: flex; 
                      justify-content: center; 
                      align-items: center; 
                      height: 100vh; 
                      margin: 0; 
                    }
                    .card { 
                      background: #1A1A1A; 
                      padding: 48px; 
                      border-radius: 24px; 
                      border: 1px solid #2A2A2A; 
                      width: 100%; 
                      max-width: 400px; 
                      box-sizing: border-box; 
                    }
                    h2 { 
                      font-weight: 400; 
                      text-align: center; 
                      margin-bottom: 32px; 
                      color: #F5F5F5;
                    }
                    label { 
                      display: block; 
                      margin-bottom: 8px; 
                      font-size: 14px; 
                      color: #9CA3AF; 
                    }
                    input { 
                      width: 100%; 
                      padding: 16px; 
                      margin-bottom: 24px; 
                      border-radius: 16px; 
                      border: 1px solid #2A2A2A; 
                      background: #121212; 
                      color: #E8EBE9; 
                      box-sizing: border-box; 
                      font-size: 16px;
                    }
                    button { 
                      width: 100%; 
                      padding: 16px; 
                      background: #B4A9C8; /* Lavender accent */
                      color: #151515; 
                      border: none; 
                      border-radius: 40px; 
                      font-size: 16px; 
                      font-weight: 500; 
                      cursor: pointer; 
                      transition: opacity 0.2s;
                    }
                    button:hover { opacity: 0.9; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <h2>Reset Password</h2>
                    <form action="/api/v1/auth/reset-password" method="post">
                      <input type="hidden" name="email" value="{{email}}">
                      <input type="hidden" name="token" value="{{token}}">
                      <label>New Password</label>
                      <input type="password" name="newPassword" required minlength="6" placeholder="••••••••">
                      <button type="submit">Update Password</button>
                    </form>
                  </div>
                </body>
                </html>
                """;

            return Content(html, "text/html");
        }

        [HttpPost("reset-password")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ResetPassword([FromForm] string email, [FromForm] string token, [FromForm] string newPassword)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return Content(await GetHtmlPageAsync("❌ Error", "Invalid user."), "text/html");

            string decodedToken;
            try
            {
                var decodedTokenBytes = WebEncoders.Base64UrlDecode(token);
                decodedToken = Encoding.UTF8.GetString(decodedTokenBytes);
            }
            catch
            {
                return Content(await GetHtmlPageAsync("❌ Error", "Invalid token format."), "text/html");
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, newPassword);

            if (!result.Succeeded)
            {
                string errors = string.Join("<br>", result.Errors.Select(e => System.Net.WebUtility.HtmlEncode(e.Description)));
                return Content(await GetHtmlPageAsync("❌ Reset Failed", $"Failed to reset password:<br><br>{errors}"), "text/html");
            }

            return Content(await GetHtmlPageAsync("✅ Password Reset!", "Your password has been reset successfully. You can now return to the app and log in with your new password."), "text/html");
        }

        private async Task<string> GetHtmlPageAsync(string title, string message)
        {
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "email_confirmation_result.html");
            if (!System.IO.File.Exists(templatePath))
            {
                // Fallback basic HTML if template is missing
                return $"<html><body><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1><p>{message}</p></body></html>";
            }

            var html = await System.IO.File.ReadAllTextAsync(templatePath);
            var icon = title.StartsWith("❌") ? "⚠️" : "✅";
            
            return html
                .Replace("{{title}}", System.Net.WebUtility.HtmlEncode(title))
                .Replace("{{icon}}", System.Net.WebUtility.HtmlEncode(icon))
                .Replace("{{message}}", message);
        }

        [HttpPost("edit")]
        [Authorize]
        public async Task<IActionResult> EditProfile([FromBody] EditProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId!);
            if (user == null) return NotFound();

            // 1. Update Email
            bool emailChanged = false;
            if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.Email)
            {
                if (!await _userManager.HasPasswordAsync(user))
                {
                    return BadRequest(new { message = "Email cannot be changed for external accounts." });
                }

                var existingEmail = await _userManager.FindByEmailAsync(dto.Email);
                if (existingEmail != null)
                    return BadRequest(new { message = "Email already in use." });

                user.Email = dto.Email;
                user.EmailConfirmed = false; // Сбрасываем подтверждение
                emailChanged = true;
            }

            // 2. Update UserName (with uniqueness check)
            if (!string.IsNullOrEmpty(dto.UserName) && dto.UserName != user.UserName)
            {
                var existing = await _userManager.FindByNameAsync(dto.UserName);
                if (existing != null) 
                    return BadRequest(new { message = "Username already taken." });
                
                user.UserName = dto.UserName;
            }

            // 3. Update Password (securely via ChangePasswordAsync)
            if (!string.IsNullOrEmpty(dto.NewPassword))
            {
                if (string.IsNullOrEmpty(dto.CurrentPassword))
                    return BadRequest(new { message = "Current password is required to change password." });

                var passResult = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
                if (!passResult.Succeeded)
                    return BadRequest(new { message = "Password update failed.", errors = passResult.Errors });
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { message = "Profile update failed.", errors = result.Errors });

            if (emailChanged)
            {
                try
                {
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    var encodedToken = Uri.EscapeDataString(token);
                    var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                    var confirmLink = $"{baseUrl}/api/v1/auth/confirm-email?userId={user.Id}&token={encodedToken}";
                    await _emailService.SendConfirmationEmailAsync(user.Email!, user.UserName!, confirmLink);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Email Error] {ex.Message}");
                }

                return Ok(new { 
                    message = "Email updated. Please check your new inbox to confirm it.",
                    emailChanged = true 
                });
            }

            return Ok(new { message = "Profile updated successfully." });
        }

        [HttpGet("achievements")]
        [Authorize]
        public async Task<IActionResult> GetAchievements()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var achievements = await _context.UserAchievements
                .Where(a => a.UserId == userId)
                .Select(a => new {
                    Type = a.Type.ToString(),
                    UnlockedAt = a.UnlockedAt
                })
                .ToListAsync();

            return Ok(achievements);
        }


        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // ── Lazy Update: Sync IsPro with Expiration ───────────────────────
            bool isExpired = user.IsPro && user.ProExpiration != null && user.ProExpiration < DateTime.UtcNow;
            if (isExpired)
            {
                user.IsPro = false;
                await _userManager.UpdateAsync(user);
            }

            bool isCurrentlyPro = user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

            return Ok(new UserDto
            {
                Id = user.Id,
                UserName = user.UserName!,
                Email = user.Email!,
                JoinedAt = user.JoinedAt,
                IsPro = isCurrentlyPro,
                HasPassword = await _userManager.HasPasswordAsync(user)
            });
        }
        
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            bool isVerified = false;

            if (await _userManager.HasPasswordAsync(user))
            {
                if (!string.IsNullOrEmpty(request.Password) && await _userManager.CheckPasswordAsync(user, request.Password))
                {
                    isVerified = true;
                }
            }
            else
            {
                // Google user (no password) - verify Google ID Token
                if (!string.IsNullOrEmpty(request.IdToken))
                {
                    try
                    {
                        var settings = new GoogleJsonWebSignature.ValidationSettings();
                        var clientIds = _config.GetSection("GoogleAuth:ClientIds").Get<string[]>();
                        if (clientIds != null && clientIds.Length > 0)
                        {
                            settings.Audience = clientIds;
                        }

                        var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
                        if (payload != null && payload.Email == user.Email)
                        {
                            isVerified = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Delete Account Error] Google token verification failed: {ex.Message}");
                    }
                }
            }

            if (!isVerified)
            {
                return Unauthorized(new { message = "Authentication failed. Incorrect credentials." });
            }

            // Сначала удаляем все связанные данные (Friendships, Habits, Achievements),
            // чтобы избежать ошибки Foreign Key constraint (500 Server Error)
            var friendships = _context.Friendships
                .Where(f => f.RequesterId == user.Id || f.AddresseeId == user.Id);
            _context.Friendships.RemoveRange(friendships);

            var habits = _context.Habits.IgnoreQueryFilters()
                .Where(h => h.UserId == user.Id);
            _context.Habits.RemoveRange(habits);

            var achievements = _context.UserAchievements
                .Where(a => a.UserId == user.Id);
            _context.UserAchievements.RemoveRange(achievements);

            await _context.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { message = "Account deletion failed.", errors = result.Errors });
            return Ok(new { message = "Account deleted successfully." });
        }

        [HttpPost("upgrade-to-pro")]
        [Authorize]
        public async Task<IActionResult> UpgradeToPro([FromBody] UpgradeRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // Если подписка уже есть и она активна, будем продлевать от текущей даты окончания, 
            // либо от сейчас, если она истекла.
            DateTime startDate = (user.ProExpiration != null && user.ProExpiration > DateTime.UtcNow) 
                ? user.ProExpiration.Value 
                : DateTime.UtcNow;

            if (request.Plan.ToLower() == "monthly")
            {
                user.ProExpiration = startDate.AddMonths(1);
            }
            else if (request.Plan.ToLower() == "yearly")
            {
                user.ProExpiration = startDate.AddYears(1);
            }
            else
            {
                return BadRequest(new { message = "Invalid plan type. Use 'monthly' or 'yearly'." });
            }

            user.IsPro = true;
            await _userManager.UpdateAsync(user);

            return Ok(new { 
                message = $"Successfully subscribed to {request.Plan}!", 
                proUntil = user.ProExpiration,
                isPro = true 
            });
        }

        [HttpPost("cancel-pro")]
        [Authorize]
        public async Task<IActionResult> CancelPro()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            if (!user.IsPro)
                return BadRequest(new { message = "You are not a Pro user." });

            user.IsPro = false;
            // Optionally, we could set ProExpiration to DateTime.UtcNow so the UI immediately sees it as expired
            user.ProExpiration = DateTime.UtcNow.AddMinutes(-1);
            
            await _userManager.UpdateAsync(user);

            return Ok(new { message = "Pro plan cancelled successfully.", isPro = false });
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private string GenerateToken(ApplicationUser user)
        {
            var jwtSection = _config.GetSection("Jwt");

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["Key"]!));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id), // 🔥 КЛЮЧЕВОЕ
                new Claim(ClaimTypes.Name, user.UserName!)
            };

            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: jwtSection["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(365), // Увеличено с 8 часов до 1 года для мобильного приложения
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<string> GenerateUniqueUsernameAsync(string email)
        {
            var baseUsername = email.Split('@')[0];
            
            // Clean up to only contain alphanumeric, underscore, dot, hyphen
            baseUsername = new string(baseUsername.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-').ToArray());
            
            if (string.IsNullOrEmpty(baseUsername))
            {
                baseUsername = "user";
            }

            var username = baseUsername;
            var suffix = 1;
            
            while (await _userManager.FindByNameAsync(username) != null)
            {
                username = $"{baseUsername}{suffix}";
                suffix++;
            }
            
            return username;
        }

        private static string GenerateInviteCode(string username)
        {
            var prefix = username.Length >= 4
                ? username[..4].ToUpper()
                : username.ToUpper().PadRight(4, 'X');
            var suffix = Guid.NewGuid().ToString("N")[..4].ToUpper();
            return $"{prefix}-{suffix}";
        }

        /// <summary>
        /// Saves or updates the device FCM token for push notifications.
        /// </summary>
        [Authorize]
        [HttpPost("fcm-token")]
        public async Task<IActionResult> UpdateFcmToken([FromBody] FcmTokenDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) return Unauthorized();

            // First, remove this token from any OTHER users (prevent notifications for previous accounts)
            var otherUsersWithToken = await _userManager.Users
                .Where(u => u.FcmToken == dto.Token && u.Id != userId)
                .ToListAsync();

            foreach (var otherUser in otherUsersWithToken)
            {
                otherUser.FcmToken = null;
                await _userManager.UpdateAsync(otherUser);
            }

            user.FcmToken = dto.Token;
            await _userManager.UpdateAsync(user);

            return Ok();
        }

        /// <summary>
        /// Removes the device FCM token on logout.
        /// </summary>
        [Authorize]
        [HttpDelete("fcm-token")]
        public async Task<IActionResult> RemoveFcmToken()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) return Unauthorized();

            user.FcmToken = null;
            await _userManager.UpdateAsync(user);

            return Ok();
        }

        /// <summary>Updates the user's habit reminder settings.</summary>
        [Authorize]
        [HttpPut("notification-settings")]
        public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();

            user.ReminderEnabled = dto.Enabled;
            user.ReminderFrequency = dto.Frequency;
            await _userManager.UpdateAsync(user);
            return Ok();
        }

        /// <summary>Returns current notification settings.</summary>
        [Authorize]
        [HttpGet("notification-settings")]
        public async Task<IActionResult> GetNotificationSettings()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Unauthorized();
            return Ok(new { enabled = user.ReminderEnabled, frequency = user.ReminderFrequency });
        }

        [Authorize]
        [HttpPost("test-notification")]
        public async Task<IActionResult> TestNotification()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) return Unauthorized();
            if (string.IsNullOrEmpty(user.FcmToken))
                return BadRequest(new { message = "No device token registered. Please grant notification permissions in the app." });

            await _notificationService.SendAsync(
                user.FcmToken, 
                "Test Notification", 
                "Everything is working! This is how your habit reminders will look.",
                new Dictionary<string, string> { { "type", "test" } }
            );

            return Ok(new { message = "Test notification sent!" });
        }

        [Authorize]
        [HttpPost("test-ai-notification")]
        public async Task<IActionResult> TestAiNotification([FromServices] AiService aiService)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) return Unauthorized();
            if (string.IsNullOrEmpty(user.FcmToken))
                return BadRequest(new { message = "No device token registered. Please grant notification permissions in the app." });

            var today = DateTime.UtcNow.Date;
            var incompleteHabits = await _context.Habits
                .Where(h => h.UserId == user.Id && !h.IsDeleted)
                .Where(h => !h.Logs.Any(l => l.Date == today && l.IsCompleted))
                .Select(h => new { h.Id, h.Title })
                .ToListAsync();

            if (incompleteHabits.Count == 0)
            {
                return BadRequest(new { message = "You have completed all habits for today! Add an incomplete habit to test this feature." });
            }

            var habitTitles = incompleteHabits.Select(h => h.Title).ToList();

            string title = "AI Coach Message";
            string prompt = $"Uncompleted habits: {string.Join(", ", habitTitles)}";
            string body = await aiService.GenerateNotificationMessageAsync(prompt);

            var topHabits = incompleteHabits.Take(3).ToList();
            var habitIdsString = string.Join(",", topHabits.Select(h => h.Id.ToString()));
            var habitTitlesString = string.Join(",", topHabits.Select(h => h.Title));

            await _notificationService.SendAsync(
                user.FcmToken, 
                title, 
                body,
                new Dictionary<string, string> 
                { 
                    { "type", "reminder" },
                    { "habitIds", habitIdsString },
                    { "habitTitles", habitTitlesString }
                }
            );

            return Ok(new { message = "AI notification sent!", generatedBody = body });
        }
        
        [Authorize]
        [HttpPost("usage")]
        public async Task<IActionResult> UpdateUsage([FromBody] AppUsageDto request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            if (!DateTime.TryParse(request.Date, out var dateParsed))
            {
                return BadRequest("Invalid date format.");
            }

            var dateOnly = dateParsed.Date;

            var usage = await _context.AppUsages
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Date == dateOnly);

            if (usage != null)
            {
                usage.SecondsSpent = request.SecondsSpent;
            }
            else
            {
                usage = new AppUsage
                {
                    UserId = userId,
                    Date = dateOnly,
                    SecondsSpent = request.SecondsSpent
                };
                _context.AppUsages.Add(usage);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Usage updated" });
        }
        
        [Authorize]
        [EnableRateLimiting("AiLimit")]
        [HttpPost("generate-routine")]
        public async Task<IActionResult> GenerateRoutine([FromBody] GenerateRoutineRequest request, [FromServices] AiService aiService)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound("User not found");

            // Limits logic
            const int FreeTotalLimit = 2;
            const int ProDailyLimit = 5;

            bool isCurrentlyPro = user.IsPro && (user.ProExpiration == null || user.ProExpiration > DateTime.UtcNow);

            if (isCurrentlyPro)
            {
                // Daily limit for Pro
                var today = DateTime.UtcNow.Date;
                if (user.LastAiGenerationDate?.Date != today)
                {
                    user.AiGenerationsCount = 0;
                    user.LastAiGenerationDate = today;
                }

                if (user.AiGenerationsCount >= ProDailyLimit)
                {
                    return BadRequest(new { code = "LimitReached", message = "Daily generation limit reached for Pro users." });
                }
            }
            else
            {
                // Total limit for Free
                if (user.AiGenerationsCount >= FreeTotalLimit)
                {
                    return BadRequest(new { code = "ProRequired", message = "Free generation limit reached. Upgrade to Pro for daily generations!" });
                }
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return BadRequest("Prompt cannot be empty.");
            }

            try
            {
                var habits = await aiService.GenerateHabitRoutineAsync(request.Prompt);

                // Increment and save - using a more direct approach to ensure persistence
                user.AiGenerationsCount++;
                user.LastAiGenerationDate = DateTime.UtcNow;
                
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                int remaining = isCurrentlyPro ? (ProDailyLimit - user.AiGenerationsCount) : (FreeTotalLimit - user.AiGenerationsCount);

                return Ok(new { 
                    habits = habits, 
                    aiGenerationsRemaining = remaining,
                    isPro = isCurrentlyPro,
                    aiTotalLimit = isCurrentlyPro ? ProDailyLimit : FreeTotalLimit
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to generate routine", error = ex.Message });
            }
        }
    }

    // ── request DTO ──────────────────────────────────────────────────────────

    public record GoogleLoginRequest(string IdToken, int? TimeZoneOffset);
    public record DeleteAccountRequest(string Username, string? Password, string? IdToken);
    public record UpgradeRequest(string Plan);
    public record LoginRequest(string Username, string Password, int? TimeZoneOffset);
    public record FcmTokenDto(string Token);
    public record NotificationSettingsDto(bool Enabled, int Frequency);
    public record ForgotPasswordDto(string Email);
    public record AppUsageDto(string Date, int SecondsSpent);
    public record GenerateRoutineRequest(string Prompt);
}
