using Microsoft.Extensions.Diagnostics.HealthChecks;
using MobyGuardApi.Auth;

namespace MobyGuardApi.HealthChecks;

public class RegistryKeyHealthCheck(RegistryTokenSigner tokenSigner) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenSigner.KeyId))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Registry token signing key is not initialized."));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Registry token signing key loaded (kid: {tokenSigner.KeyId})."));
    }
}
