using HomeAssistant.Services.HouseDevices;
using HomeAssistant.Shared;
using Shouldly;
using System.Text.Json;

namespace HomeAssistant.Tests;

/// <summary>
/// The 3D view's signal lines come from ZHA's <c>zha/devices</c> answer. These pin how it is read, in the shape ZHA
/// actually sends (a neighbour's LQI as a string, its relationship in PascalCase), checked against the live answer
/// on 2026-10-22.
/// </summary>
[TestClass]
public class ZigbeeMeshReaderTests
{
    private const string Answer = """
        [
          { "ieee": "00:01", "device_reg_id": "hub", "device_type": "Coordinator", "available": true,
            "neighbors": [ { "ieee": "00:02", "lqi": "180", "relationship": "Child" } ] },
          { "ieee": "00:02", "device_reg_id": "plug", "device_type": "Router", "available": true,
            "neighbors": [
              { "ieee": "00:01", "lqi": "172", "relationship": "Parent" },
              { "ieee": "00:03", "lqi": "140", "relationship": "Child" },
              { "ieee": "99:99", "lqi": "50", "relationship": "Sibling" }
            ] },
          { "ieee": "00:03", "device_reg_id": "thermometer", "device_type": "EndDevice", "available": false, "neighbors": [] }
        ]
        """;

    [TestMethod]
    public void ReadsRolesAndLinks_ByDeviceRegistryId()
    {
        HouseZigbeeDto mesh = ZigbeeMeshReader.Parse(JsonDocument.Parse(Answer).RootElement);

        mesh.Nodes.Select(n => (n.DeviceId, n.Role, n.Available)).ShouldBe([("hub", "coordinator", true), ("plug", "router", true), ("thermometer", "end", false)]);
        mesh.Links.Select(l => (l.From, l.To, l.Lqi, l.Relationship)).ShouldBe(
        [
            ("hub", "plug", 180, "child"),
            ("plug", "hub", 172, "parent"),
            ("plug", "thermometer", 140, "child")
        ]);
    }

    [TestMethod]
    public void KeepsAHubWithNoEntities_SoSignalLinesHaveSomewhereToEnd()
    {
        HouseDeviceReading hub = new("hub", "SONOFF ZBDongle-E", "SONOFF", "ZBDongle-E", "Living Room", []);

        HouseDeviceRules.Describe([hub], new Dictionary<string, string>()).ShouldBeEmpty();
        HouseDeviceRules.Describe([hub], new Dictionary<string, string>(), new HashSet<string> { "hub" }).Single().Id.ShouldBe("hub");
    }
}
