using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HabitTrackerApi.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendConfirmationEmailAsync(string toEmail, string userName, string confirmationLink)
    {
        var emailSection = _config.GetSection("Email");
        var from = emailSection["From"] ?? "no-reply@flowtracks.app";
        var smtpHost = emailSection["SmtpHost"] ?? "smtp.resend.com";
        var smtpPort = int.Parse(emailSection["SmtpPort"] ?? "587");
        var username = emailSection["Username"] ?? "resend";
        var password = emailSection["Password"]!;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Flow", from));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Confirm your Flow account";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                  <meta charset="UTF-8">
                  <style>
                    /* Typography: Fallback to system sans-serif for body, but prioritizing serif for headings if available */
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #121212; margin: 0; padding: 40px 20px; }
                    
                    /* The Bento Card: Matte charcoal background, ultra-rounded borders */
                    .card { max-width: 480px; margin: 0 auto; background: #1A1A1A; border-radius: 32px; padding: 48px 40px; border: 1px solid #2A2A2A; box-shadow: 0 10px 40px rgba(0,0,0,0.3); }
                    
                    /* Logo & Title: Elegant, breathing serif aesthetic */
                    .logo { text-align: center; font-family: 'Lora', 'Playfair Display', Georgia, serif; font-size: 26px; font-weight: 500; color: #E8EBE9; letter-spacing: 0.5px; }
                    .title { text-align: center; font-family: 'Lora', 'Playfair Display', Georgia, serif; color: #F5F5F5; font-size: 24px; font-weight: 400; margin: 24px 0 16px; }
                    
                    /* Muted, empathetic copy */
                    .subtitle { text-align: center; color: #9CA3AF; font-size: 16px; font-weight: 300; line-height: 1.6; margin-bottom: 40px; }
                    
                    /* Main CTA: Plush pill-shaped button in soft Sage Green */
                    .button { display: inline-block; background: #96B0A0; color: #151515; text-decoration: none; text-align: center; padding: 16px 36px; border-radius: 40px; font-size: 16px; font-weight: 500; margin: 0 auto; width: max-content; }
                    .button-container { text-align: center; }
                    
                    /* Gentle footer */
                    .footer { text-align: center; color: #6B7280; font-size: 13px; font-weight: 300; margin-top: 40px; line-height: 1.5; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="logo">Flow</div>
                    <div class="title">Begin your journey</div>
                    <div class="subtitle">Welcome to your space, {{userName}}.<br><br>Take a deep breath. Click the button below to confirm your email and step into a calmer routine.</div>
                    <div class="button-container">
                      <a href="{{confirmationLink}}" class="button">Confirm Email</a>
                    </div>
                    <div class="footer">This link expires in 24 hours.<br>If you didn't create an account, you can safely ignore this message.</div>
                  </div>
                </body>
                </html>
                """
        };

        message.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Confirmation email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to {Email}", toEmail);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        var emailSection = _config.GetSection("Email");
        var from = emailSection["From"] ?? "no-reply@flowtracks.app";
        var smtpHost = emailSection["SmtpHost"] ?? "smtp.resend.com";
        var smtpPort = int.Parse(emailSection["SmtpPort"] ?? "587");
        var username = emailSection["Username"] ?? "resend";
        var password = emailSection["Password"]!;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Flow", from));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Reset your Flow password";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                  <meta charset="UTF-8">
                  <style>
                    /* Typography: System sans-serif for body, elegant serif for headings */
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #121212; margin: 0; padding: 40px 20px; }
                    
                    /* The Bento Card: Matte charcoal background, ultra-rounded borders */
                    .card { max-width: 480px; margin: 0 auto; background: #1A1A1A; border-radius: 32px; padding: 48px 40px; border: 1px solid #2A2A2A; box-shadow: 0 10px 40px rgba(0,0,0,0.3); }
                    
                    /* Logo & Title: Breathing serif aesthetic */
                    .logo { text-align: center; font-family: 'Lora', 'Playfair Display', Georgia, serif; font-size: 26px; font-weight: 500; color: #E8EBE9; letter-spacing: 0.5px; }
                    .title { text-align: center; font-family: 'Lora', 'Playfair Display', Georgia, serif; color: #F5F5F5; font-size: 24px; font-weight: 400; margin: 24px 0 16px; }
                    
                    /* Muted, empathetic copy */
                    .subtitle { text-align: center; color: #9CA3AF; font-size: 16px; font-weight: 300; line-height: 1.6; margin-bottom: 40px; }
                    
                    /* Main CTA: Plush pill-shaped button in Soft Lavender for recovery */
                    .button { display: inline-block; background: #B4A9C8; color: #151515; text-decoration: none; text-align: center; padding: 16px 36px; border-radius: 40px; font-size: 16px; font-weight: 500; margin: 0 auto; width: max-content; }
                    .button-container { text-align: center; }
                    
                    /* Gentle footer */
                    .footer { text-align: center; color: #6B7280; font-size: 13px; font-weight: 300; margin-top: 40px; line-height: 1.5; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="logo">Flow</div>
                    <div class="title">Find your way back</div>
                    <div class="subtitle">Breathe, it happens, {{userName}}.<br><br>Click the button below to quietly reset your password and return to your routine.</div>
                    <div class="button-container">
                      <a href="{{resetLink}}" class="button">Reset Password</a>
                    </div>
                    <div class="footer">If you didn't request a reset, you can safely ignore this message.<br>Your sanctuary remains secure.</div>
                  </div>
                </body>
                </html>
                """
        };

        message.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Password reset email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw;
        }
    }
}
