namespace MobyGuard.Database.Dtos.RepositoryPermissionDtos;

public class RepositoryPermissionDto
{
    public uint Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string RepositoryPattern { get; set; } = string.Empty;
    public bool CanPull { get; set; }
    public bool CanPush { get; set; }
    public bool CanDelete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
