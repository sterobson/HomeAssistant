using System.Collections.Generic;

namespace HomeAssistant.Shared;

public class EntityListDto
{
    public List<EntityItemDto> Entities { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

public class EntityItemDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? State { get; set; }
}
