namespace QueueEngine.Models;

public class QueueJob
{
    public Guid Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string Queue { get; set; } = "default";
    public string Payload { get; set; } = "{}";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Priority { get; set; } = 0;
    public int Progress { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool CancellationRequested { get; set; }
}

public enum JobStatus
{
    Pending,
    Running,
    Done,
    Failed,
    Cancelled
}
