namespace MobyGuard.Database.Dtos.RetentionPolicyDtos;

public class RetentionPolicyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RepositoryPattern { get; set; } = "*";
    public int? KeepLastCount { get; set; }
    public int? MaxAgeDays { get; set; }
    public string ProtectedTagPatterns { get; set; } = "latest";
    public bool IsEnabled { get; set; }
    public int? RunIntervalHours { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int LastDeletedTagsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
