using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Configuration;

public class JwtOptions
{
    [Required]
    public string Issuer { get; init; } = "mobyguard";

    [Required]
    public string Audience { get; init; } = "mobyguard";

    // Tokens are signed with HS256, which needs a key of at least 256 bits.
    [Required]
    [MinLength(32, ErrorMessage = "SecurityKey must be at least 32 characters long")]
    public string SecurityKey { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenExpiryMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenExpiryDays { get; init; } = 7;
}
