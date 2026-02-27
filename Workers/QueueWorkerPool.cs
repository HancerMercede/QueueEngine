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
    private readonly Lock _lock = new();
    private readonly string _workerId;
    private readonly bool _clusterEnabled;
    private CancellationTokenSource? _heartbeatCts;
    private readonly Dictionary<string, bool> _pausedQueues = new();

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
        _clusterEnabled = options.Cluster.Enabled;
        _workerId = options.Cluster.WorkerId;
    }

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

    public async void Start()
    {
        if (_clusterEnabled)
        {
            var queues = _options.Queues.Keys.ToArray();
            await _repository.RegisterWorkerAsync(_workerId, queues, _options.Cluster.HeartbeatIntervalSeconds);
            
            _heartbeatCts = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
            
            if (_options.Cluster.EnableJobStealing)
            {
                _ = RecoverStaleJobsLoopAsync(_heartbeatCts.Token);
            }
            
            _logger.LogInformation("Worker {WorkerId} registered in cluster mode", _workerId);
        }

        foreach (var (queueName, queueOptions) in _options.Queues)
        {
            var cts = new CancellationTokenSource();
            _workerCts[queueName] = cts;
            _rateLimiters[queueName] = new RateLimiter(queueOptions.RateLimitPerSecond);
            _pausedQueues[queueName] = queueOptions.StartPaused;

            if (queueOptions.StartPaused)
            {
                _logger.LogInformation("Queue {QueueName} starts in paused state", queueName);
                continue;
            }

            for (int i = 0; i < queueOptions.Concurrency; i++)
            {
                _ = Task.Run(() => WorkerLoopAsync(queueName, cts.Token));
            }
        }
    }

    public async void Stop()
    {
        lock (_lock)
        {
            foreach (var cts in _workerCts.Values)
            {
                cts?.Cancel();
            }
        }

        if (_clusterEnabled)
        {
            _heartbeatCts?.Cancel();
            await _repository.UnregisterWorkerAsync(_workerId);
            _logger.LogInformation("Worker {WorkerId} unregistered from cluster", _workerId);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.Cluster.HeartbeatIntervalSeconds), ct);
                await _repository.HeartbeatAsync(_workerId);
                _logger.LogDebug("Heartbeat sent for worker {WorkerId}", _workerId);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat for worker {WorkerId}", _workerId);
            }
        }
    }

    private async Task RecoverStaleJobsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                await _repository.RecoverStaleJobsAsync(_workerId, _options.Cluster.StaleJobTimeoutSeconds);
                _logger.LogDebug("Stale job recovery completed for worker {WorkerId}", _workerId);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover stale jobs for worker {WorkerId}", _workerId);
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
                if (_pausedQueues.TryGetValue(queueName, out var isPaused) && isPaused)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                await rateLimiter.WaitAsync(ct);

                var job = await _repository.DequeueAsync(queueName, _clusterEnabled ? _workerId : null);
                if (job == null)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                if (job.CancellationRequested)
                {
                    await _repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled by user");
                    _logger.LogInformation("Job {JobId} was cancelled", job.Id);
                    continue;
                }

                if (!_handlers.TryGetValue(job.JobType, out var handler))
                {
                    _logger.LogError("No handler registered for job type '{JobType}'", job.JobType);
                    await _repository.CompleteAsync(job.Id, JobStatus.Failed, $"No handler for type {job.JobType}");
                    continue;
                }

                _logger.LogInformation("Processing job {JobId} of type {JobType} on worker {WorkerId}", 
                    job.Id, job.JobType, _clusterEnabled ? _workerId : "local");

                try
                {
                    var progress = new Progress<int>(async p =>
                    {
                        await _repository.UpdateProgressAsync(job.Id, p);
                    });
                    
                    await handler.HandleAsync(job, job.Payload, progress, ct);
                    
                    var currentJob = await _repository.GetJobAsync(job.Id);
                    if (currentJob?.CancellationRequested == true)
                    {
                        await _repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled during execution");
                        _logger.LogInformation("Job {JobId} was cancelled during execution", job.Id);
                    }
                    else
                    {
                        await _repository.CompleteAsync(job.Id, JobStatus.Done);
                        _logger.LogInformation("Job {JobId} completed successfully", job.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                    await _repository.CompleteAsync(job.Id, JobStatus.Cancelled, "Cancelled during execution");
                    _logger.LogInformation("Job {JobId} was cancelled", job.Id);
                }
                catch (Exception ex)
                {
                    var sanitizedMessage = SanitizeErrorMessage(ex.Message);
                    var queueConfig = _options.Queues[queueName];
                    var newRetryCount = job.RetryCount + 1;
                    
                    if (newRetryCount < queueConfig.MaxRetries)
                    {
                        var delay = queueConfig.RetryDelaySeconds * (int)Math.Pow(2, job.RetryCount);
                        _logger.LogWarning("Job {JobId} failed, requeueing for retry {RetryCount}/{MaxRetries} after {Delay}s", 
                            job.Id, newRetryCount, queueConfig.MaxRetries, delay);
                        
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                        await _repository.RequeueAsync(job.Id, newRetryCount, $"Retry {newRetryCount}: {sanitizedMessage}");
                    }
                    else
                    {
                        _logger.LogError(ex, "Job {JobId} failed permanently after {Retries} retries", job.Id, newRetryCount);
                        
                        if (queueConfig.EnableDeadLetterQueue)
                        {
                            await _repository.MoveToDeadLetterAsync(job.Id, $"Max retries exceeded: {sanitizedMessage}");
                            _logger.LogInformation("Job {JobId} moved to dead letter queue", job.Id);
                        }
                        else
                        {
                            await _repository.CompleteAsync(job.Id, JobStatus.Failed, $"Max retries exceeded: {sanitizedMessage}");
                        }
                    }
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

    public async Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetAllStatsAsync()
    {
        var stats = new Dictionary<string, (int, int, int, int, int)>();
        foreach (var queueName in _options.Queues.Keys)
        {
            var s = await _repository.GetStatsAsync(queueName);
            stats[queueName] = s;
        }
        return stats;
    }

    public async Task PauseQueueAsync(string queue)
    {
        if (!_options.Queues.ContainsKey(queue))
            throw new ArgumentException($"Queue '{queue}' is not configured");

        lock (_lock)
        {
            _pausedQueues[queue] = true;
        }
        _logger.LogInformation("Queue {Queue} paused", queue);
    }

    public async Task ResumeQueueAsync(string queue)
    {
        if (!_options.Queues.ContainsKey(queue))
            throw new ArgumentException($"Queue '{queue}' is not configured");

        lock (_lock)
        {
            _pausedQueues[queue] = false;
        }
        
        var cts = _workerCts[queue];
        var queueOptions = _options.Queues[queue];
        
        for (int i = 0; i < queueOptions.Concurrency; i++)
        {
            _ = Task.Run(() => WorkerLoopAsync(queue, cts.Token));
        }
        
        _logger.LogInformation("Queue {Queue} resumed", queue);
    }

    public Task<bool> IsQueuePausedAsync(string queue)
    {
        lock (_lock)
        {
            return Task.FromResult(_pausedQueues.TryGetValue(queue, out var isPaused) && isPaused);
        }
    }
}
