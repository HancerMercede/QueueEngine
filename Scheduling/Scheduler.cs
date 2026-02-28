using Cronos;
using Microsoft.Extensions.Logging;
using QueueEngine.Config;
using QueueEngine.Data;

namespace QueueEngine.Scheduling;

public class Scheduler : IScheduler, IDisposable
{
    private readonly IJobRepository _repository;
    private readonly QueueEngineOptions _options;
    private readonly ILogger<Scheduler> _logger;
    private CancellationTokenSource? _cts;
    private Task? _runnerTask;
    private readonly Dictionary<string, JobSchedule> _schedules = new();

    public Scheduler(
        IJobRepository repository,
        QueueEngineOptions options,
        ILogger<Scheduler> logger)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    public async Task ScheduleAsync(string jobType, object payload, string cronExpression, string queue = "default")
    {
        if (string.IsNullOrWhiteSpace(jobType))
            throw new ArgumentException("Job type cannot be empty", nameof(jobType));
        
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("CRON expression cannot be empty", nameof(cronExpression));
        
        ValidateCronExpression(cronExpression);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        
        var schedule = new JobSchedule
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = jobType,
            Payload = payloadJson,
            Queue = queue,
            CronExpression = cronExpression,
            NextRunAt = GetNextRun(cronExpression),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SaveScheduleAsync(schedule);
        
        lock (_lock)
        {
            _schedules[schedule.Id] = schedule;
        }

        _logger.LogInformation("Scheduled job {JobType} with CRON '{Cron}' (ID: {ScheduleId})", 
            jobType, cronExpression, schedule.Id);
    }

    public async Task UnscheduleAsync(string scheduleId)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
            throw new ArgumentException("Schedule ID cannot be empty", nameof(scheduleId));

        await _repository.DeleteScheduleAsync(scheduleId);
        
        lock (_lock)
        {
            _schedules.Remove(scheduleId);
        }

        _logger.LogInformation("Unscheduled job {ScheduleId}", scheduleId);
    }

    public async Task<IEnumerable<JobSchedule>> GetSchedulesAsync(string? queue = null)
    {
        return await _repository.GetSchedulesAsync(queue);
    }

    public void Start()
    {
        if (_runnerTask != null) return;

        _cts = new CancellationTokenSource();
        _runnerTask = RunLoopAsync(_cts.Token);
        
        _logger.LogInformation("Scheduler started");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _runnerTask = null;
        
        _logger.LogInformation("Scheduler stopped");
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var dueSchedules = await _repository.GetDueSchedulesAsync();
                
                foreach (var schedule in dueSchedules)
                {
                    try
                    {
                        var jobPayload = System.Text.Json.JsonSerializer.Deserialize<object>(schedule.Payload)
                            ?? new { };

                        await _repository.EnqueueAsync(new Models.QueueJob
                        {
                            Id = Guid.NewGuid(),
                            JobType = schedule.JobType,
                            Queue = schedule.Queue,
                            Payload = schedule.Payload,
                            Status = Models.JobStatus.Pending,
                            Priority = 0,
                            CreatedAt = DateTime.UtcNow
                        });

                        schedule.LastRunAt = DateTime.UtcNow;
                        schedule.NextRunAt = GetNextRun(schedule.CronExpression);
                        await _repository.UpdateScheduleAsync(schedule);

                        _logger.LogDebug("Enqueued scheduled job {JobType} (Schedule: {ScheduleId})", 
                            schedule.JobType, schedule.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to enqueue scheduled job {ScheduleId}", schedule.Id);
                    }
                }

                await Task.Delay(10_000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler loop error");
                await Task.Delay(5000, ct);
            }
        }
    }

    private static DateTime? GetNextRun(string cronExpression)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression);
            return cron.GetNextOccurrence(DateTime.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateCronExpression(string cronExpression)
    {
        try
        {
            CronExpression.Parse(cronExpression);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid CRON expression: {cronExpression}", nameof(cronExpression), ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private readonly object _lock = new();
}
