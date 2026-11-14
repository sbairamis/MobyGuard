namespace MobyGuard.Database.Dtos.RetentionPolicyDtos;

public class RetentionExecutionResultDto
{
    public int PolicyId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public int MatchedRepositoriesCount { get; set; }
    public int EvaluatedTagsCount { get; set; }
    public int ProtectedTagsCount { get; set; }
    public int DeletedDigestsCount { get; set; }
    public int DeletedTagsCount { get; set; }
    public int SkippedSharedDigestTagsCount { get; set; }
    public List<RetentionTagActionDto> Items { get; set; } = [];
}

public class RetentionTagActionDto
{
    public string Repository { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
