using Microsoft.Extensions.Logging;
using Moq;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Tests.Core;

public class IQueueEngineTests : IDisposable
{
    private readonly Mock<IJobRepository> _mockRepository;
    private readonly Mock<IQueueWorkerPool> _mockWorkerPool;
    private readonly Mock<ILogger<QueueEngine.Core.QueueEngine>> _mockLogger;
    private readonly QueueEngineOptions _options;
    private readonly QueueEngine.Core.QueueEngine _engine;

    public IQueueEngineTests()
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
                ["default"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 10, MaxPriority = 50 },
                ["priority"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 20, MaxPriority = 100 }
            }
        };

        _engine = new QueueEngine.Core.QueueEngine(
            _mockRepository.Object,
            _mockWorkerPool.Object,
            _options,
            _mockLogger.Object);
    }

    public void Dispose() { }

    [Fact]
    public void RegisterHandler_WithValidType_ShouldSucceed()
    {
        var handler = new TestJobHandler();
        
        _engine.RegisterHandler(handler);
    }

    [Fact]
    public void RegisterHandler_WithInvalidType_ShouldThrow()
    {
        var handler = new InvalidJobHandler();
        
        Assert.Throws<ArgumentException>(() => _engine.RegisterHandler(handler));
    }

    [Fact]
    public void RegisterHandler_WithNull_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.RegisterHandler(null!));
    }

    [Fact]
    public async Task EnqueueAsync_WithPriority_ShouldSucceed()
    {
        var expectedId = Guid.NewGuid();
        _mockRepository.Setup(r => r.EnqueueAsync(It.IsAny<QueueJob>()))
            .ReturnsAsync(expectedId);

        var result = await _engine.EnqueueAsync("test-job", new { Message = "hello" }, "default", null, 25);

        Assert.Equal(expectedId, result);
        _mockRepository.Verify(r => r.EnqueueAsync(It.Is<QueueJob>(j => j.Priority == 25)), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WithPriorityExceedingMax_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("test-job", new { Message = "hello" }, "default", null, 100));
    }

    [Fact]
    public async Task EnqueueAsync_WithNegativePriority_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("test-job", new { Message = "hello" }, "default", null, -1));
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyJobType_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("", new { Message = "hello" }));
    }

    [Fact]
    public async Task EnqueueAsync_WithWhitespaceJobType_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("  ", new { Message = "hello" }));
    }

    [Fact]
    public async Task EnqueueAsync_WithInvalidJobTypeCharacters_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("invalid job type!", new { Message = "hello" }));
    }

    [Fact]
    public async Task EnqueueAsync_WithLongJobType_ShouldThrow()
    {
        var longJobType = new string('a', 256);
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync(longJobType, new { Message = "hello" }));
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("test-job", new { Message = "hello" }, ""));
    }

    [Fact]
    public async Task EnqueueAsync_WithLongQueueName_ShouldThrow()
    {
        var longQueueName = new string('a', 101);
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("test-job", new { Message = "hello" }, longQueueName));
    }

    [Fact]
    public async Task EnqueueAsync_WithLargePayload_ShouldThrow()
    {
        var largePayload = new { Data = new string('x', 1024 * 1024 + 1) };
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.EnqueueAsync("test-job", largePayload));
    }

    [Fact]
    public async Task BulkEnqueueAsync_WithPriority_ShouldSucceed()
    {
        var jobs = new List<(string, object, string, int)>
        {
            ("job1", new { Data = "1" }, "default", 10),
            ("job2", new { Data = "2" }, "default", 5),
            ("job3", new { Data = "3" }, "default", 15)
        };

        _mockRepository.Setup(r => r.BulkEnqueueAsync(It.IsAny<IEnumerable<QueueJob>>()))
            .Returns(Task.CompletedTask);

        await _engine.BulkEnqueueAsync(jobs);

        _mockRepository.Verify(r => r.BulkEnqueueAsync(It.Is<IEnumerable<QueueJob>>(
            j => j.Count() == 3)), Times.Once);
    }

    [Fact]
    public async Task BulkEnqueueAsync_WithEmptyList_ShouldNotCallRepository()
    {
        await _engine.BulkEnqueueAsync([]);

        _mockRepository.Verify(r => r.BulkEnqueueAsync(It.IsAny<IEnumerable<QueueJob>>()), Times.Never);
    }

    [Fact]
    public async Task CancelJobAsync_WithEmptyId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.CancelJobAsync(Guid.Empty));
    }

    [Fact]
    public async Task CancelJobAsync_WithValidId_ShouldReturnTrue()
    {
        _mockRepository.Setup(r => r.RequestCancellationAsync(It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var result = await _engine.CancelJobAsync(Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task GetJobAsync_WithEmptyId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.GetJobAsync(Guid.Empty));
    }

    [Fact]
    public async Task GetJobAsync_WithValidId_ShouldReturnJob()
    {
        var jobId = Guid.NewGuid();
        var expectedJob = new QueueJob { Id = jobId, JobType = "test", Queue = "default", Payload = "{}" };
        _mockRepository.Setup(r => r.GetJobAsync(jobId))
            .ReturnsAsync(expectedJob);

        var result = await _engine.GetJobAsync(jobId);

        Assert.NotNull(result);
        Assert.Equal(jobId, result.Id);
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_WithEmptyId_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.MoveToDeadLetterAsync(Guid.Empty));
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_WhenJobNotFound_ShouldThrow()
    {
        _mockRepository.Setup(r => r.GetJobAsync(It.IsAny<Guid>()))
            .ReturnsAsync((QueueJob?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _engine.MoveToDeadLetterAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDeadLetterJobsAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.GetDeadLetterJobsAsync(""));
    }

    [Fact]
    public async Task PauseQueueAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.PauseQueueAsync(""));
    }

    [Fact]
    public async Task ResumeQueueAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.ResumeQueueAsync(""));
    }

    [Fact]
    public async Task IsQueuePausedAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.IsQueuePausedAsync(""));
    }

    [Fact]
    public async Task GetQueueStatsAsync_WithInvalidQueue_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _engine.GetQueueStatsAsync(""));
    }

    [Fact]
    public async Task StartAsync_ShouldInitializeRepository()
    {
        _mockRepository.Setup(r => r.InitializeAsync())
            .Returns(Task.CompletedTask);
        
        _mockWorkerPool.Setup(w => w.Start());

        await _engine.StartAsync();

        _mockRepository.Verify(r => r.InitializeAsync(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldCallWorkerPoolStart()
    {
        _mockRepository.Setup(r => r.InitializeAsync())
            .Returns(Task.CompletedTask);
        
        _mockWorkerPool.Setup(w => w.Start());

        await _engine.StartAsync();

        _mockWorkerPool.Verify(w => w.Start(), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldStopWorkers()
    {
        _mockWorkerPool.Setup(w => w.Stop());

        await _engine.StopAsync();

        _mockWorkerPool.Verify(w => w.Stop(), Times.Once);
    }

    private class TestJobHandler : JobHandler<TestPayload>
    {
        public override string JobType => "test-job";

        protected override Task HandleAsync(QueueJob job, TestPayload payload, IProgress<int>? progress, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private class InvalidJobHandler : JobHandler<TestPayload>
    {
        public override string JobType => "invalid job type!";

        protected override Task HandleAsync(QueueJob job, TestPayload payload, IProgress<int>? progress, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private record TestPayload(string Data);
}
