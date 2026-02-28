using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Models;

[MemoryDiagnoser]
public class QueueBenchmarks
{
    private JobRepository? _repository;
    private const string ConnectionString = "Data Source=benchmark.db";
    
    [GlobalSetup]
    public async Task Setup()
    {
        var options = new QueueEngineOptions
        {
            ConnectionString = ConnectionString,
            DatabaseProvider = "sqlite"
        };
        
        _repository = new JobRepository(options);
        await _repository.InitializeAsync();
        
        for (int i = 0; i < 1000; i++)
        {
            await _repository.EnqueueAsync(new QueueJob
            {
                Id = Guid.NewGuid(),
                JobType = "benchmark-job",
                Queue = "default",
                Payload = "{\"data\":\"test\"}",
                Status = JobStatus.Pending,
                Priority = 0,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
    
    [Benchmark]
    public async Task Enqueue_SingleJob()
    {
        await _repository!.EnqueueAsync(new QueueJob
        {
            Id = Guid.NewGuid(),
            JobType = "benchmark-job",
            Queue = "default",
            Payload = "{\"data\":\"test\"}",
            Status = JobStatus.Pending,
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        });
    }
    
    [Benchmark]
    public async Task Dequeue_Job()
    {
        await _repository!.DequeueAsync("default");
    }
    
    [Benchmark]
    public async Task GetStats()
    {
        await _repository!.GetStatsAsync("default");
    }
    
    [Benchmark]
    public async Task BulkEnqueue_100Jobs()
    {
        var jobs = Enumerable.Range(0, 100).Select(i => new QueueJob
        {
            Id = Guid.NewGuid(),
            JobType = "benchmark-job",
            Queue = "default",
            Payload = "{\"data\":\"test\"}",
            Status = JobStatus.Pending,
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        });
        
        await _repository!.BulkEnqueueAsync(jobs);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists("benchmark.db"))
            File.Delete("benchmark.db");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<QueueBenchmarks>();
    }
}
