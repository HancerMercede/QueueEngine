using QueueEngine.Models;
using QueueEngine.Scheduling;

namespace QueueEngine.Data;

public interface IJobRepository
{
    Task InitializeAsync();
    Task<Guid> EnqueueAsync(QueueJob job);
    Task<QueueJob?> DequeueAsync(string queue, string? workerId = null);
    Task CompleteAsync(Guid jobId, JobStatus status, string? errorMessage = null);
    Task<bool> RequestCancellationAsync(Guid jobId);
    Task<QueueJob?> GetJobAsync(Guid jobId);
    Task RequeueAsync(Guid jobId, int retryCount, string? errorMessage = null);
    Task BulkEnqueueAsync(IEnumerable<QueueJob> jobs);
    Task MoveToDeadLetterAsync(Guid jobId, string? errorMessage = null);
    Task<IEnumerable<QueueJob>> GetDeadLetterJobsAsync(string queue);
    Task RecoverStaleJobsAsync(string workerId, int staleTimeoutSeconds);
    Task RegisterWorkerAsync(string workerId, string[] queues, int heartbeatIntervalSeconds);
    Task HeartbeatAsync(string workerId);
    Task UnregisterWorkerAsync(string workerId);
    Task<IEnumerable<WorkerInfo>> GetActiveWorkersAsync();
    Task<(int Pending, int Running, int Done, int Failed, int Cancelled)> GetStatsAsync(string queue);
    Task UpdateProgressAsync(Guid jobId, int progress);
    Task<bool> IsQueuePausedAsync(string queue);
    Task SetQueuePausedAsync(string queue, bool paused);
    
    Task SaveScheduleAsync(JobSchedule schedule);
    Task UpdateScheduleAsync(JobSchedule schedule);
    Task DeleteScheduleAsync(string scheduleId);
    Task<IEnumerable<JobSchedule>> GetSchedulesAsync(string? queue = null);
    Task<IEnumerable<JobSchedule>> GetDueSchedulesAsync();
}

public class WorkerInfo
{
    public string WorkerId { get; set; } = string.Empty;
    public string[] Queues { get; set; } = [];
    public DateTime LastHeartbeat { get; set; }
    public bool IsActive { get; set; }
}
