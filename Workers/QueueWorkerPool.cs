using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;

namespace QueueEngine.Workers;

public class QueueWorkerPool(
    IJobRepository repository,
    Dictionary<string, IJobHandler> handlers,
    QueueEngineOptions options,
    ILogger<QueueWorkerPool> logger)
    : IQueueWorkerPool
{
    private Dictionary<string, IJobHandler> _handlers = handlers;
    private readonly Dictionary<string, CancellationTokenSource> _workerCts = new();
    private readonly Dictionary<string, RateLimiter> _rateLimiters = new();
    private readonly Lock _lock = new();

    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "Unknown error";
        
        if (message.Length > 500)
            message = message[..500] + "...";
        
        return message;
    }

    public void SetHandlers(Dictionary<string, IJobHandler> handlers)
    {
        _handlers = handlers;
    }

    public void Start()
    {
        foreach (var (queueName, queueOptions) in options.Queues)
        {
            var cts = new CancellationTokenSource();
            _workerCts[queueName] = cts;
            _rateLimiters[queueName] = new RateLimiter(queueOptions.RateLimitPerSecond);

            for (int i = 0; i < queueOptions.Concurrency; i++)
            {
                Task.Run(() => WorkerLoopAsync(queueName, cts.Token));
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            foreach (var cts in _workerCts.Values)
            {
                cts?.Cancel();
            }
        }
    }

    private async Task WorkerLoopAsync(string queueName, CancellationToken ct)
    {
        var rateLimiter = _rateLimiters[queueName];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await rateLimiter.WaitAsync(ct);

                var job = await repository.DequeueAsync(queueName);
                if (job == null)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                if (job.CancellationRequested)
                {
                    await repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled by user");
                    logger.LogInformation("Job {JobId} was cancelled", job.Id);
                    continue;
                }

                if (!_handlers.TryGetValue(job.JobType, out var handler))
                {
                    logger.LogError("No handler registered for job type '{JobType}'", job.JobType);
                    await repository.CompleteAsync(job.Id, JobStatus.Failed, $"No handler for type {job.JobType}");
                    continue;
                }

                logger.LogInformation("Processing job {JobId} of type {JobType}", job.Id, job.JobType);

                try
                {
                    await handler.HandleAsync(job, job.Payload, ct);
                    
                    var currentJob = await repository.GetJobAsync(job.Id);
                    if (currentJob?.CancellationRequested == true)
                    {
                        await repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled during execution");
                        logger.LogInformation("Job {JobId} was cancelled during execution", job.Id);
                    }
                    else
                    {
                        await repository.CompleteAsync(job.Id, JobStatus.Done);
                        logger.LogInformation("Job {JobId} completed successfully", job.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                    await repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled during execution");
                    logger.LogInformation("Job {JobId} was cancelled", job.Id);
                }
                catch (Exception ex)
                {
                    var sanitizedMessage = SanitizeErrorMessage(ex.Message);
                    logger.LogError(ex, "Job {JobId} failed", job.Id);
                    await repository.CompleteAsync(job.Id, JobStatus.Failed, sanitizedMessage);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker error in queue {Queue}", queueName);
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetAllStatsAsync()
    {
        var stats = new Dictionary<string, (int, int, int, int, int)>();
        foreach (var queueName in options.Queues.Keys)
        {
            var s = await repository.GetStatsAsync(queueName);
            stats[queueName] = s;
        }
        return stats;
    }
}
