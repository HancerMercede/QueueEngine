using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Core;

public interface IQueueEngine
{
    void RegisterHandler(IJobHandler handler);
    Task StartAsync();
    Task StopAsync();
    Task<Guid> EnqueueAsync(string jobType, object payload, string queue = "default", DateTime? scheduledAt = null);
    Task<Dictionary<string, (int Pending, int Running, int Done, int Failed)>> GetStatsAsync();
}
