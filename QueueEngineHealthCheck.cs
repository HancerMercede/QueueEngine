using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueueEngine.Core;

namespace QueueEngine;

public class QueueEngineHealthCheck(IQueueEngine queueEngine) : IHealthCheck
{
    private readonly IQueueEngine _queueEngine = queueEngine;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _queueEngine.GetStatsAsync();
            
            var totalPending = stats.Values.Sum(s => s.Pending);
            var totalRunning = stats.Values.Sum(s => s.Running);
            var totalFailed = stats.Values.Sum(s => s.Failed);

            var data = new Dictionary<string, object>
            {
                ["queues"] = stats.Count,
                ["totalPending"] = totalPending,
                ["totalRunning"] = totalRunning,
                ["totalFailed"] = totalFailed
            };

            if (totalFailed > 100)
            {
                return HealthCheckResult.Degraded(
                    $"Queue engine has {totalFailed} failed jobs",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Queue engine operational with {totalPending} pending jobs",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Queue engine health check failed",
                ex);
        }
    }
}

public static class QueueEngineHealthCheckExtensions
{
    public static IHealthChecksBuilder AddQueueEngineHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "queueengine",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        return builder.Add(
            new HealthCheckRegistration(
                name,
                sp => new QueueEngineHealthCheck(sp.GetRequiredService<IQueueEngine>()),
                failureStatus,
                tags));
    }
}
