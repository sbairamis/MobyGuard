using MobyGuard.Repos;

namespace MobyGuard.Services;

public class RetentionPolicySchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionPolicySchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AuditLogPurgeInterval = TimeSpan.FromHours(1);

    private DateTime _nextAuditLogPurgeUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await RunMaintenanceTickAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred in the scheduled maintenance background runner.");
            }
        }
    }

    internal void ResetAuditPurgeSchedule() => _nextAuditLogPurgeUtc = DateTime.MinValue;

    internal async Task<(int ExecutedPolicies, int PurgedAuditLogs)> RunMaintenanceTickAsync(
        DateTime utcNow,
        CancellationToken stoppingToken = default)
    {
        using var scope = scopeFactory.CreateScope();

        var retentionRepository = scope.ServiceProvider.GetRequiredService<RetentionPolicyRepository>();
        var executedCount = await retentionRepository.ExecuteDueScheduledPoliciesAsync();

        if (executedCount > 0)
        {
            logger.LogInformation("Scheduled retention runner executed {Count} due retention policy(ies).", executedCount);
        }

        var purgedCount = 0;
        if (utcNow >= _nextAuditLogPurgeUtc)
        {
            var auditLogRepository = scope.ServiceProvider.GetRequiredService<AuditLogRepository>();
            purgedCount = await auditLogRepository.PurgeExpiredLogsAsync(stoppingToken);
            _nextAuditLogPurgeUtc = utcNow.Add(AuditLogPurgeInterval);

            if (purgedCount > 0)
            {
                logger.LogInformation("Scheduled audit cleanup purged {Count} expired audit log row(s).", purgedCount);
            }
        }

        return (executedCount, purgedCount);
    }
}
