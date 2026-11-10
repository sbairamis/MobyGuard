namespace MobyGuard.Database.Dtos.UserTypeDtos;

public class UserTypeDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
