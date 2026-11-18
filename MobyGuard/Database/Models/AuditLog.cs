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

    /// <summary>Actor: the user (or "system" / "anonymous") who performed the action, captured at the time of the event.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Actor user id. Intentionally not a foreign key so history survives user deletion and renames.</summary>
    public int? ActorUserId { get; set; }

    /// <summary>Affected entity: target username, webhook name or retention policy name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Affected user id (when the target is a user). Intentionally not a foreign key.</summary>
    public int? TargetUserId { get; set; }

    public string Repository { get; set; } = string.Empty;
    public string Actions { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Human-readable change summary (e.g. "role: User → Admin"). Never contains secrets.</summary>
    public string Details { get; set; } = string.Empty;

    public string ClientIp { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
