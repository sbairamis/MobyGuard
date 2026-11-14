using System.Text.Json.Serialization;

namespace MobyGuard.Database.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;
    public DateTime LastLogin { get; set; }
    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public string Salt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int UserTypeId { get; set; }
    public UserType? UserType { get; set; }
}
