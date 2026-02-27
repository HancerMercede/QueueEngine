using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;

namespace QueueEngine.Workers;

public class QueueWorkerPool : IQueueWorkerPool
{
    private readonly IJobRepository _repository;
    private Dictionary<string, IJobHandler> _handlers;
    private readonly QueueEngineOptions _options;
    private readonly ILogger<QueueWorkerPool> _logger;
    private readonly Dictionary<string, CancellationTokenSource> _workerCts = new();
    private readonly Dictionary<string, RateLimiter> _rateLimiters = new();
    private readonly object _lock = new();

    public QueueWorkerPool(
        IJobRepository repository,
        Dictionary<string, IJobHandler> handlers,
        QueueEngineOptions options,
        ILogger<QueueWorkerPool> logger)
    {
        _repository = repository;
        _handlers = handlers;
        _options = options;
        _logger = logger;
    }

    public void SetHandlers(Dictionary<string, IJobHandler> handlers)
    {
        _handlers = handlers;
    }

    public void Start()
    {
        foreach (var (queueName, queueOptions) in _options.Queues)
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

                var job = await _repository.DequeueAsync(queueName);
                if (job == null)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                if (!_handlers.TryGetValue(job.JobType, out var handler))
                {
                    _logger.LogError("No handler registered for job type '{JobType}'", job.JobType);
                    await _repository.CompleteAsync(job.Id, JobStatus.Failed, $"No handler for type {job.JobType}");
                    continue;
                }

                _logger.LogInformation("Processing job {JobId} of type {JobType}", job.Id, job.JobType);

                try
                {
                    await handler.HandleAsync(job, job.Payload, ct);
                    await _repository.CompleteAsync(job.Id, JobStatus.Done);
                    _logger.LogInformation("Job {JobId} completed successfully", job.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId} failed: {Error}", job.Id, ex.Message);
                    await _repository.CompleteAsync(job.Id, JobStatus.Failed, ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker error in queue {Queue}", queueName);
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task<Dictionary<string, (int Pending, int Running, int Done, int Failed)>> GetAllStatsAsync()
    {
        var stats = new Dictionary<string, (int, int, int, int)>();
        foreach (var queueName in _options.Queues.Keys)
        {
            var s = await _repository.GetStatsAsync(queueName);
            stats[queueName] = s;
        }
        return stats;
    }
}
