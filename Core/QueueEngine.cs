using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Core;

public class QueueEngine : IQueueEngine
{
    private readonly IJobRepository _repository;
    private readonly IQueueWorkerPool _workerPool;
    private readonly Dictionary<string, IJobHandler> _handlers = new();
    private readonly QueueEngineOptions _options;
    private readonly ILogger<QueueEngine> _logger;
    private bool _started;

    public QueueEngine(
        IJobRepository repository,
        IQueueWorkerPool workerPool,
        QueueEngineOptions options,
        ILogger<QueueEngine> logger)
    {
        _repository = repository;
        _workerPool = workerPool;
        _options = options;
        _logger = logger;
    }

    public void RegisterHandler(IJobHandler handler)
    {
        _handlers[handler.JobType] = handler;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        
        await _repository.InitializeAsync();
        
        if (_workerPool is QueueWorkerPool pool)
        {
            pool.SetHandlers(_handlers);
        }
        
        _workerPool.Start();
        _started = true;
        
        _logger.LogInformation("QueueEngine started with {QueueCount} queue(s).", _options.Queues.Count);
    }

    public Task StopAsync()
    {
        _workerPool.Stop();
        return Task.CompletedTask;
    }

    public async Task<Guid> EnqueueAsync(string jobType, object payload, string queue = "default", DateTime? scheduledAt = null)
    {
        var job = new QueueJob
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Queue = queue,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            Status = JobStatus.Pending,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow
        };

        return await _repository.EnqueueAsync(job);
    }

    public Task<Dictionary<string, (int Pending, int Running, int Done, int Failed)>> GetStatsAsync()
    {
        return _workerPool.GetAllStatsAsync();
    }
}
