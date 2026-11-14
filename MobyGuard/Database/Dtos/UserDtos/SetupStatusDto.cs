namespace MobyGuard.Database.Dtos.UserDtos;

public class SetupStatusDto
{
    public bool SetupRequired { get; set; }
    public string ApiVersion { get; set; } = "v1.0.8";
}
