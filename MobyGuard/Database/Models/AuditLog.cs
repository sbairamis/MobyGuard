using MobyGuard.Interfaces;

namespace MobyGuard.Database.Models;

public class AuditLog : IModel
{
    public AuditLog(
        string username = "",
        string repository = "",
        string actions = "",
        string description = "",
        string clientIp = "")
    {
        this.Username = username;
        this.Repository = repository;
        this.Actions = actions;
        this.Description = description;
        this.ClientIp = clientIp;
        this.CreatedAt = DateTime.UtcNow;
        this.UpdatedAt = DateTime.UtcNow;
    }

    public uint Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Actions { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
