using System.Text.Json.Serialization;

namespace MobyGuardApi.Database.Dtos.TokenDtos;

public class RegistryTokenResponseDto
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("issued_at")]
    public DateTime IssuedAt { get; set; }
}
