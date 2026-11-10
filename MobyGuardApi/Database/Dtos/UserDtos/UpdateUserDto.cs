using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class UpdateUserDto
{
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    public int UserTypeId { get; set; }

    public bool IsActive { get; set; }
}
