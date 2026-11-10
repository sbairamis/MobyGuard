using System.Text.Json.Serialization;

namespace MobyGuardApi.Database.Dtos.TokenDtos;

public class RegistryJwksResponseDto
{
    [JsonPropertyName("keys")]
    public List<RegistryJwkDto> Keys { get; set; } = [];
}

public class RegistryJwkDto
{
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = string.Empty;

    [JsonPropertyName("use")]
    public string Use { get; set; } = string.Empty;

    [JsonPropertyName("alg")]
    public string Alg { get; set; } = string.Empty;

    [JsonPropertyName("kid")]
    public string Kid { get; set; } = string.Empty;

    [JsonPropertyName("n")]
    public string N { get; set; } = string.Empty;

    [JsonPropertyName("e")]
    public string E { get; set; } = string.Empty;

    [JsonPropertyName("x5c")]
    public List<string> X5c { get; set; } = [];
}
