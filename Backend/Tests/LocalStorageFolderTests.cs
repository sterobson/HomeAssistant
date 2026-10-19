using HomeAssistant.Services;
using Shouldly;

namespace HomeAssistant.Tests;

/// <summary>
/// The app caches schedules, device settings and battery rules on disk. That folder is shared by
/// every copy run under the same Windows user and is not keyed by house, so a test copy would
/// overwrite a developer's own cache unless it can be given a folder of its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LocalStorageFolderTests
{
    private string? _original;

    [TestInitialize]
    public void Remember() => _original = Environment.GetEnvironmentVariable(LocalStorageFolder.OverrideVariable);

    [TestCleanup]
    public void Restore() => Environment.SetEnvironmentVariable(LocalStorageFolder.OverrideVariable, _original);

    [TestMethod]
    public void UsesTheOverrideFolderWhenSet()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ha-local-storage-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(LocalStorageFolder.OverrideVariable, folder);

        try
        {
            LocalStorageFolder.Resolve().ShouldBe(folder);
            Directory.Exists(folder).ShouldBeTrue("the folder should be created so the services can write to it straight away");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void FallsBackToTheUserProfileFolderWhenUnsetOrBlank(string? value)
    {
        Environment.SetEnvironmentVariable(LocalStorageFolder.OverrideVariable, value);

        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeAssistant");

        LocalStorageFolder.Resolve().ShouldBe(expected);
    }

    // As a Home Assistant add-on only /data survives a restart (2026-10-19: the boiler belief was lost).

    [TestMethod]
    public void AsAHomeAssistantAddOn_UsesTheAddOnsDataFolder()
    {
        string folder = LocalStorageFolder.Choose(
            name => name == LocalStorageFolder.SupervisorTokenVariable ? "a token" : null,
            path => path == LocalStorageFolder.AddOnDataFolder);

        folder.ShouldBe("/data/HomeAssistant");
    }

    [TestMethod]
    public void WithoutTheSupervisor_IgnoresAFolderCalledData()
    {
        string folder = LocalStorageFolder.Choose(_ => null, path => path == LocalStorageFolder.AddOnDataFolder);

        folder.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeAssistant"));
    }

    [TestMethod]
    public void WithTheSupervisorButNoDataFolder_FallsBackToTheUserProfile()
    {
        string folder = LocalStorageFolder.Choose(
            name => name == LocalStorageFolder.SupervisorTokenVariable ? "a token" : null,
            _ => false);

        folder.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeAssistant"));
    }

    [TestMethod]
    public void TheOverrideWinsEvenInsideAnAddOn()
    {
        string folder = LocalStorageFolder.Choose(
            name => name switch
            {
                LocalStorageFolder.OverrideVariable => "/config/elsewhere",
                LocalStorageFolder.SupervisorTokenVariable => "a token",
                _ => null
            },
            _ => true);

        folder.ShouldBe("/config/elsewhere");
    }
}
