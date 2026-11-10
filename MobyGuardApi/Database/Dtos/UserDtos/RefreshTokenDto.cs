using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class RefreshTokenDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
