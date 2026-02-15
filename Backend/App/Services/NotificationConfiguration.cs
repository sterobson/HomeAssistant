using System.Collections.Generic;

namespace HomeAssistant.Services;

internal class NotificationConfiguration
{
    public Dictionary<string, string> Recipients { get; set; } = new();
    public Dictionary<string, List<string>> Groups { get; set; } = new();
}
