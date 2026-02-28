using QueueEngine.Config;

namespace QueueEngine.Tests.Extensions;

public class QueueOptionsTests
{
    [Fact]
    public void QueueOptions_DefaultValues()
    {
        var options = new QueueOptions();
        
        Assert.Equal(1, options.Concurrency);
        Assert.Equal(10, options.RateLimitPerSecond);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(5, options.RetryDelaySeconds);
        Assert.True(options.EnableDeadLetterQueue);
        Assert.Equal("dead-letter", options.DeadLetterQueueName);
        Assert.Equal(10, options.MaxPriority);
        Assert.False(options.StartPaused);
    }

    [Fact]
    public void SchedulerOptions_DefaultValues()
    {
        var options = new SchedulerOptions();
        
        Assert.False(options.Enabled);
        Assert.Equal(10, options.CheckIntervalSeconds);
    }

    [Fact]
    public void QueueEngineOptions_DefaultValues()
    {
        var options = new QueueEngineOptions();
        
        Assert.Equal("Data Source=queue.db", options.ConnectionString);
        Assert.Equal("sqlite", options.DatabaseProvider);
        Assert.NotNull(options.Queues);
        Assert.Contains("default", options.Queues.Keys);
    }

    [Fact]
    public void Validate_WithValidOptions_ShouldNotThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 10 }
            }
        };
        
        options.Validate();
    }

    [Fact]
    public void Validate_WithEmptyConnectionString_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions()
            }
        };
        
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithInvalidDatabaseProvider_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "mysql",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions()
            }
        };
        
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithNoQueues_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>()
        };
        
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithZeroConcurrency_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 0 }
            }
        };
        
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithPostgres_ShouldWork()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Host=localhost;Database=queue",
            DatabaseProvider = "postgres",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 1 }
            }
        };
        
        options.Validate();
    }
}
