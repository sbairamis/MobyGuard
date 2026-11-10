using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class AddUserDto
{
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public int UserTypeId { get; set; }
}
