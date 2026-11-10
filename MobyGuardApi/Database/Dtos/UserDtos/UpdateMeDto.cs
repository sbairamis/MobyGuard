using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Database.Dtos.UserDtos;

public class UpdateMeDto
{
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;
}
