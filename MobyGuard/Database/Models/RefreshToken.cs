using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Database.Models;

/// <summary>
/// Persisted SHA-256 hash of a single-use Web UI refresh token.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
