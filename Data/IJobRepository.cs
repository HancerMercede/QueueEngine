using QueueEngine.Models;

namespace QueueEngine.Data;

public interface IJobRepository
{
    Task InitializeAsync();
    Task<Guid> EnqueueAsync(QueueJob job);
    Task<QueueJob?> DequeueAsync(string queue);
    Task CompleteAsync(Guid jobId, JobStatus status, string? errorMessage = null);
    Task<(int Pending, int Running, int Done, int Failed)> GetStatsAsync(string queue);
}
