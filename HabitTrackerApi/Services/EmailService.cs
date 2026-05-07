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
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0C1017; margin: 0; padding: 40px 20px; }
                    .card { max-width: 480px; margin: 0 auto; background: #161A22; border-radius: 20px; padding: 40px; border: 1px solid #262A34; }
                    .logo { text-align: center; font-size: 28px; font-weight: 700; color: #fff; margin-bottom: 8px; }
                    .logo span { background: linear-gradient(135deg, #DA22FF, #9733EE); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
                    .title { text-align: center; color: #fff; font-size: 22px; font-weight: 600; margin: 24px 0 12px; }
                    .subtitle { text-align: center; color: #8A8F9E; font-size: 15px; line-height: 1.6; margin-bottom: 32px; }
                    .button { display: block; background: linear-gradient(135deg, #DA22FF, #9733EE); color: #fff; text-decoration: none; text-align: center; padding: 16px 32px; border-radius: 14px; font-size: 16px; font-weight: 600; margin: 0 auto; }
                    .footer { text-align: center; color: #4A4F5E; font-size: 12px; margin-top: 32px; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="logo"><span>Flow</span></div>
                    <div class="title">Confirm your email</div>
                    <div class="subtitle">Hi {{userName}}!<br>Click the button below to confirm your account and start building great habits.</div>
                    <a href="{{confirmationLink}}" class="button">Confirm Email</a>
                    <div class="footer">This link expires in 24 hours. If you didn't create a Flow account, you can ignore this email.</div>
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
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0C1017; margin: 0; padding: 40px 20px; }
                    .card { max-width: 480px; margin: 0 auto; background: #161A22; border-radius: 20px; padding: 40px; border: 1px solid #262A34; }
                    .logo { text-align: center; font-size: 28px; font-weight: 700; color: #fff; margin-bottom: 8px; }
                    .logo span { background: linear-gradient(135deg, #DA22FF, #9733EE); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
                    .title { text-align: center; color: #fff; font-size: 22px; font-weight: 600; margin: 24px 0 12px; }
                    .subtitle { text-align: center; color: #8A8F9E; font-size: 15px; line-height: 1.6; margin-bottom: 32px; }
                    .button { display: block; background: linear-gradient(135deg, #DA22FF, #9733EE); color: #fff; text-decoration: none; text-align: center; padding: 16px 32px; border-radius: 14px; font-size: 16px; font-weight: 600; margin: 0 auto; }
                    .footer { text-align: center; color: #4A4F5E; font-size: 12px; margin-top: 32px; }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="logo"><span>Flow</span></div>
                    <div class="title">Reset Password</div>
                    <div class="subtitle">Hi {{userName}}!<br>Click the button below to reset your password. If you didn't request this, you can safely ignore this email.</div>
                    <a href="{{resetLink}}" class="button">Reset Password</a>
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
