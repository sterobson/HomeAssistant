namespace HomeAssistant.Services;

internal class NotificationService
{
    private readonly IHaContext _ha;

    public NotificationService(IHaContext ha)
    {
        _ha = ha;
    }

    public void SendPersistentNotification(string title, string message)
    {
        SendPersistentNotification(title, message, null);
    }

    public void SendPersistentNotification(string title, string message, string? notificationId)
    {
        _ha.CallService("notify", "persistent_notification", data: new { message, title, notificationId });
    }
}
