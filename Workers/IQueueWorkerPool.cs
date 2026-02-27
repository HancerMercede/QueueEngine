namespace QueueEngine.Workers;

public interface IQueueWorkerPool
{
    void SetHandlers(Dictionary<string, IJobHandler> handlers);
    void Start();
    void Stop();
    Task<Dictionary<string, (int Pending, int Running, int Done, int Failed, int Cancelled)>> GetAllStatsAsync();
    Task PauseQueueAsync(string queue);
    Task ResumeQueueAsync(string queue);
    Task<bool> IsQueuePausedAsync(string queue);
}
