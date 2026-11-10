using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class ResetPasswordDto
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
