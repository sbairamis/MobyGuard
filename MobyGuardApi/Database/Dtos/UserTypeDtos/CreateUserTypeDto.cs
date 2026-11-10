using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserTypeDtos;

public class CreateUserTypeDto
{
    [Required]
    public string Type { get; set; } = string.Empty;
}
