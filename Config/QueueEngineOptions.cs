namespace QueueEngine.Config;

public class QueueEngineOptions
{
    public string ConnectionString { get; set; } = "Data Source=queue.db";
    public string DatabaseProvider { get; set; } = "sqlite";
    public Dictionary<string, QueueOptions> Queues { get; set; } = new()
    {
        ["default"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 10 }
    };
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    
    public ClusterOptions Cluster { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString is required", nameof(ConnectionString));
        
        if (DatabaseProvider != "sqlite" && DatabaseProvider != "postgres")
            throw new ArgumentException("DatabaseProvider must be 'sqlite' or 'postgres'", nameof(DatabaseProvider));
        
        if (Queues == null || Queues.Count == 0)
            throw new ArgumentException("At least one queue must be configured", nameof(Queues));
        
        foreach (var (name, options) in Queues)
        {
            if (options.Concurrency <= 0)
                throw new ArgumentException($"Queue '{name}': Concurrency must be greater than 0");
            if (options.RateLimitPerSecond <= 0)
                throw new ArgumentException($"Queue '{name}': RateLimitPerSecond must be greater than 0");
        }
        
        Cluster.Validate();
    }
}

public class QueueOptions
{
    public int Concurrency { get; set; } = 1;
    public int RateLimitPerSecond { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public bool EnableDeadLetterQueue { get; set; } = true;
    public string DeadLetterQueueName { get; set; } = "dead-letter";
}

public class ClusterOptions
{
    public bool Enabled { get; set; } = false;
    public string WorkerId { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int StaleJobTimeoutSeconds { get; set; } = 300;
    public bool EnableJobStealing { get; set; } = true;

    internal void Validate()
    {
        if (Enabled && string.IsNullOrWhiteSpace(WorkerId))
        {
            WorkerId = $"worker-{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
        }
        
        if (HeartbeatIntervalSeconds < 5)
            throw new ArgumentException("HeartbeatIntervalSeconds must be at least 5");
        
        if (StaleJobTimeoutSeconds < 30)
            throw new ArgumentException("StaleJobTimeoutSeconds must be at least 30");
    }
}
