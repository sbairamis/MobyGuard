using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Configuration;

public class JwtOptions
{
    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    // Tokens are signed with HS256, which needs a key of at least 256 bits.
    [Required]
    [MinLength(32, ErrorMessage = "SecurityKey must be at least 32 characters long")]
    public string SecurityKey { get; init; } = string.Empty;

    [Range(1, 365)]
    public int TokenExpiryDays { get; init; }
}
