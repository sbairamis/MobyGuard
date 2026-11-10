using MobyGuardApi.Interfaces;

namespace MobyGuardApi.Database.Models;

public class RepositoryPermission : IModel
{
    public uint Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string RepositoryPattern { get; set; } = string.Empty;
    public bool CanPull { get; set; }
    public bool CanPush { get; set; }
    public bool CanDelete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
