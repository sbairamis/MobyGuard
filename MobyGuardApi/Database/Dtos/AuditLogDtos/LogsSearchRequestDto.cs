namespace MobyGuardApi.Database.Dtos.AuditLogDtos;

public class LogsSearchRequestDto
{
    public Enumerators.AuditLogEnums.SearchBy SearchBy { get; set; } = Enumerators.AuditLogEnums.SearchBy.None;
    public string? ValueStr { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}
