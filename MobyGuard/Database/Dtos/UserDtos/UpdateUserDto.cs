using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserDtos;

public class UpdateUserDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    public int UserTypeId { get; set; }

    public bool IsActive { get; set; }
}
