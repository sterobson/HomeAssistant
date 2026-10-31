using HomeAssistant.Services.HouseDevices;
using NetDaemon.Client;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// When each room thermometer was last heard from (owner, 2026-10-31, after Ste's desk thermometer went silent at 12:57 and
/// the app read its last 19.2 °C for over three hours, keeping the desk heater on in a 22 °C room: HOME-ASSISTANT.md
/// gotcha 32). HA keeps a silent battery device's last value and calls it available for hours, and NetDaemon sees only
/// changes of value, so ZHA is asked directly.
/// </summary>
internal interface IThermometerReports
{
    /// <summary>When the thermometer was last heard from, or null when that cannot be told (it is then taken as working).</summary>
    Task<DateTimeOffset?> LastHeardFromAsync(string entityId);
}

/// <summary>
/// Reads each thermometer's Zigbee device's <c>last_seen</c> from ZHA (<c>zha/devices</c>), at most once per
/// <see cref="CacheFor"/>. That is any message from the device, so it moves every few minutes on a working one. The state's
/// <c>last_reported</c> does not: on 2026-10-31 Bedroom 1's and the hallway's were 3¾ hours old while ZHA had heard from
/// both within 7 minutes (HOME-ASSISTANT.md gotcha 32). ZHA writes <c>last_seen</c> without a time zone, so it is not read
/// as an instant except the first time: after that, the device was heard from when the value last changed, by the app's
/// own clock. A thermometer ZHA does not know (or ZHA unreachable) gives null.
/// </summary>
internal sealed class ThermometerReports(IHomeAssistantRunner runner, TimeProvider timeProvider, ILogger<ThermometerReports> logger) : IThermometerReports
{
    internal static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, (string LastSeen, DateTimeOffset HeardAt)> _heard = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _lastSeenByEntity = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _askedAt = DateTimeOffset.MinValue;

    public async Task<DateTimeOffset?> LastHeardFromAsync(string entityId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now - _askedAt >= CacheFor)
        {
            _askedAt = now;
            _lastSeenByEntity = await ReadLastSeenAsync() ?? new(StringComparer.OrdinalIgnoreCase);
        }

        if (!_lastSeenByEntity.TryGetValue(entityId, out string? lastSeen))
        {
            return null;
        }

        return Observe(_heard, entityId, lastSeen, now);
    }

    /// <summary>
    /// When the device was heard from, given ZHA's <c>last_seen</c> now. A value seen for the first time is read as UTC and
    /// capped at now (read early, never late, whichever zone ZHA meant); a changed value means it was heard from just now.
    /// </summary>
    internal static DateTimeOffset Observe(Dictionary<string, (string LastSeen, DateTimeOffset HeardAt)> heard, string entityId, string lastSeen, DateTimeOffset now)
    {
        if (heard.TryGetValue(entityId, out (string LastSeen, DateTimeOffset HeardAt) previous))
        {
            if (previous.LastSeen == lastSeen)
            {
                return previous.HeardAt;
            }

            heard[entityId] = (lastSeen, now);
            return now;
        }

        DateTimeOffset heardAt = DateTimeOffset.TryParse(lastSeen, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed) && parsed < now
            ? parsed
            : now;
        heard[entityId] = (lastSeen, heardAt);
        return heardAt;
    }

    private async Task<Dictionary<string, string>?> ReadLastSeenAsync()
    {
        try
        {
            if (runner.CurrentConnection is not { } connection)
            {
                return null;
            }

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            JsonElement? response = await connection.SendCommandAndReturnResponseRawAsync(new ZhaDevicesCommand(), timeout.Token);
            return response is { ValueKind: JsonValueKind.Array } devices ? ParseLastSeen(devices) : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not ask ZHA when its devices were last seen");
            return null;
        }
    }

    /// <summary>Each ZHA entity's device's <c>last_seen</c>, from a <c>zha/devices</c> answer.</summary>
    internal static Dictionary<string, string> ParseLastSeen(JsonElement devices)
    {
        Dictionary<string, string> lastSeenByEntity = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement device in devices.EnumerateArray())
        {
            if (!device.TryGetProperty("last_seen", out JsonElement lastSeen) || lastSeen.ValueKind != JsonValueKind.String
                || !device.TryGetProperty("entities", out JsonElement entities) || entities.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement entity in entities.EnumerateArray())
            {
                if (entity.TryGetProperty("entity_id", out JsonElement entityId) && entityId.ValueKind == JsonValueKind.String)
                {
                    lastSeenByEntity[entityId.GetString()!] = lastSeen.GetString()!;
                }
            }
        }

        return lastSeenByEntity;
    }
}
