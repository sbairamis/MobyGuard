using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class SetupAdminDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;

    public string SetupToken { get; set; } = string.Empty;
}
