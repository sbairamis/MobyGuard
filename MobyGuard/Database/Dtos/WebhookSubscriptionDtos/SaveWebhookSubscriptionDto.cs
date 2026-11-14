using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.WebhookSubscriptionDtos;

public class SaveWebhookSubscriptionDto
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Provider { get; set; } = "Slack";

    [Required]
    [MaxLength(1000)]
    public string TargetUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string RepositoryPattern { get; set; } = "*";

    public bool NotifyOnPush { get; set; } = true;

    public bool NotifyOnDelete { get; set; } = true;

    public bool NotifyOnRetention { get; set; } = true;

    public bool IsEnabled { get; set; } = true;
}
