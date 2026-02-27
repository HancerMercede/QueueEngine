using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Core;

public interface IQueueEngine
{
    void RegisterHandler(IJobHandler handler);
    Task StartAsync();
    Task StopAsync();
    Task<Guid> EnqueueAsync(string jobType, object payload, string queue = "default", DateTime? scheduledAt = null, int priority = 0);
    Task BulkEnqueueAsync(IEnumerable<(string JobType, object Payload, string Queue, int Priority)> jobs);
    Task<bool> CancelJobAsync(Guid jobId);
    Task<QueueJob?> GetJobAsync(Guid jobId);
    Task MoveToDeadLetterAsync(Guid jobId);
    Task<IEnumerable<QueueJob>> GetDeadLetterJobsAsync(string queue);
    Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetStatsAsync();
    Task<IEnumerable<WorkerInfo>> GetActiveWorkersAsync();
    Task PauseQueueAsync(string queue);
    Task ResumeQueueAsync(string queue);
    Task<bool> IsQueuePausedAsync(string queue);
    Task<(int Pending, int Running, int Done, int Failed, int Cancelled)> GetQueueStatsAsync(string queue);
}
