using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.Services;

internal class NotificationService
{
    private readonly IHaContext _ha;
    private readonly NotificationConfiguration _config;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IHaContext ha, NotificationConfiguration config, ILogger<NotificationService> logger)
    {
        _ha = ha;
        _config = config;
        _logger = logger;
    }

    public void SendPersistentNotification(string title, string message)
    {
        SendPersistentNotification(title, message, null);
    }

    public void SendPersistentNotification(string title, string message, string? notificationId)
    {
        _ha.CallService("notify", "persistent_notification", data: new { message, title, notificationId });
    }

    public void SendNotificationToGroups(string title, string message, params string[] groups)
    {
        HashSet<string> recipients = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in groups)
        {
            string key = name.Trim().ToLowerInvariant();

            if (_config.Groups.TryGetValue(key, out List<string>? members))
            {
                foreach (string member in members)
                {
                    recipients.Add(member);
                }
            }
            else if (_config.Recipients.ContainsKey(key))
            {
                recipients.Add(key);
            }
            else
            {
                _logger.LogWarning("Unrecognised notification recipient or group: {Name}", key);
            }
        }

        foreach (string recipient in recipients)
        {
            if (_config.Recipients.TryGetValue(recipient, out string? serviceName))
            {
                _ha.CallService("notify", serviceName, data: new { message, title });
            }
            else
            {
                _logger.LogWarning("Recipient '{Recipient}' is referenced in a group but not defined in Recipients", recipient);
            }
        }
    }
}
