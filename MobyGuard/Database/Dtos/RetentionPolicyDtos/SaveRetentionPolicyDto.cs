using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.RetentionPolicyDtos;

public class SaveRetentionPolicyDto
{
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
}
