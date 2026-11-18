using System.Reflection;

namespace MobyGuard.Global;

public static class Variables
{
    public static string AppVersion { get; } = ResolveAppVersion();
    public static int LoginMaxRetries { get; } = 5;
    public static int LoginRetriesExpiryMinutes { get; } = 30;
    public static int SessionExpiryDays { get; } = 7;
    public static int AuditLogRetentionDays { get; } = 90;

    private static string ResolveAppVersion()
    {
        var infoVersion = typeof(Variables).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(infoVersion))
        {
            return "v1.0.0";
        }

        var cleanVersion = infoVersion.Split('+', 2)[0].Trim();
        return cleanVersion.StartsWith('v') ? cleanVersion : $"v{cleanVersion}";
    }
}
