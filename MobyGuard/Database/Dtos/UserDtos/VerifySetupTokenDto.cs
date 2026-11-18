using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class VerifySetupTokenDto
{
    [Required]
    public string SetupToken { get; set; } = string.Empty;
}
