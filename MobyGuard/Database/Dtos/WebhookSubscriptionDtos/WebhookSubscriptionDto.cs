namespace MobyGuard.Database.Dtos.WebhookSubscriptionDtos;

public class WebhookSubscriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "Slack";
    public string TargetUrl { get; set; } = string.Empty;
    public string RepositoryPattern { get; set; } = "*";
    public bool NotifyOnPush { get; set; }
    public bool NotifyOnDelete { get; set; }
    public bool NotifyOnRetention { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int? LastStatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
