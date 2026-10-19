using System;
using System.IO;

namespace HomeAssistant.Services;

/// <summary>
/// Where the app keeps its on-disk caches (schedules, device settings, battery rules, the boiler belief).
///
/// By default this is %LOCALAPPDATA%\HomeAssistant, which is correct for the deployed app. But that
/// folder is shared by every copy of the app run under the same Windows user and is not keyed by
/// house, so a second copy - for example a local test run against a copy of production data -
/// silently overwrites what the first one cached. Set <see cref="OverrideVariable"/> to give a
/// copy its own folder.
/// </summary>
internal static class LocalStorageFolder
{
    public const string OverrideVariable = "HOMEASSISTANT_LOCAL_STORAGE";

    public static string Resolve()
    {
        string? overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        string folder = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeAssistant");

        Directory.CreateDirectory(folder);
        return folder;
    }
}
