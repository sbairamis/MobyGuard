using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Configuration;

public class HostingOptions
{
    [Required]
    [RegularExpression("^(http|https)$", ErrorMessage = "Scheme must be exactly 'http' or 'https'")]
    public string Scheme { get; init; } = string.Empty;

    [Required]
    public string Ip { get; init; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Port must be a valid network port between 1 and 65535")]
    public int Port { get; init; }
}
