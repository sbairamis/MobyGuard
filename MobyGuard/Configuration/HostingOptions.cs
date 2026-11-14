using System.ComponentModel.DataAnnotations;

namespace MobyGuard.Configuration;

public class HostingOptions
{
    [Required]
    [RegularExpression("^(http|https)$", ErrorMessage = "Scheme must be exactly 'http' or 'https'")]
    public string Scheme { get; init; } = "http";

    [Required]
    public string Ip { get; init; } = "0.0.0.0";

    [Range(1, 65535, ErrorMessage = "Port must be a valid network port between 1 and 65535")]
    public int Port { get; init; } = 5090;
}
