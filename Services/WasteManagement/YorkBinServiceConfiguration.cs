using System.Collections.Generic;

namespace HomeAssistant.Services.WasteManagement;

internal class YorkBinServiceConfiguration
{
    public string CollectionDataEndpoint { get; set; } = "";
    public string Uprn { get; set; } = "";
    public List<YorkBinServiceProperty> Properties { get; set; } = [];
}

internal class YorkBinServiceProperty
{
    public List<string> NotificationLabels { get; set; } = [];
    public string Uprn { get; set; } = "";
    public string Schedule { get; set; } = "";
}
