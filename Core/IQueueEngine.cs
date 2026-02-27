using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Core;

public interface IQueueEngine
{
    void RegisterHandler(IJobHandler handler);
    Task StartAsync();
    Task StopAsync();
    Task<Guid> EnqueueAsync(string jobType, object payload, string queue = "default", DateTime? scheduledAt = null);
    Task BulkEnqueueAsync(IEnumerable<(string JobType, object Payload, string Queue)> jobs);
    Task<bool> CancelJobAsync(Guid jobId);
    Task<QueueJob?> GetJobAsync(Guid jobId);
    Task MoveToDeadLetterAsync(Guid jobId);
    Task<IEnumerable<QueueJob>> GetDeadLetterJobsAsync(string queue);
    Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetStatsAsync();
}
