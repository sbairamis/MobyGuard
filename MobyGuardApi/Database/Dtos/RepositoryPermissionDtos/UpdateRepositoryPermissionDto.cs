using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;

public class UpdateRepositoryPermissionDto
{
    [Required]
    public string RepositoryPattern { get; set; } = string.Empty;

    public bool CanPull { get; set; }

    public bool CanPush { get; set; }

    public bool CanDelete { get; set; }
}
