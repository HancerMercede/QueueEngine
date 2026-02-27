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
        ["default"] = new QueueOptions { Concurrency = 2, RateLimitPerSecond = 10 },
        ["critical"] = new QueueOptions { Concurrency = 3, RateLimitPerSecond = 50 },
        ["background"] = new QueueOptions { Concurrency = 1, RateLimitPerSecond = 5 }
    }
};

options.Validate();

var repository = new JobRepository(options);
var workerPoolLogger = loggerFactory.CreateLogger<QueueWorkerPool>();
var workerPool = new QueueWorkerPool(repository, new Dictionary<string, IJobHandler>(), options, workerPoolLogger);
var engine = new QueueEngine.Core.QueueEngine(repository, workerPool, options, logger);

engine.RegisterHandler(new EmailJobHandler());
engine.RegisterHandler(new NotificationJobHandler());

await engine.StartAsync();

Console.WriteLine("Jobs enqueued. Processing...");

await engine.EnqueueAsync("send-email", new EmailPayload("user@example.com", "Welcome!", "Thanks for signing up!"));
await engine.EnqueueAsync("send-email", new EmailPayload("admin@example.com", "New user", "A new user has registered!"), "critical");
await engine.EnqueueAsync("notify-user", new NotificationPayload("usr_123", "Your order was shipped!"), "background", DateTime.UtcNow.AddSeconds(10));

await Task.Delay(3000);

var stats = await engine.GetStatsAsync();
foreach (var kvp in stats)
{
    int pending = kvp.Value.Pending;
    int running = kvp.Value.Running;
    int done = kvp.Value.Done;
    int failed = kvp.Value.Failed;
    int cancelled = kvp.Value.Cancelled;
    Console.WriteLine($"Queue '{kvp.Key}':    pending={pending} running={running} done={done} failed={failed} cancelled={cancelled}");
}

await engine.StopAsync();
