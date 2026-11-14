namespace MobyGuard.Global;

public static class Variables
{
    public static int LoginMaxRetries { get; } = 5;
    public static int LoginRetriesExpiryMinutes { get; } = 30;
    public static int AuditLogRetentionDays { get; } = 90;
}
