using HomeAssistantGenerated;

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
}