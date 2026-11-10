namespace MobyGuardApi.Database.Dtos.UserDtos;

public class LoginResponseDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}
