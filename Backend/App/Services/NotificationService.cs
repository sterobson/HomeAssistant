using HomeAssistantGenerated;
using System.Collections.Generic;
using System.Linq;

namespace HomeAssistant.Services;

internal class NotificationService
{
    private readonly IHaContext _ha;
    private readonly NotifyServices _notifyServices;

    public NotificationService(IHaContext ha, NotifyServices notifyServices)
    {
        _ha = ha;
        _notifyServices = notifyServices;
    }

    public void SendPersistentNotification(string title, string message)
    {
        SendPersistentNotification(title, message, null);
    }

    public void SendPersistentNotification(string title, string message, string? notificationId)
    {
        _ha.CallService("notify", "persistent_notification", data: new { message, title, notificationId });
    }

    public void SendNotificationToStePhone(string title, string message)
    {
        _notifyServices.MobileAppSmA546b(message, title);
    }

    public void SendNotificationToRuthPhone(string title, string message)
    {
        _notifyServices.MobileAppRuthgalaxya55(message, title);
    }

    public void SendNotificationToKevinPhone(string title, string message)
    {
        _notifyServices.MobileAppKevinsIphone(message, title);
    }

    public const string GroupRuth = "ruth";
    public const string GroupSte = "ste";
    public const string GroupRobson = "robson";
    public const string GroupKevin = "kevin";
    public const string GroupBlount = "blount";
    public const string GroupAll = "all";

    public void SendNotificationToGroups(string title, string message, params string[] groups)
    {
        List<string> trimmedLower = [.. groups.Select(g => g.Trim().ToLower())];

        if (trimmedLower.Intersect([GroupSte, GroupRobson, GroupAll]).Any())
        {
            SendNotificationToStePhone(message, title);
        }

        if (trimmedLower.Intersect([GroupRuth, GroupRobson, GroupAll]).Any())
        {
            SendNotificationToRuthPhone(message, title);
        }

        if (trimmedLower.Intersect([GroupKevin, GroupBlount, GroupAll]).Any())
        {
            SendNotificationToKevinPhone(message, title);
        }
    }


}