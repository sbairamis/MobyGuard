using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobyGuard.Database.Models;

public class RetentionPolicy
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string RepositoryPattern { get; set; } = "*";

    public int? KeepLastCount { get; set; }

    public int? MaxAgeDays { get; set; }

    [MaxLength(500)]
    public string ProtectedTagPatterns { get; set; } = "latest";

    public bool IsEnabled { get; set; } = true;

    public int? RunIntervalHours { get; set; }

    public DateTime? LastRunAt { get; set; }

    public int LastDeletedTagsCount { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
