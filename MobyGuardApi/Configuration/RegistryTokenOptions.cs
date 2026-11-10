using System.ComponentModel.DataAnnotations;

namespace MobyGuardApi.Configuration;

public class RegistryTokenOptions
{
    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Service { get; init; } = string.Empty;

    [Required]
    public string CertificatePath { get; init; } = string.Empty;

    [Required]
    public string PrivateKeyPath { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int TokenExpiryMinutes { get; init; } = 15;

    [Required]
    public string RegistryUrl { get; init; } = "http://registry:5000";
}
