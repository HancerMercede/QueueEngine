using QueueEngine.Models;

namespace QueueEngine.Data;

public interface IJobRepository
{
    Task InitializeAsync();
    Task<Guid> EnqueueAsync(QueueJob job);
    Task<QueueJob?> DequeueAsync(string queue);
    Task CompleteAsync(Guid jobId, JobStatus status, string? errorMessage = null);
    Task<bool> RequestCancellationAsync(Guid jobId);
    Task<QueueJob?> GetJobAsync(Guid jobId);
    Task<(int Pending, int Running, int Done, int Failed, int Cancelled)> GetStatsAsync(string queue);
}
