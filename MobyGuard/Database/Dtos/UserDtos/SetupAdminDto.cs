using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class SetupAdminDto
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}
