using MobyGuard.Global;

namespace MobyGuard.Database.Dtos.UserDtos;

public class SetupStatusDto
{
    public bool SetupRequired { get; set; }
    public string ApiVersion { get; set; } = Variables.AppVersion;
}
