# QueueEngine

A lightweight, production-ready queue engine for .NET 10 with support for SQLite and PostgreSQL.

## Features

- **Multiple Queues**: Support for multiple named queues with individual concurrency and rate limiting
- **Dual Database Support**: SQLite for development, PostgreSQL for production
- **Job Handlers**: Strongly-typed job handlers with automatic JSON serialization
- **Scheduled Jobs**: Support for delayed/scheduled job execution
- **Rate Limiting**: Per-queue rate limiting to control throughput
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
```

## Quick Start

### Console Application

```csharp
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Workers;

// Create logger
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<QueueEngine.Core.QueueEngine>();

// Configure options
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

// Create components
var repository = new JobRepository(options);
var workerPool = new QueueWorkerPool(repository, new Dictionary<string, IJobHandler>(), options, loggerFactory.CreateLogger<QueueWorkerPool>());
var engine = new QueueEngine.Core.QueueEngine(repository, workerPool, options, logger);

// Register handlers
engine.RegisterHandler(new MyJobHandler());

// Start processing
await engine.EnqueueAsync("my-job", new MyPayload("Hello World"));
await engine.StartAsync();

// Wait for jobs to complete
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
        // Your logic here
        Console.WriteLine($"Sending email to {payload.To}");
        
        // Simulate work
        await Task.Delay(500, ct);
    }
}
```

### Step 3: Register and Use

```csharp
engine.RegisterHandler(new EmailJobHandler());
await engine.EnqueueAsync("send-email", new SendEmailPayload("user@example.com", "Hello", "World!"));
```

## Configuration

### QueueEngineOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| ConnectionString | string | "Data Source=queue.db" | Database connection string |
| DatabaseProvider | string | "sqlite" | "sqlite" or "postgres" |
| Queues | Dictionary | default queue | Queue configurations |
| MaxRetries | int | 3 | Maximum retry attempts |
| RetryDelaySeconds | int | 5 | Delay between retries |

### QueueOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Concurrency | int | 1 | Number of concurrent workers |
| RateLimitPerSecond | int | 10 | Maximum jobs per second |

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
└── Program.cs                      # Demo application
```

## Key Interfaces

### IQueueEngine

```csharp
public interface IQueueEngine
{
    void RegisterHandler(IJobHandler handler);
    Task StartAsync();
    Task StopAsync();
    Task<Guid> EnqueueAsync(string jobType, object payload, string queue = "default", DateTime? scheduledAt = null);
    Task<Dictionary<string, (int Pending, int Running, int Done, int Failed)>> GetStatsAsync();
}
```

### IJobHandler

```csharp
public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(QueueJob job, string payload, CancellationToken ct);
}
```

## Database Schema

### SQLite

```sql
CREATE TABLE queue_jobs (
    id TEXT PRIMARY KEY,
    job_type TEXT NOT NULL,
    queue TEXT NOT NULL DEFAULT 'default',
    payload TEXT NOT NULL DEFAULT '{}',
    status INTEGER NOT NULL DEFAULT 0,
    retry_count INTEGER NOT NULL DEFAULT 0,
    error_message TEXT,
    scheduled_at TEXT,
    created_at TEXT NOT NULL,
    started_at TEXT,
    completed_at TEXT
);
CREATE INDEX idx_queue_status ON queue_jobs(queue, status);
```

### PostgreSQL

```sql
CREATE TABLE queue_jobs (
    id UUID PRIMARY KEY,
    job_type VARCHAR(255) NOT NULL,
    queue VARCHAR(255) NOT NULL DEFAULT 'default',
    payload TEXT NOT NULL DEFAULT '{}',
    status INTEGER NOT NULL DEFAULT 0,
    retry_count INTEGER NOT NULL DEFAULT 0,
    error_message TEXT,
    scheduled_at TIMESTAMP,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    started_at TIMESTAMP,
    completed_at TIMESTAMP
);
CREATE INDEX idx_queue_status ON queue_jobs(queue, status);
```

## License

MIT
