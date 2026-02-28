using QueueEngine;
using QueueEngine.Config;
using QueueEngine.Core;
using QueueEngine.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQueueEngine(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("QueueEngine") ?? "Data Source=queue.db";
    options.DatabaseProvider = builder.Configuration["QueueEngine:DatabaseProvider"] ?? "sqlite";
    options.Queues["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 20 };
    options.Queues["critical"] = new QueueOptions { Concurrency = 4, RateLimitPerSecond = 50 };
    options.Queues["background"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 5 };
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/stats", async (IQueueEngine engine) =>
{
    var stats = await engine.GetStatsAsync();
    var workers = await engine.GetActiveWorkersAsync();
    
    var totalPending = stats.Values.Sum(s => s.Pending);
    var totalRunning = stats.Values.Sum(s => s.Running);
    var totalDone = stats.Values.Sum(s => s.Done);
    var totalFailed = stats.Values.Sum(s => s.Failed);
    
    return Results.Ok(new
    {
        queues = stats,
        workers = workers.Select(w => new { w.WorkerId, w.Queues, w.LastHeartbeat, w.IsActive }),
        totals = new { pending = totalPending, running = totalRunning, done = totalDone, failed = totalFailed }
    });
});

app.MapGet("/api/queues", async (IQueueEngine engine) =>
{
    var stats = await engine.GetStatsAsync();
    var queues = new List<object>();
    
    foreach (var (name, stat) in stats)
    {
        var isPaused = await engine.IsQueuePausedAsync(name);
        queues.Add(new
        {
            name,
            pending = stat.Pending,
            running = stat.Running,
            done = stat.Done,
            failed = stat.Failed,
            cancelled = stat.Cancelled,
            isPaused
        });
    }
    
    return Results.Ok(queues);
});

app.MapGet("/api/jobs/{queue}", async (string queue, IJobRepository repository) =>
{
    var stats = await repository.GetStatsAsync(queue);
    return Results.Ok(new
    {
        queue,
        pending = stats.Pending,
        running = stats.Running,
        done = stats.Done,
        failed = stats.Failed,
        cancelled = stats.Cancelled
    });
});

app.MapGet("/api/workers", async (IQueueEngine engine) =>
{
    var workers = await engine.GetActiveWorkersAsync();
    return Results.Ok(workers.Select(w => new
    {
        w.WorkerId,
        w.Queues,
        w.LastHeartbeat,
        w.IsActive
    }));
});

app.MapPost("/api/queues/{queue}/pause", async (string queue, IQueueEngine engine) =>
{
    await engine.PauseQueueAsync(queue);
    return Results.Ok(new { message = $"Queue '{queue}' paused" });
});

app.MapPost("/api/queues/{queue}/resume", async (string queue, IQueueEngine engine) =>
{
    await engine.ResumeQueueAsync(queue);
    return Results.Ok(new { message = $"Queue '{queue}' resumed" });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.MapGet("/", () => Results.Redirect("/index.html"));

app.MapGet("/index.html", async (HttpContext context) =>
{
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync("wwwroot/index.html");
});

app.UseStaticFiles();
app.UseDefaultFiles();

app.Run();
