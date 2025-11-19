using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeAssistant.Services.WasteManagement;

public interface IWasteCollectionService
{
    Task<List<BinServiceDto>> GetBinCollectionsAsync(string uprn);
}

public class BinServiceDto
{
    public string? Service { get; set; }
    public DateTime? LastCollected { get; set; }
    public DateTime? NextCollection { get; set; }
    public string? Frequency { get; set; }
    public string? BinDescription { get; set; }
    public string? WasteType { get; set; }
    public string? CollectedBy { get; set; }
}