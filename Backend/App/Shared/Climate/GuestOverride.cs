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
    /// override, and a group's supersedes any inside it). A group's override is tagged with
    /// <paramref name="groupId"/>, exactly like an owner's group boost. Returns how many zones it set (zones not in
    /// the schedules are skipped). Call <see cref="Validate"/> and <see cref="LockedBy"/> first.
    /// </summary>
    public static int Apply(RoomSchedulesDto schedules, IReadOnlyCollection<int> roomIds, double temperature, int durationMinutes, DateTimeOffset now, string? groupId = null)
    {
        DateTimeOffset start = now.ToUniversalTime();
        int applied = 0;
        foreach (RoomDto room in schedules.Rooms.Where(r => roomIds.Contains(r.Id)))
        {
            room.Boost = new BoostDto
            {
                StartTime = start.ToString("O", CultureInfo.InvariantCulture),
                EndTime = start.AddMinutes(durationMinutes).ToString("O", CultureInfo.InvariantCulture),
                Temperature = temperature,
                GroupId = groupId
            };
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Clears the boost on each of the zones. For a group (<paramref name="groupId"/> set) only that group's own
    /// boost is cleared: a zone's own boost, or a smaller group's, is left alone. Returns how many it cleared.
    /// </summary>
    public static int Cancel(RoomSchedulesDto schedules, IReadOnlyCollection<int> roomIds, string? groupId = null)
    {
        int cleared = 0;
        foreach (RoomDto room in schedules.Rooms.Where(r => roomIds.Contains(r.Id) && r.Boost != null))
        {
            if (groupId != null && room.Boost!.GroupId != groupId)
            {
                continue;
            }

            room.Boost = null;
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// The group whose running boost locks a zone's or a group's boost controls, or null (owner, 2026-10-19). While
    /// a group's boost runs nothing inside it can be boosted on its own; only that group, or one around it, can
    /// change it. For a zone: the group its running boost is tagged with, if the zone still sits in it. For a group:
    /// the outermost group around it with a running boost of its own. Mirrors Frontend/src/utils/boosts.js.
    /// </summary>
    public static RoomLayoutNodeDto? LockedBy(RoomSchedulesDto schedules, int? roomId, string? groupId, DateTimeOffset now)
    {
        if (groupId != null)
        {
            return GroupsAround(schedules.Layout, node => node.RoomId == null && node.Id == groupId)
                .FirstOrDefault(outer => GroupBoostActive(schedules, outer, now));
        }

        RoomDto? room = schedules.Rooms.FirstOrDefault(r => r.Id == roomId);
        string? boostGroupId = room == null ? null : Active(room, now)?.GroupId;
        if (boostGroupId == null)
        {
            return null;
        }

        return GroupsAround(schedules.Layout, node => node.RoomId == roomId)
            .FirstOrDefault(group => group.Id == boostGroupId);
    }

    /// <summary>Whether a group's own boost is running: a zone inside carries a running boost tagged with it.</summary>
    public static bool GroupBoostActive(RoomSchedulesDto schedules, RoomLayoutNodeDto group, DateTimeOffset now)
    {
        List<int> ids = [];
        Collect(group, ids);
        return schedules.Rooms.Any(room => ids.Contains(room.Id) && Active(room, now)?.GroupId == group.Id);
    }

    /// <summary>The groups around the first node matching <paramref name="isTarget"/>, outermost first.</summary>
    private static List<RoomLayoutNodeDto> GroupsAround(IEnumerable<RoomLayoutNodeDto>? nodes, Func<RoomLayoutNodeDto, bool> isTarget)
    {
        foreach (RoomLayoutNodeDto node in nodes ?? [])
        {
            if (isTarget(node))
            {
                return [];
            }

            if (node.RoomId == null)
            {
                List<RoomLayoutNodeDto> inner = GroupsAround(node.Children, isTarget);
                if (inner.Count > 0 || ContainsTarget(node.Children, isTarget))
                {
                    return [node, .. inner];
                }
            }
        }

        return [];
    }

    private static bool ContainsTarget(IEnumerable<RoomLayoutNodeDto>? nodes, Func<RoomLayoutNodeDto, bool> isTarget)
    {
        return (nodes ?? []).Any(node => isTarget(node) || (node.RoomId == null && ContainsTarget(node.Children, isTarget)));
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
