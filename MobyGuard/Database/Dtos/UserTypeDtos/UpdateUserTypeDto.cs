using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Dtos.UserTypeDtos;

public class UpdateUserTypeDto
{
    [Required]
    public string Type { get; set; } = string.Empty;
}
