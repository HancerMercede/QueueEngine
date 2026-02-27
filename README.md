# QueueEngine

A lightweight, production-ready queue engine for .NET 10 with support for SQLite and PostgreSQL.

> **📖 For detailed usage guide, see [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md)**

## Features

- **Multiple Queues**: Support for multiple named queues with individual concurrency and rate limiting
- **Dual Database Support**: SQLite for development, PostgreSQL for production
- **Job Handlers**: Strongly-typed job handlers with automatic JSON serialization
- **Scheduled Jobs**: Support for delayed/scheduled job execution
- **Rate Limiting**: Per-queue rate limiting to control throughput
- **Retry Mechanism**: Automatic retry with exponential backoff for failed jobs
- **Dead Letter Queue**: Move permanently failed jobs for analysis
- **Job Cancellation**: Cancel pending or running jobs
- **Bulk Enqueue**: Enqueue multiple jobs at once
- **Health Checks**: ASP.NET Core health check integration
- **Dependency Injection**: Full DI support for integration with ASP.NET Core
- **Logging**: Structured logging using Microsoft.Extensions.Logging
- **Testable**: Interfaces for all core components for easy unit testing

## Installation

```bash
dotnet add package QueueEngine
```

Or install dependencies manually:

```bash
dotnet add package Dapper
dotnet add package Microsoft.Data.Sqlite
dotnet add package Npgsql
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Hosting.Abstractions
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.Console
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks
```

## Quick Start

### Console Application

```csharp
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Workers;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<QueueEngine.Core.QueueEngine>();

var options = new QueueEngineOptions
{
    ConnectionString = "Data Source=queue.db",
    DatabaseProvider = "sqlite",
    Queues = new Dictionary<string, QueueOptions>
    {
        ["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 10 }
    }
};

options.Validate();

var repository = new JobRepository(options);
var workerPool = new QueueWorkerPool(repository, new Dictionary<string, IJobHandler>(), options, loggerFactory.CreateLogger<QueueWorkerPool>());
var engine = new QueueEngine.Core.QueueEngine(repository, workerPool, options, logger);

engine.RegisterHandler(new MyJobHandler());

await engine.EnqueueAsync("my-job", new MyPayload("Hello World"));
await engine.StartAsync();

await Task.Delay(5000);

await engine.StopAsync();
```

### ASP.NET Core Integration

```csharp
// Program.cs
builder.Services
    .AddQueueEngine(opts =>
    {
        opts.ConnectionString = builder.Configuration.GetConnectionString("Queue")!;
        opts.DatabaseProvider = "postgres";
        opts.Queues["default"] = new QueueOptions
        {
            Concurrency = 4,
            RateLimitPerSecond = 20
        };
    })
    .AddJobHandler<EmailJobHandler>()
    .AddQueueEngineHostedService();

// Add health checks
builder.Services.AddHealthChecks()
    .AddQueueEngineHealthCheck();
```

## Creating Job Handlers

### Step 1: Define Your Payload

```csharp
public record SendEmailPayload(string To, string Subject, string Body);
```

### Step 2: Create the Handler

```csharp
using QueueEngine.Models;
using QueueEngine.Workers;

public class EmailJobHandler : JobHandler<SendEmailPayload>
{
    public override string JobType => "send-email";

    protected override async Task HandleAsync(QueueJob job, SendEmailPayload payload, CancellationToken ct)
    {
        Console.WriteLine($"Sending email to {payload.To}");
        await Task.Delay(500, ct);
    }
}
```

### Step 3: Register and Use

```csharp
engine.RegisterHandler(new EmailJobHandler());
await engine.EnqueueAsync("send-email", new SendEmailPayload("user@example.com", "Hello", "World!"));
```

## API Reference

### QueueEngine Methods

```csharp
// Enqueue a single job
await engine.EnqueueAsync("job-type", payload, "queue-name", scheduledAt: DateTime.UtcNow.AddHours(1));

// Bulk enqueue
await engine.BulkEnqueueAsync(new[] {
    ("send-email", new EmailPayload(...), "default"),
    ("notify-user", new NotificationPayload(...), "critical")
});

// Cancel a job
await engine.CancelJobAsync(jobId);

// Get job status
var job = await engine.GetJobAsync(jobId);

// Move to dead letter
await engine.MoveToDeadLetterAsync(jobId);

// Get dead letter jobs
var deadJobs = await engine.GetDeadLetterJobsAsync("default");

// Get queue stats
var stats = await engine.GetStatsAsync();
```

## Configuration

### QueueEngineOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| ConnectionString | string | "Data Source=queue.db" | Database connection string |
| DatabaseProvider | string | "sqlite" | "sqlite" or "postgres" |
| Queues | Dictionary | default queue | Queue configurations |

### QueueOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Concurrency | int | 1 | Number of concurrent workers |
| RateLimitPerSecond | int | 10 | Maximum jobs per second |
| MaxRetries | int | 3 | Maximum retry attempts |
| RetryDelaySeconds | int | 5 | Base delay between retries |
| EnableDeadLetterQueue | bool | true | Move failed jobs to DLQ |
| DeadLetterQueueName | string | "dead-letter" | DLQ queue name |

## Architecture

```
QueueEngine/
├── Config/
│   └── QueueEngineOptions.cs       # Configuration classes
├── Core/
│   ├── IQueueEngine.cs             # Engine interface
│   └── QueueEngine.cs              # Main engine implementation
├── Data/
│   ├── IJobRepository.cs           # Repository interface
│   └── JobRepository.cs            # Database operations
├── Models/
│   └── QueueJob.cs                  # Job entity
├── Workers/
│   ├── IJobHandler.cs              # Handler interface
│   ├── IQueueWorkerPool.cs         # Worker pool interface
│   ├── QueueWorkerPool.cs          # Worker management
│   └── RateLimiter.cs              # Rate limiting
├── Handlers/
│   └── ExampleHandlers.cs          # Example handlers
├── QueueEngineExtensions.cs        # DI extensions
├── QueueEngineHealthCheck.cs       # Health check
└── Program.cs                      # Demo application
```

## Security

### Input Validation
- Job types and queue names validated (alphanumeric, dash, underscore only)
- Maximum length limits (255 chars job type, 100 chars queue)
- Payload size limited to 1MB

### SQL Injection Protection
- Parameterized queries via Dapper
- No string concatenation in SQL

### Error Message Sanitization
- Truncated to 500 chars
- Stack traces never exposed

## Database Recommendations

| Environment | Database | Notes |
|-------------|----------|-------|
| Development | SQLite | Lightweight, no setup required |
| Production | PostgreSQL | Recommended for concurrent workloads |

> **Warning**: SQLite is not suitable for high-concurrency production. Always use PostgreSQL.

## License

MIT
