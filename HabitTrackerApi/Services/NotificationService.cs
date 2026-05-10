using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace HabitTrackerApi.Services;

/// <summary>
/// Сервис для отправки push-уведомлений через Firebase Cloud Messaging.
/// </summary>
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Отправить push-уведомление конкретному устройству по FCM токену.
    /// </summary>
    public async Task SendAsync(string fcmToken, string title, string body, Dictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(fcmToken))
        {
            _logger.LogWarning("SendAsync: FCM token is empty, skipping notification.");
            return;
        }

        try
        {
            var message = new Message
            {
                Token = fcmToken,
                Notification = new Notification
                {
                    Title = title,
                    Body = body,
                },
                Data = data ?? new Dictionary<string, string>(),
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ChannelId = "flow_channel",
                        Color = "#FF512F",
                        Sound = "default",
                    }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Sound = "default",
                        Badge = 1,
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            _logger.LogInformation("FCM notification sent: {Response}", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send FCM notification to token {Token}", fcmToken[..Math.Min(20, fcmToken.Length)]);
            throw; // Re-throw to inform the client
        }
    }
}
