using Microsoft.Extensions.Logging;
using Moq;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;
using Xunit;

namespace QueueEngine.Tests.Core;

public class QueueEngineTests
{
    private readonly Mock<IJobRepository> _mockRepository;
    private readonly Mock<IQueueWorkerPool> _mockWorkerPool;
    private readonly Mock<ILogger<QueueEngine.Core.QueueEngine>> _mockLogger;
    private readonly QueueEngineOptions _options;
    private readonly QueueEngine.Core.QueueEngine _engine;

    public QueueEngineTests()
    {
        _mockRepository = new Mock<IJobRepository>();
        _mockWorkerPool = new Mock<IQueueWorkerPool>();
        _mockLogger = new Mock<ILogger<QueueEngine.Core.QueueEngine>>();
        _options = new QueueEngineOptions
        {
            ConnectionString = "Data Source=test.db",
            DatabaseProvider = "sqlite",
            Queues = new Dictionary<string, QueueOptions>
            {
                ["default"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 10 }
            }
        };

        _engine = new QueueEngine.Core.QueueEngine(
            _mockRepository.Object,
            _mockWorkerPool.Object,
            _options,
            _mockLogger.Object);
    }

    [Fact]
    public async Task StartAsync_ShouldInitializeRepository()
    {
        _mockRepository.Setup(r => r.InitializeAsync()).Returns(Task.CompletedTask);
        
        await _engine.StartAsync();

        _mockRepository.Verify(r => r.InitializeAsync(), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldStopWorkers()
    {
        await _engine.StopAsync();

        _mockWorkerPool.Verify(w => w.Stop(), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WithValidData_ShouldReturnJobId()
    {
        var expectedId = Guid.NewGuid();
        _mockRepository.Setup(r => r.EnqueueAsync(It.IsAny<QueueJob>()))
            .ReturnsAsync(expectedId);

        var result = await _engine.EnqueueAsync("test-job", new { Message = "hello" });

        Assert.Equal(expectedId, result);
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyJobType_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("", new { Message = "hello" }));
    }

    [Fact]
    public async Task EnqueueAsync_WithInvalidJobType_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("invalid job type!", new { Message = "hello" }));
    }

    [Fact]
    public async Task CancelJobAsync_WithEmptyId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.CancelJobAsync(Guid.Empty));
    }

    [Fact]
    public async Task GetJobAsync_WithEmptyId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.GetJobAsync(Guid.Empty));
    }
}
