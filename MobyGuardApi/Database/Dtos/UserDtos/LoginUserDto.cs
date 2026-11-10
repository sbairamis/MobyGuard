using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class LoginUserDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
