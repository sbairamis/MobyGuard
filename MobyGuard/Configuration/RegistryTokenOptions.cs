using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Configuration;

public class RegistryTokenOptions
{
    [Required]
    public string Issuer { get; init; } = "mobyguard-token-issuer";

    [Required]
    public string Service { get; init; } = "registry";

    [Required]
    public string CertificatePath { get; init; } = "/app/certs/registry-auth.crt";

    [Required]
    public string PrivateKeyPath { get; init; } = "/app/certs/registry-auth.key";

    [Range(1, 1440)]
    public int TokenExpiryMinutes { get; init; } = 15;

    [Required]
    public string RegistryUrl { get; init; } = "http://registry:5000";

    public string? WebhookSecret { get; init; }
}
