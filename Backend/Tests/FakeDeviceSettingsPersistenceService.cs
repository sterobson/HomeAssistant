using HomeAssistant.Services;
using HomeAssistant.Shared;

namespace HomeAssistant.Tests;

/// <summary>
/// Stands in for the real device settings cache. Settings can be changed between evaluations,
/// and <see cref="RaiseSettingsUpdatedAsync"/> stands in for the "device-settings-changed"
/// SignalR notification.
/// </summary>
internal class FakeDeviceSettingsPersistenceService : IDeviceSettingsPersistenceService
{
    public event Func<Task>? SettingsUpdated;

    public DeviceSettingsDto Settings { get; set; } = new();

    public int GetSettingsCallCount { get; private set; }

    /// <summary>
    /// Simulates the API and the local cache both being unreachable.
    /// </summary>
    public bool ThrowOnGet { get; set; }

    public void SetBoilerControlPath(BoilerControlPath path)
    {
        Settings = new DeviceSettingsDto
        {
            Heating = new HeatingSettingsDto { BoilerControlPath = path }
        };
    }

    public Task<DeviceSettingsDto> GetSettingsAsync()
    {
        GetSettingsCallCount++;

        if (ThrowOnGet)
        {
            throw new InvalidOperationException("Device settings are unavailable");
        }

        return Task.FromResult(Settings);
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task RaiseSettingsUpdatedAsync()
    {
        return SettingsUpdated?.Invoke() ?? Task.CompletedTask;
    }
}
