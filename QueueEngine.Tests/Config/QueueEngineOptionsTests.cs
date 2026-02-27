using QueueEngine.Config;
using Xunit;

namespace QueueEngine.Tests.Config;

public class QueueEngineOptionsTests
{
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
            DatabaseProvider = "sqlite"
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithInvalidDatabaseProvider_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "invalid"
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithEmptyQueues_ShouldThrow()
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
    public void QueueOptions_DefaultValues()
    {
        var options = new QueueOptions();

        Assert.Equal(1, options.Concurrency);
        Assert.Equal(10, options.RateLimitPerSecond);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(5, options.RetryDelaySeconds);
        Assert.True(options.EnableDeadLetterQueue);
    }

    [Fact]
    public void ClusterOptions_DefaultValues()
    {
        var options = new ClusterOptions();

        Assert.False(options.Enabled);
        Assert.Equal(30, options.HeartbeatIntervalSeconds);
        Assert.Equal(300, options.StaleJobTimeoutSeconds);
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
                ["default"] = new QueueOptions { Concurrency = 0, RateLimitPerSecond = 10 }
            }
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithNegativeConcurrency_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = -1, RateLimitPerSecond = 10 }
            }
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Validate_WithZeroRateLimit_ShouldThrow()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 0 }
            }
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void QueueOptions_NewProperties_HaveDefaults()
    {
        var options = new QueueOptions();

        Assert.Equal(10, options.MaxPriority);
        Assert.False(options.StartPaused);
    }

    [Fact]
    public void ClusterOptions_WithEnabled_GeneratesWorkerId()
    {
        var options = new ClusterOptions
        {
            Enabled = true
        };
        options.Validate();

        Assert.NotNull(options.WorkerId);
        Assert.NotEmpty(options.WorkerId);
    }

    [Fact]
    public void ClusterOptions_WithLowHeartbeatInterval_ShouldThrow()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            HeartbeatIntervalSeconds = 2
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void ClusterOptions_WithLowStaleJobTimeout_ShouldThrow()
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            StaleJobTimeoutSeconds = 10
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }
}
