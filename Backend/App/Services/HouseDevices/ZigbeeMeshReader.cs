using HomeAssistant.Shared;
using NetDaemon.Client;
using NetDaemon.Client.HomeAssistant.Model;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeAssistant.Services.HouseDevices;

/// <summary>Reads the Zigbee mesh for the 3D view's signal lines.</summary>
internal interface IZigbeeMeshReader
{
    /// <summary>The mesh, or null when ZHA could not be asked.</summary>
    Task<HouseZigbeeDto?> ReadAsync();
}

/// <summary>
/// Asks ZHA for its devices over Home Assistant's websocket (<c>zha/devices</c>, as tools/ha/zigbee-mesh.cjs does) and
/// keeps each device's neighbour table: who it hears and at what LQI. ZHA refreshes those tables itself every few
/// hours, so this is as fresh as its last scan.
/// </summary>
internal sealed class ZigbeeMeshReader(IHomeAssistantRunner runner, ILogger<ZigbeeMeshReader> logger) : IZigbeeMeshReader
{
    public async Task<HouseZigbeeDto?> ReadAsync()
    {
        try
        {
            IHomeAssistantConnection? connection = runner.CurrentConnection;
            if (connection == null)
            {
                return null;
            }

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            JsonElement? response = await connection.SendCommandAndReturnResponseRawAsync(new ZhaDevicesCommand(), timeout.Token);
            return response is { ValueKind: JsonValueKind.Array } devices ? Parse(devices) : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the Zigbee mesh from ZHA");
            return null;
        }
    }

    /// <summary>The mesh from a <c>zha/devices</c> answer. Neighbours ZHA has no registry device for are left out.</summary>
    internal static HouseZigbeeDto Parse(JsonElement devices)
    {
        Dictionary<string, string> deviceIdByIeee = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement device in devices.EnumerateArray())
        {
            string? ieee = ReadString(device, "ieee");
            string? deviceId = ReadString(device, "device_reg_id");
            if (ieee != null && deviceId != null)
            {
                deviceIdByIeee[ieee] = deviceId;
            }
        }

        HouseZigbeeDto mesh = new();
        foreach (JsonElement device in devices.EnumerateArray())
        {
            string? deviceId = ReadString(device, "device_reg_id");
            if (deviceId == null)
            {
                continue;
            }

            mesh.Nodes.Add(new HouseZigbeeNodeDto
            {
                DeviceId = deviceId,
                Role = (ReadString(device, "device_type") ?? string.Empty).ToLowerInvariant() switch
                {
                    "coordinator" => "coordinator",
                    "router" => "router",
                    _ => "end"
                },
                Available = device.TryGetProperty("available", out JsonElement available) && available.ValueKind == JsonValueKind.True
            });

            if (!device.TryGetProperty("neighbors", out JsonElement neighbours) || neighbours.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement neighbour in neighbours.EnumerateArray())
            {
                string? ieee = ReadString(neighbour, "ieee");
                if (ieee == null || !deviceIdByIeee.TryGetValue(ieee, out string? to) || !TryReadInt(neighbour, "lqi", out int lqi))
                {
                    continue;
                }

                string relationship = (ReadString(neighbour, "relationship") ?? string.Empty).ToLowerInvariant();
                mesh.Links.Add(new HouseZigbeeLinkDto
                {
                    From = deviceId,
                    To = to,
                    Lqi = lqi,
                    Relationship = relationship.Contains("parent") ? "parent"
                        : relationship.Contains("child") ? "child"
                        : relationship.Contains("sibling") ? "sibling"
                        : "other"
                });
            }
        }

        return mesh;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ZHA reports a neighbour's LQI as a string ("172"), a device's as a number.
    private static bool TryReadInt(JsonElement element, string name, out int result)
    {
        result = 0;
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out result),
            JsonValueKind.String => int.TryParse(value.GetString(), out result),
            _ => false
        };
    }
}

/// <summary>HA's websocket <c>zha/devices</c> command: every ZHA device with its neighbour table.</summary>
internal sealed record ZhaDevicesCommand : CommandMessage
{
    public ZhaDevicesCommand()
    {
        Type = "zha/devices";
    }
}
