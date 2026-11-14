using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobyGuard.Database.Models;

public class WebhookSubscription
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Provider { get; set; } = "Slack"; // "Slack", "Discord", "Teams"

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

    public DateTime? LastTriggeredAt { get; set; }

    public int? LastStatusCode { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
