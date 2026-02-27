using Microsoft.Extensions.Logging;
using Moq;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Tests.Workers;

public class QueueWorkerPoolTests : IDisposable
{
    private readonly Mock<IJobRepository> _mockRepository;
    private readonly Mock<ILogger<QueueWorkerPool>> _mockLogger;
    private readonly QueueEngineOptions _options;
    private readonly QueueWorkerPool _pool;

    public QueueWorkerPoolTests()
    {
        _mockRepository = new Mock<IJobRepository>();
        _mockLogger = new Mock<ILogger<QueueWorkerPool>>();
        _options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 100 },
                ["priority"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 50 }
            }
        };

        _pool = new QueueWorkerPool(
            _mockRepository.Object,
            new Dictionary<string, IJobHandler>(),
            _options,
            _mockLogger.Object);
    }

    public void Dispose()
    {
    }

    [Fact]
    public void SetHandlers_ShouldStoreHandlers()
    {
        var handler = new TestJobHandler();
        
        _pool.SetHandlers(new Dictionary<string, IJobHandler>
        {
            ["test-job"] = handler
        });

        var handlers = new Dictionary<string, IJobHandler>();
        _pool.SetHandlers(handlers);
    }

    [Fact]
    public async Task GetAllStatsAsync_ShouldReturnStatsForAllQueues()
    {
        _mockRepository.Setup(r => r.GetStatsAsync("default"))
            .ReturnsAsync((1, 2, 3, 0, 0));
        _mockRepository.Setup(r => r.GetStatsAsync("priority"))
            .ReturnsAsync((5, 6, 7, 1, 0));

        var stats = await _pool.GetAllStatsAsync();

        Assert.Equal(2, stats.Count);
        Assert.Equal((1, 2, 3, 0, 0), stats["default"]);
        Assert.Equal((5, 6, 7, 1, 0), stats["priority"]);
    }

    [Fact]
    public async Task PauseQueueAsync_ShouldPauseQueue()
    {
        _mockRepository.Setup(r => r.GetStatsAsync(It.IsAny<string>()))
            .ReturnsAsync((0, 0, 0, 0, 0));

        await _pool.PauseQueueAsync("default");
        
        var isPaused = await _pool.IsQueuePausedAsync("default");
        
        Assert.True(isPaused);
    }

    [Fact]
    public async Task PauseQueueAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _pool.PauseQueueAsync("non-existent"));
    }

    [Fact]
    public async Task ResumeQueueAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _pool.ResumeQueueAsync("non-existent"));
    }

    [Fact]
    public async Task IsQueuePausedAsync_ForUnpausedQueue_ShouldReturnFalse()
    {
        var isPaused = await _pool.IsQueuePausedAsync("default");
        
        Assert.False(isPaused);
    }

    private class TestJobHandler : JobHandler<TestPayload>
    {
        public override string JobType => "test-job";

        protected override Task HandleAsync(QueueJob job, TestPayload payload, IProgress<int>? progress, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private record TestPayload(string Data);
}
