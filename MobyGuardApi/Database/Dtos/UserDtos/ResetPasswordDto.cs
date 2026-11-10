using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class ResetPasswordDto
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
