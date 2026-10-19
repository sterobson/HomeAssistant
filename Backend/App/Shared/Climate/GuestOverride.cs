using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HomeAssistant.Shared.Climate;

/// <summary>
/// What someone holding a QR link may do: put that zone — or every zone in that layout group — on a temporary
/// override, change it, and cancel it. The override is stored as each zone's <see cref="RoomDto.Boost"/>, exactly
/// like an owner's boost: it sets that temperature, up or down, until it ends. Nothing else in the schedules is
/// ever touched. Linked into the Functions project, which is the only caller; it lives here so the existing
/// test project can cover it.
/// </summary>
public static class GuestOverride
{
    public const double MinTemperature = 16;
    public const double MaxTemperature = 24;
    public const int MaxMinutes = 360;

    /// <summary>Why the request can't be accepted, or null when it is within the guest limits.</summary>
    public static string? Validate(double temperature, int durationMinutes)
    {
        if (double.IsNaN(temperature) || temperature < MinTemperature || temperature > MaxTemperature)
        {
            return $"Temperature must be between {MinTemperature} and {MaxTemperature} °C.";
        }

        if (durationMinutes < 1 || durationMinutes > MaxMinutes)
        {
            return $"Duration must be between 1 and {MaxMinutes} minutes.";
        }

        return null;
    }

    /// <summary>
    /// Sets the override on each of the zones, replacing any boost they had (so calling it again changes a running
    /// override). Returns how many zones it set (zones not in the schedules are skipped). Call
    /// <see cref="Validate"/> first.
    /// </summary>
    public static int Apply(RoomSchedulesDto schedules, IReadOnlyCollection<int> roomIds, double temperature, int durationMinutes, DateTimeOffset now)
    {
        DateTimeOffset start = now.ToUniversalTime();
        int applied = 0;
        foreach (RoomDto room in schedules.Rooms.Where(r => roomIds.Contains(r.Id)))
        {
            room.Boost = new BoostDto
            {
                StartTime = start.ToString("O", CultureInfo.InvariantCulture),
                EndTime = start.AddMinutes(durationMinutes).ToString("O", CultureInfo.InvariantCulture),
                Temperature = temperature
            };
            applied++;
        }

        return applied;
    }

    /// <summary>Clears the boost on each of the zones. Returns how many had one.</summary>
    public static int Cancel(RoomSchedulesDto schedules, IReadOnlyCollection<int> roomIds)
    {
        int cleared = 0;
        foreach (RoomDto room in schedules.Rooms.Where(r => roomIds.Contains(r.Id) && r.Boost != null))
        {
            room.Boost = null;
            cleared++;
        }

        return cleared;
    }

    /// <summary>The zone's boost if it is still running, else null. Expired boosts linger in the blob.</summary>
    public static BoostDto? Active(RoomDto room, DateTimeOffset now)
    {
        if (room.Boost is not { Temperature: not null, EndTime: string end })
        {
            return null;
        }

        return DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset endTime) && endTime > now
            ? room.Boost
            : null;
    }

    /// <summary>
    /// Every zone a layout group contains, at any depth, that still exists in the schedules. Empty when the
    /// group is gone, so a link to a deleted group stops working rather than touching nothing silently.
    /// </summary>
    public static List<int> RoomIdsInGroup(RoomSchedulesDto schedules, string groupId)
    {
        RoomLayoutNodeDto? group = FindGroup(schedules.Layout, groupId);
        if (group == null)
        {
            return [];
        }

        HashSet<int> existing = [.. schedules.Rooms.Select(r => r.Id)];
        List<int> ids = [];
        Collect(group, ids);
        return [.. ids.Where(existing.Contains).Distinct()];
    }

    /// <summary>The group node with this ID, searched at any depth, or null.</summary>
    public static RoomLayoutNodeDto? FindGroup(IEnumerable<RoomLayoutNodeDto>? nodes, string groupId)
    {
        foreach (RoomLayoutNodeDto node in nodes ?? [])
        {
            if (node.RoomId == null && node.Id == groupId)
            {
                return node;
            }

            RoomLayoutNodeDto? inner = FindGroup(node.Children, groupId);
            if (inner != null)
            {
                return inner;
            }
        }

        return null;
    }

    private static void Collect(RoomLayoutNodeDto node, List<int> ids)
    {
        if (node.RoomId is int roomId)
        {
            ids.Add(roomId);
        }

        foreach (RoomLayoutNodeDto child in node.Children ?? [])
        {
            Collect(child, ids);
        }
    }
}
