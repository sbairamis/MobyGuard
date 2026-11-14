using Microsoft.Extensions.Diagnostics.HealthChecks;
using MobyGuard.Database.Context;

namespace MobyGuard.HealthChecks;

public class DatabaseHealthCheck(DatabaseContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQLite database is reachable.")
                : HealthCheckResult.Unhealthy("SQLite database is unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite database check failed.", ex);
        }
    }
}
