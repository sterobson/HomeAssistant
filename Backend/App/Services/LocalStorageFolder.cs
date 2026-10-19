using System;
using System.IO;

namespace HomeAssistant.Services;

/// <summary>
/// Where the app keeps its on-disk caches (schedules, device settings, battery rules, the boiler belief).
///
/// Running as a Home Assistant add-on - which is how the app is deployed - that is the add-on's
/// <c>/data</c> folder. The Supervisor recreates an add-on's container whenever the add-on restarts,
/// and every over-the-air update restarts it, so anything else in the container is lost: on 2026-10-19
/// the boiler belief saved in the user profile folder did not survive a restart, and the app forgot it
/// had turned the boiler on. <c>/data</c> is the one folder the Supervisor keeps for each add-on.
///
/// Anywhere else (a developer's machine) it is %LOCALAPPDATA%\HomeAssistant. That folder is shared by
/// every copy of the app run under the same Windows user and is not keyed by house, so a second copy -
/// for example a local test run against a copy of production data - silently overwrites what the first
/// one cached. Set <see cref="OverrideVariable"/> to give a copy its own folder; it wins everywhere.
/// </summary>
internal static class LocalStorageFolder
{
    public const string OverrideVariable = "HOMEASSISTANT_LOCAL_STORAGE";

    // Set by the Supervisor in every add-on container. Only its presence is checked; never read or log it.
    internal const string SupervisorTokenVariable = "SUPERVISOR_TOKEN";
    internal const string AddOnDataFolder = "/data";

    public static string Resolve()
    {
        string folder = Choose(Environment.GetEnvironmentVariable, Directory.Exists);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Picks the folder without touching the disk, so the choice can be tested anywhere.</summary>
    internal static string Choose(Func<string, string?> getVariable, Func<string, bool> folderExists)
    {
        string? overridePath = getVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        if (!string.IsNullOrEmpty(getVariable(SupervisorTokenVariable)) && folderExists(AddOnDataFolder))
        {
            return AddOnDataFolder + "/HomeAssistant";
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeAssistant");
    }
}
