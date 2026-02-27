# QueueEngine Developer Guide

A comprehensive guide for implementing QueueEngine in your .NET applications.

## Table of Contents

1. [Quick Start](#quick-start)
2. [Installation](#installation)
3. [Basic Usage](#basic-usage)
4. [ASP.NET Core Integration](#aspnet-core-integration)
5. [Worker Service Integration](#worker-service-integration)
6. [Job Handlers](#job-handlers)
7. [Configuration](#configuration)
8. [Advanced Features](#advanced-features)
9. [Best Practices](#best-practices)
10. [Troubleshooting](#troubleshooting)

---

## Quick Start

### 1. Install Package

```bash
dotnet add package QueueEngine
```

### 2. Create a Job Handler

```csharp
using QueueEngine.Models;
using QueueEngine.Workers;

public record SendEmailPayload(string To, string Subject, string Body);

public class SendEmailHandler : JobHandler<SendEmailPayload>
{
    public override string JobType => "send-email";

    protected override async Task HandleAsync(QueueJob job, SendEmailPayload payload, CancellationToken ct)
    {
        // Your email sending logic here
        Console.WriteLine($"Sending email to {payload.To}");
        
        // Simulate work
        await Task.Delay(500, ct);
    }
}
```

### 3. Set Up Queue Engine

```csharp
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;
using QueueEngine.Workers;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var options = new QueueEngineOptions
{
    ConnectionString = "Data Source=queue.db",
    DatabaseProvider = "sqlite",
    Queues = new Dictionary<string, QueueOptions>
    {
        ["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 10 }
    }
};

var repository = new JobRepository(options);
var workerPool = new QueueWorkerPool(repository, new Dictionary<string, IJobHandler>(), options, 
    loggerFactory.CreateLogger<QueueWorkerPool>());
var engine = new QueueEngine(repository, workerPool, options, 
    loggerFactory.CreateLogger<QueueEngine>());

// Register handlers
engine.RegisterHandler(new SendEmailHandler());

// Start processing
await engine.EnqueueAsync("send-email", new SendEmailPayload("user@test.com", "Welcome!", "Hello!"));
await engine.StartAsync();

Console.WriteLine("Queue engine started...");
```

---

## Installation

### Required Packages

```bash
dotnet add package Dapper
dotnet add package Microsoft.Data.Sqlite        # For SQLite
dotnet add package Npgsql                        # For PostgreSQL
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Hosting.Abstractions
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.Console
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks  # Optional
```

---

## Basic Usage

### Enqueue Jobs

```csharp
// Single job
var jobId = await engine.EnqueueAsync("send-email", 
    new SendEmailPayload("user@test.com", "Subject", "Body"));

// With custom queue
await engine.EnqueueAsync("process-image", 
    new ImagePayload("photo.jpg"), 
    "image-processing");

// Scheduled job (execute in 1 hour)
await engine.EnqueueAsync("send-reminder", 
    new ReminderPayload(userId), 
    scheduledAt: DateTime.UtcNow.AddHours(1));

// Bulk enqueue
await engine.BulkEnqueueAsync(new[]
{
    ("send-email", new EmailPayload("a@test.com", "Hi", "A"), "default"),
    ("send-email", new EmailPayload("b@test.com", "Hi", "B"), "default"),
    ("notify-user", new NotificationPayload("user1", "Welcome!"), "notifications")
});
```

### Monitor Jobs

```csharp
// Get job status
var job = await engine.GetJobAsync(jobId);
Console.WriteLine($"Job status: {job.Status}");

// Get queue statistics
var stats = await engine.GetStatsAsync();
foreach (var (queue, (pending, running, done, failed, cancelled)) in stats)
{
    Console.WriteLine($"{queue}: pending={pending}, running={running}, done={done}, failed={failed}");
}

// Get dead letter jobs
var deadJobs = await engine.GetDeadLetterJobsAsync("default");
```

### Cancel Jobs

```csharp
// Cancel a pending or running job
var cancelled = await engine.CancelJobAsync(jobId);
if (cancelled)
    Console.WriteLine("Job cancelled successfully");
```

---

## ASP.NET Core Integration

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure QueueEngine
builder.Services.AddQueueEngine(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("Queue") 
        ?? throw new InvalidOperationException("ConnectionString required");
    opts.DatabaseProvider = "postgres"; // Use PostgreSQL in production
    opts.Queues = new Dictionary<string, QueueOptions>
    {
        ["default"] = new QueueOptions 
        { 
            Concurrency = 4, 
            RateLimitPerSecond = 20,
            MaxRetries = 3,
            RetryDelaySeconds = 5,
            EnableDeadLetterQueue = true
        },
        ["emails"] = new QueueOptions
        {
            Concurrency = 2,
            RateLimitPerSecond = 10
        }
    };
});

// Register handlers
builder.Services.AddSingleton<SendEmailHandler>();
builder.Services.AddSingleton<ProcessImageHandler>();

// Add hosted service
builder.Services.AddQueueEngineHostedService();

// Add health checks (optional)
builder.Services.AddHealthChecks()
    .AddQueueEngineHealthCheck();

var app = builder.Build();

// Map endpoints
app.MapGet("/api/jobs/enqueue", async (IQueueEngine engine) =>
{
    var jobId = await engine.EnqueueAsync("send-email", 
        new EmailPayload("user@test.com", "Test", "Hello"));
    return Results.Ok(new { jobId });
});

app.MapGet("/api/jobs/{id:guid}", async (Guid id, IQueueEngine engine) =>
{
    var job = await engine.GetJobAsync(id);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/api/stats", async (IQueueEngine engine) =>
{
    var stats = await engine.GetStatsAsync();
    return Results.Ok(stats);
});

app.Run();
```

---

## Worker Service Integration

### Program.cs

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Queue configuration
builder.Services.Configure<QueueEngineOptions>(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("Queue")!;
    opts.DatabaseProvider = "postgres";
    opts.Queues["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 10 };
});

// Register handlers
builder.Services.AddSingleton<IJobHandler, SendEmailHandler>();
builder.Services.AddSingleton<IJobHandler, ProcessImageHandler>();

// Add queue engine
builder.Services.AddSingleton<IQueueEngine, QueueEngine>();
builder.Services.AddHostedService<QueueEngineWorker>();

var host = builder.Build();
host.Run();
```

### QueueEngineWorker.cs

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueueEngine.Core;

public class QueueEngineWorker(
    IQueueEngine engine,
    ILogger<QueueEngineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting QueueEngine...");

        await engine.StartAsync();
        
        logger.LogInformation("QueueEngine started successfully");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await engine.StopAsync();
        logger.LogInformation("QueueEngine stopped");
    }
}
```

---

## Job Handlers

### Simple Handler

```csharp
public class ProcessImageHandler : JobHandler<ProcessImagePayload>
{
    public override string JobType => "process-image";

    protected override async Task HandleAsync(
        QueueJob job, 
        ProcessImagePayload payload, 
        CancellationToken ct)
    {
        // Process image
        var image = await LoadImageAsync(payload.ImagePath, ct);
        var processed = ResizeImage(image, payload.Width, payload.Height);
        await SaveImageAsync(processed, payload.OutputPath, ct);
    }
}
```

### Handler with External Dependencies

```csharp
public class SendEmailHandler : JobHandler<SendEmailPayload>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendEmailHandler> _logger;

    public SendEmailHandler(IEmailService emailService, ILogger<SendEmailHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public override string JobType => "send-email";

    protected override async Task HandleAsync(
        QueueJob job, 
        SendEmailPayload payload, 
        CancellationToken ct)
    {
        _logger.LogInformation("Sending email to {To}", payload.To);
        
        await _emailService.SendAsync(payload.To, payload.Subject, payload.Body, ct);
        
        _logger.LogInformation("Email sent to {To}", payload.To);
    }
}
```

### Register Handler with DI

```csharp
builder.Services.AddJobHandler<SendEmailHandler>();

// Or manually
builder.Services.AddSingleton<IJobHandler>(sp => 
    new SendEmailHandler(
        sp.GetRequiredService<IEmailService>(),
        sp.GetRequiredService<ILogger<SendEmailHandler>>()));
```

---

## Configuration

### Queue Options

```csharp
var options = new QueueEngineOptions
{
    ConnectionString = "Data Source=queue.db",
    DatabaseProvider = "sqlite",  // or "postgres"
    Queues = new Dictionary<string, QueueOptions>
    {
        ["default"] = new QueueOptions
        {
            Concurrency = 4,              // Number of parallel workers
            RateLimitPerSecond = 20,      // Max jobs per second
            MaxRetries = 3,               // Retry attempts before DLQ
            RetryDelaySeconds = 5,        // Base delay for exponential backoff
            EnableDeadLetterQueue = true, // Move to DLQ after max retries
            DeadLetterQueueName = "dead-letter"
        },
        ["critical"] = new QueueOptions   // High priority queue
        {
            Concurrency = 8,
            RateLimitPerSecond = 100,
            MaxRetries = 5
        },
        ["background"] = new QueueOptions // Low priority
        {
            Concurrency = 1,
            RateLimitPerSecond = 5
        }
    }
};
```

### Environment-Specific Configuration

```csharp
// appsettings.json
{
  "QueueEngine": {
    "ConnectionString": "${DB_CONNECTION_STRING}",
    "DatabaseProvider": "postgres",
    "Queues": {
      "default": {
        "Concurrency": 4,
        "RateLimitPerSecond": 20
      }
    }
  }
}

// Program.cs
builder.Services.Configure<QueueEngineOptions>(
    builder.Configuration.GetSection("QueueEngine"));
```

---

## Advanced Features

### Retry with Exponential Backoff

Jobs automatically retry on failure:

```
Retry 1: wait 5s  → fail → wait 10s  → fail → wait 20s → fail → DLQ
```

```csharp
// Configure per queue
["default"] = new QueueOptions
{
    MaxRetries = 3,
    RetryDelaySeconds = 5  // 5, 10, 20 seconds
}
```

### Dead Letter Queue

Permanently failed jobs are moved to DLQ:

```csharp
// Get failed jobs
var deadJobs = await engine.GetDeadLetterJobsAsync("default");

// Process dead letter manually
foreach (var job in deadJobs)
{
    var payload = JsonSerializer.Deserialize<SendEmailPayload>(job.Payload);
    await emailService.SendAsync(payload.To, payload.Subject, payload.Body);
    Console.WriteLine($"Manually processed job {job.Id}");
}

// Or requeue for retry
await engine.EnqueueAsync(job.JobType, payload, job.Queue);
```

### Health Checks

```csharp
// In Program.cs
builder.Services.AddHealthChecks()
    .AddQueueEngineHealthCheck();

// GET /health returns:
// {
//   "status": "Healthy",
//   "results": {
//     "queueengine": {
//       "status": "Healthy",
//       "data": {
//         "queues": 3,
//         "totalPending": 10,
//         "totalRunning": 2,
//         "totalFailed": 0
//       }
//     }
//   }
// }
```

---

## Best Practices

### 1. Use PostgreSQL in Production

```csharp
opts.DatabaseProvider = "postgres";  // Not sqlite in production!
opts.ConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
```

### 2. Register Handlers Early

```csharp
// In DI container - handlers loaded before engine starts
builder.Services.AddSingleton<IJobHandler, EmailHandler>();
builder.Services.AddSingleton<IJobHandler, ImageHandler>();
```

### 3. Keep Payloads Small

```csharp
// Good - reference IDs
await engine.EnqueueAsync("process-order", new { OrderId = 123 });

// Avoid - large data
await engine.EnqueueAsync("process-report", new { FullReport = entireFile }); // Bad!
```

### 4. Handle CancellationToken

```csharp
protected override async Task HandleAsync(QueueJob job, TPayload payload, CancellationToken ct)
{
    // Check cancellation periodically
    for (int i = 0; i < 100; i++)
    {
        ct.ThrowIfCancellationRequested();
        await ProcessItemAsync(i, ct);
    }
}
```

### 5. Log Appropriately

```csharp
protected override async Task HandleAsync(QueueJob job, TPayload payload, CancellationToken ct)
{
    _logger.LogInformation("Processing job {JobId} - {JobType}", job.Id, job.JobType);
    
    try
    {
        await DoWorkAsync(payload, ct);
        _logger.LogInformation("Job {JobId} completed", job.Id);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Job {JobId} failed", job.Id);
        throw; // Let QueueEngine handle retry
    }
}
```

### 6. Separate Queues by Priority

```csharp
Queues = new Dictionary<string, QueueOptions>
{
    ["critical"] = new QueueOptions { Concurrency = 8, RateLimitPerSecond = 100 },  // User-facing
    ["default"] = new QueueOptions { Concurrency = 4, RateLimitPerSecond = 20 },    // Normal
    ["bulk"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 5 }       // Background
}

// Usage
await engine.EnqueueAsync("send-welcome-email", payload, "critical");
await engine.EnqueueAsync("generate-report", payload, "bulk");
```

---

## Troubleshooting

### Jobs Not Processing

1. Check queue configuration:
```csharp
var stats = await engine.GetStatsAsync();
// If pending > 0 but not processing, check workers
```

2. Verify handlers registered:
```csharp
engine.RegisterHandler(new MyHandler()); // Must be registered before StartAsync!
```

3. Check database connection:
```csharp
// Test connection
opts.DatabaseProvider = "postgres";
opts.ConnectionString = "Host=localhost;Database=queue;Username=user;Password=pass";
```

### SQLite Database Locked

SQLite doesn't support concurrent writes. Use PostgreSQL in production:

```csharp
opts.DatabaseProvider = "postgres";
```

### Jobs Stuck in Running Status

This happens when worker crashes. Manually reset:

```csharp
// Not implemented yet - planned feature
```

### High Memory Usage

Reduce concurrency or rate limiting:

```csharp
["default"] = new QueueOptions
{
    Concurrency = 2,  // Reduce
    RateLimitPerSecond = 10  // Reduce
}
```

---

## Migration from Other Queues

### From Hangfire

```csharp
// Hangfire
BackgroundJob.Enqueue(() => service.SendEmail(id));

// QueueEngine
await engine.EnqueueAsync("send-email", new EmailPayload(id));
```

### From Quasar

```csharp
// Quasar
await Queue.Enqueue("send-email", new EmailPayload(id));

// QueueEngine  
await engine.EnqueueAsync("send-email", new EmailPayload(id));
```

---

## License

MIT
