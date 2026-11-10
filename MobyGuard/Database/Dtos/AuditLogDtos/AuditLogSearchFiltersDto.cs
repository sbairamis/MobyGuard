namespace MobyGuard.Database.Dtos.AuditLogDtos;

public class AuditLogSearchFiltersDto
{
    public Dictionary<string, int> SearchBy { get; set; } = [];
    public AuditLogStringFiltersDto ValuesStr { get; set; } = new();
}

public class AuditLogStringFiltersDto
{
    public Dictionary<string, int> Description { get; set; } = [];
}
