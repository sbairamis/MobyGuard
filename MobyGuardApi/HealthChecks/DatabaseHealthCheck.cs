using Microsoft.Extensions.Diagnostics.HealthChecks;
using MobyGuardApi.Database.Context;

namespace MobyGuardApi.HealthChecks;

public class DatabaseHealthCheck(DatabaseContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL database is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL database is unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL database check failed.", ex);
        }
    }
}
