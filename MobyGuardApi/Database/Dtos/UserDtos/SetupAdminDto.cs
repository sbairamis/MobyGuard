using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class SetupAdminDto
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(128)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}
