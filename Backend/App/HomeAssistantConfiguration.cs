namespace HomeAssistant;

public class HomeAssistantConfiguration
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string Token { get; set; } = string.Empty;
    public Guid HouseId { get; set; } = Guid.Empty;
}