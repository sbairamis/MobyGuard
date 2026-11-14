using MobyGuard.Repos;

namespace MobyGuard.Services;

public class RetentionPolicySchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionPolicySchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

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

                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<RetentionPolicyRepository>();
                var executedCount = await repository.ExecuteDueScheduledPoliciesAsync();

                if (executedCount > 0)
                {
                    logger.LogInformation("Scheduled retention runner executed {Count} due retention policy(ies).", executedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred in the scheduled retention policy background runner.");
            }
        }
    }
}
