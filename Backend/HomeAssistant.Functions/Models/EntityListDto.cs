using System.Collections.Generic;

namespace HomeAssistant.Functions.Models;

public class EntityListDto
{
    public List<EntityDto> Entities { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

public class EntityDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? State { get; set; }
}
