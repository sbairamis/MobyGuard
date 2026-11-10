using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MobyGuardApi.HealthChecks;

public class RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await connectionMultiplexer.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy($"Redis is reachable ({latency.TotalMilliseconds:F0} ms).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
