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
            // We send title/body in Data payload for Android so Flutter can construct an interactive local notification.
            var payloadData = data ?? new Dictionary<string, string>();
            payloadData["title"] = title;
            payloadData["body"] = body;

            var isReminder = data != null && data.TryGetValue("type", out var type) && type == "reminder";

            var message = new Message
            {
                Token = fcmToken,
                Data = payloadData,
                Notification = isReminder ? null : new Notification
                {
                    Title = title,
                    Body = body
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Alert = new ApsAlert
                        {
                            Title = title,
                            Body = body
                        },
                        Sound = "default",
                        Badge = 1,
                        Category = "HABIT_ACTION"
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
