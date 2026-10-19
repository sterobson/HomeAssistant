using HomeAssistant.apps;
using HomeAssistant.Shared.Climate;
using System.Collections.Generic;

namespace HomeAssistant.Services.Climate;

/// <summary>
/// The rules the app applies until someone saves rules from the UI (device settings
/// <c>heating.deviceRules</c> is null). They replace the old hard-coded heater gates and were set by the
/// owner on 2026-10-19. Radiator valves have no default rules.
/// </summary>
internal static class DefaultDeviceRules
{
    public static Dictionary<string, DeviceRulesDto> For(INamedEntities entities)
    {
        return new Dictionary<string, DeviceRulesDto>(StringComparer.OrdinalIgnoreCase)
        {
            // Bedroom 1: two short daily windows, and only with someone home.
            [entities.Bedroom1HeaterSmartPlugOnOff.EntityId] = new()
            {
                TimeWindows =
                [
                    new() { Days = Days.Everyday, Start = "08:00", End = "08:30" },
                    new() { Days = Days.Everyday, Start = "21:00", End = "21:15" }
                ],
                Presence = House()
            },

            // Ste's desk: someone at that desk, any time of day.
            [entities.GamesRoomHeaterSmartPlugOnOff.EntityId] = new() { Presence = Zone("games desk") },

            // Kitchen: someone home, 05:30-22:00.
            [entities.KitchenHeaterSmartPlugOnOff.EntityId] = new()
            {
                TimeWindows = [new() { Days = Days.Everyday, Start = "05:30", End = "22:00" }],
                Presence = House()
            },

            // Ruth's desk: someone at that desk, any time of day.
            [entities.DiningRoomHeaterSmartPlugOnOff.EntityId] = new() { Presence = Zone("dining desk") }
        };
    }

    private static PresenceRuleDto House() => new() { Match = PresenceMatch.Any, Sources = [PresenceSources.House] };

    private static PresenceRuleDto Zone(string zoneName) => new() { Match = PresenceMatch.Any, Sources = [zoneName] };
}
