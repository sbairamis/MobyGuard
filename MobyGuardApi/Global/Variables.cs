namespace MobyGuardApi.Global;

public static class Variables
{
    public static int LoginMaxRetries { get; } = 5;
    public static int LoginRetriesExpiryMinutes { get; } = 30;
}
