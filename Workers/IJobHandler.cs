using QueueEngine.Models;

namespace QueueEngine.Workers;

public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(QueueJob job, string payload, CancellationToken ct);
}

public abstract class JobHandler<T> : IJobHandler
{
    public abstract string JobType { get; }
    
    protected abstract Task HandleAsync(QueueJob job, T payload, CancellationToken ct);

    public async Task HandleAsync(QueueJob job, string payload, CancellationToken ct)
    {
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<T>(payload)
            ?? throw new InvalidOperationException("Failed to deserialize payload");
        await HandleAsync(job, deserialized, ct);
    }
}
