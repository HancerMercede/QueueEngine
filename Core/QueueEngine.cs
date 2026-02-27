using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Core;

public class QueueEngine(
    IJobRepository repository,
    IQueueWorkerPool workerPool,
    QueueEngineOptions options,
    ILogger<QueueEngine> logger) : IQueueEngine
{
    private readonly IJobRepository _repository = repository;
    private readonly IQueueWorkerPool _workerPool = workerPool;
    private readonly Dictionary<string, IJobHandler> _handlers = new();
    private readonly QueueEngineOptions _options = options;
    private readonly ILogger<QueueEngine> _logger = logger;
    private bool _started;

    private static readonly Regex SafeNameRegex = new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);
    private const int MaxPayloadSize = 1024 * 1024;

    public void RegisterHandler(IJobHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        
        if (!IsValidName(handler.JobType))
            throw new ArgumentException($"Invalid job type: {handler.JobType}. Only alphanumeric, dash, underscore allowed.");
        
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
        ValidateJobType(jobType);
        ValidateQueueName(queue);
        
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        
        if (payloadJson.Length > MaxPayloadSize)
            throw new ArgumentException($"Payload exceeds maximum size of {MaxPayloadSize} bytes");
        
        var job = new QueueJob
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Queue = queue,
            Payload = payloadJson,
            Status = JobStatus.Pending,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow
        };

        return await _repository.EnqueueAsync(job);
    }

    public async Task BulkEnqueueAsync(IEnumerable<(string JobType, object Payload, string Queue)> jobs)
    {
        var jobList = jobs.ToList();
        if (!jobList.Any()) return;

        var queueJobs = new List<QueueJob>();
        
        foreach (var (jobType, payload, queue) in jobList)
        {
            ValidateJobType(jobType);
            ValidateQueueName(queue);
            
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            
            if (payloadJson.Length > MaxPayloadSize)
                throw new ArgumentException($"Payload exceeds maximum size of {MaxPayloadSize} bytes");
            
            queueJobs.Add(new QueueJob
            {
                Id = Guid.NewGuid(),
                JobType = jobType,
                Queue = queue,
                Payload = payloadJson,
                Status = JobStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _repository.BulkEnqueueAsync(queueJobs);
    }

    public async Task<bool> CancelJobAsync(Guid jobId)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("Job ID cannot be empty", nameof(jobId));
        
        return await _repository.RequestCancellationAsync(jobId);
    }

    public Task<QueueJob?> GetJobAsync(Guid jobId)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("Job ID cannot be empty", nameof(jobId));
        
        return _repository.GetJobAsync(jobId);
    }

    public async Task MoveToDeadLetterAsync(Guid jobId)
    {
        if (jobId == Guid.Empty)
            throw new ArgumentException("Job ID cannot be empty", nameof(jobId));
        
        var job = await _repository.GetJobAsync(jobId);
        if (job == null)
            throw new InvalidOperationException("Job not found");
        
        var queueConfig = _options.Queues.GetValueOrDefault(job.Queue);
        if (queueConfig?.EnableDeadLetterQueue != true)
            return;
        
        await _repository.MoveToDeadLetterAsync(jobId, job.ErrorMessage);
        _logger.LogInformation("Job {JobId} moved to dead letter queue", jobId);
    }

    public async Task<IEnumerable<QueueJob>> GetDeadLetterJobsAsync(string queue)
    {
        ValidateQueueName(queue);
        return await _repository.GetDeadLetterJobsAsync(queue);
    }

    public Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetStatsAsync()
    {
        return _workerPool.GetAllStatsAsync();
    }

    private static void ValidateJobType(string jobType)
    {
        if (string.IsNullOrWhiteSpace(jobType))
            throw new ArgumentException("Job type cannot be empty", nameof(jobType));
        
        if (jobType.Length > 255)
            throw new ArgumentException("Job type exceeds maximum length of 255 characters", nameof(jobType));
        
        if (!IsValidName(jobType))
            throw new ArgumentException($"Invalid job type: {jobType}. Only alphanumeric, dash, underscore allowed.", nameof(jobType));
    }

    private static void ValidateQueueName(string queue)
    {
        if (string.IsNullOrWhiteSpace(queue))
            throw new ArgumentException("Queue name cannot be empty", nameof(queue));
        
        if (queue.Length > 100)
            throw new ArgumentException("Queue name exceeds maximum length of 100 characters", nameof(queue));
        
        if (!IsValidName(queue))
            throw new ArgumentException($"Invalid queue name: {queue}. Only alphanumeric, dash, underscore allowed.", nameof(queue));
    }

    private static bool IsValidName(string name) => SafeNameRegex.IsMatch(name);
}
