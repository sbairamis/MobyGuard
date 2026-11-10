using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class ChangePasswordDto
{
    public string? CurrentPassword { get; set; }

    [Required]
    public string Password { get; set; } = string.Empty;
}
