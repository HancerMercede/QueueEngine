using Dapper;
using Microsoft.Data.Sqlite;
using Npgsql;
using QueueEngine.Config;
using QueueEngine.Models;
using QueueEngine.Scheduling;

namespace QueueEngine.Data;

public class JobRepository : IJobRepository
{
    private readonly string _connectionString;
    private readonly string _provider;
    private readonly Dictionary<string, bool> _pausedQueues = new();
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 100;

    public JobRepository(QueueEngineOptions options)
    {
        _connectionString = options.ConnectionString;
        _provider = options.DatabaseProvider;
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        int attempts = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
            {
                attempts++;
                if (attempts >= MaxRetries)
                    throw;
                await Task.Delay(RetryDelayMs * attempts);
            }
        }
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action)
    {
        int attempts = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
            {
                attempts++;
                if (attempts >= MaxRetries)
                    throw;
                await Task.Delay(RetryDelayMs * attempts);
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_provider == "sqlite")
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_jobs (
                    id TEXT PRIMARY KEY,
                    job_type TEXT NOT NULL,
                    queue TEXT NOT NULL DEFAULT 'default',
                    payload TEXT NOT NULL DEFAULT '{}',
                    status INTEGER NOT NULL DEFAULT 0,
                    priority INTEGER NOT NULL DEFAULT 0,
                    progress INTEGER NOT NULL DEFAULT 0,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT,
                    scheduled_at TEXT,
                    created_at TEXT NOT NULL,
                    started_at TEXT,
                    completed_at TEXT,
                    cancellation_requested INTEGER NOT NULL DEFAULT 0,
                    worker_id TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_queue_status ON queue_jobs(queue, status);
                CREATE INDEX IF NOT EXISTS idx_queue_priority ON queue_jobs(queue, priority DESC);
            ");

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_workers (
                    worker_id TEXT PRIMARY KEY,
                    queues TEXT NOT NULL,
                    last_heartbeat TEXT NOT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1
                );
            ");

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_schedules (
                    id TEXT PRIMARY KEY,
                    job_type TEXT NOT NULL,
                    payload TEXT NOT NULL DEFAULT '{}',
                    queue TEXT NOT NULL DEFAULT 'default',
                    cron_expression TEXT NOT NULL,
                    next_run_at TEXT,
                    last_run_at TEXT,
                    is_enabled INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_schedules_next_run ON queue_schedules(next_run_at, is_enabled);
            ");
        }
        else if (_provider == "postgres")
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_jobs (
                    id UUID PRIMARY KEY,
                    job_type VARCHAR(255) NOT NULL,
                    queue VARCHAR(255) NOT NULL DEFAULT 'default',
                    payload TEXT NOT NULL DEFAULT '{}',
                    status INTEGER NOT NULL DEFAULT 0,
                    priority INTEGER NOT NULL DEFAULT 0,
                    progress INTEGER NOT NULL DEFAULT 0,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT,
                    scheduled_at TIMESTAMP,
                    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                    started_at TIMESTAMP,
                    completed_at TIMESTAMP,
                    cancellation_requested BOOLEAN NOT NULL DEFAULT FALSE,
                    worker_id VARCHAR(255)
                );
                CREATE INDEX IF NOT EXISTS idx_queue_status ON queue_jobs(queue, status);
                CREATE INDEX IF NOT EXISTS idx_queue_priority ON queue_jobs(queue, priority DESC);
            ");

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_workers (
                    worker_id VARCHAR(255) PRIMARY KEY,
                    queues VARCHAR(1000) NOT NULL,
                    last_heartbeat TIMESTAMP NOT NULL DEFAULT NOW(),
                    is_active BOOLEAN NOT NULL DEFAULT TRUE
                );
            ");

            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS queue_schedules (
                    id VARCHAR(255) PRIMARY KEY,
                    job_type VARCHAR(255) NOT NULL,
                    payload TEXT NOT NULL DEFAULT '{}',
                    queue VARCHAR(255) NOT NULL DEFAULT 'default',
                    cron_expression VARCHAR(100) NOT NULL,
                    next_run_at TIMESTAMP,
                    last_run_at TIMESTAMP,
                    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at TIMESTAMP NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_schedules_next_run ON queue_schedules(next_run_at, is_enabled);
            ");
        }
    }

    public async Task<Guid> EnqueueAsync(QueueJob job)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"
                    INSERT INTO queue_jobs (id, job_type, queue, payload, status, priority, progress, retry_count, scheduled_at, created_at)
                    VALUES (@Id, @JobType, @Queue, @Payload, @Status, @Priority, @Progress, @RetryCount, @ScheduledAt, @CreatedAt)";

                await conn.ExecuteAsync(sql, new
                {
                    Id = job.Id.ToString(),
                    job.JobType,
                    job.Queue,
                    job.Payload,
                    Status = (int)job.Status,
                    job.Priority,
                    job.Progress,
                    job.RetryCount,
                    ScheduledAt = job.ScheduledAt?.ToString("o"),
                    CreatedAt = job.CreatedAt.ToString("o")
                });

                return job.Id;
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                INSERT INTO queue_jobs (id, job_type, queue, payload, status, priority, progress, retry_count, scheduled_at, created_at)
                VALUES (@Id, @JobType, @Queue, @Payload, @Status, @Priority, @Progress, @RetryCount, @ScheduledAt, @CreatedAt)";

            await conn.ExecuteAsync(sql, new
            {
                Id = job.Id,
                job.JobType,
                job.Queue,
                job.Payload,
                Status = (int)job.Status,
                job.Priority,
                job.Progress,
                job.RetryCount,
                ScheduledAt = job.ScheduledAt,
                CreatedAt = job.CreatedAt
            });

            return job.Id;
        }
    }

    public async Task<QueueJob?> DequeueAsync(string queue, string? workerId = null)
    {
        var now = DateTime.UtcNow.ToString("o");
        
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var job = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    UPDATE queue_jobs 
                    SET status = @RunningStatus, started_at = @Now, worker_id = @WorkerId
                    WHERE id = (
                        SELECT id FROM queue_jobs 
                        WHERE queue = @Queue 
                        AND status = @PendingStatus 
                        AND (scheduled_at IS NULL OR scheduled_at <= @Now)
                        ORDER BY priority DESC, created_at ASC 
                        LIMIT 1
                    )
                    RETURNING *", new { 
                    Queue = queue, 
                    PendingStatus = (int)JobStatus.Pending,
                    RunningStatus = (int)JobStatus.Running,
                    Now = now,
                    WorkerId = workerId ?? (object)DBNull.Value
                });

                if (job == null) return null;
                return MapToJobSqlite(job);
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var job = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                UPDATE queue_jobs 
                SET status = @RunningStatus, started_at = @Now, worker_id = @WorkerId
                WHERE id = (
                    SELECT id FROM queue_jobs 
                    WHERE queue = @Queue 
                    AND status = @PendingStatus 
                    AND (scheduled_at IS NULL OR scheduled_at <= @Now)
                    ORDER BY priority DESC, created_at ASC 
                    LIMIT 1
                )
                RETURNING id, job_type, queue, payload, status, priority, progress, retry_count, error_message, scheduled_at, created_at, started_at, completed_at", new { 
                Queue = queue, 
                PendingStatus = (int)JobStatus.Pending,
                RunningStatus = (int)JobStatus.Running,
                Now = now,
                WorkerId = workerId ?? (object)DBNull.Value
            });

            if (job == null) return null;
            return MapToJobPostgres(job);
        }
    }

    public async Task CompleteAsync(Guid jobId, JobStatus status, string? errorMessage = null)
    {
        var now = DateTime.UtcNow.ToString("o");
        
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                
                await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET status = @Status, completed_at = @Now, error_message = @ErrorMessage
                    WHERE id = @Id",
                    new { Id = jobId.ToString(), Status = (int)status, Now = now, ErrorMessage = errorMessage });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET status = @Status, completed_at = @Now, error_message = @ErrorMessage
                WHERE id = @Id",
                new { Id = jobId, Status = (int)status, Now = now, ErrorMessage = errorMessage });
        }
    }

    public async Task<(int Pending, int Running, int Done, int Failed, int Cancelled)> GetStatsAsync(string queue)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var stats = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END) as pending,
                        SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END) as running,
                        SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END) as done,
                        SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END) as failed,
                        SUM(CASE WHEN status = 4 THEN 1 ELSE 0 END) as cancelled
                    FROM queue_jobs 
                    WHERE queue = @Queue", new { Queue = queue });

                return (
                    (int)(stats?.pending ?? 0),
                    (int)(stats?.running ?? 0),
                    (int)(stats?.done ?? 0),
                    (int)(stats?.failed ?? 0),
                    (int)(stats?.cancelled ?? 0)
                );
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var stats = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END) as pending,
                    SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END) as running,
                    SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END) as done,
                    SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END) as failed,
                    SUM(CASE WHEN status = 4 THEN 1 ELSE 0 END) as cancelled
                FROM queue_jobs 
                WHERE queue = @Queue", new { Queue = queue });

            return (
                (int)(stats?.pending ?? 0),
                (int)(stats?.running ?? 0),
                (int)(stats?.done ?? 0),
                (int)(stats?.failed ?? 0),
                (int)(stats?.cancelled ?? 0)
            );
        }
    }

    public async Task<bool> RequestCancellationAsync(Guid jobId)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var rowsAffected = await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET cancellation_requested = 1
                    WHERE id = @Id AND status IN (0, 1)",
                    new { Id = jobId.ToString() });

                return rowsAffected > 0;
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var rowsAffected = await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET cancellation_requested = true
                WHERE id = @Id AND status IN (0, 1)",
                new { Id = jobId });

            return rowsAffected > 0;
        }
    }

    public async Task<QueueJob?> GetJobAsync(Guid jobId)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var job = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT * FROM queue_jobs WHERE id = @Id",
                    new { Id = jobId.ToString() });

                return job == null ? null : MapToJobSqlite(job);
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var job = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM queue_jobs WHERE id = @Id",
                new { Id = jobId });

            return job == null ? null : MapToJobPostgres(job);
        }
    }

    private QueueJob MapToJobSqlite(dynamic row)
    {
        return new QueueJob
        {
            Id = Guid.Parse((string)row.id),
            JobType = (string)row.job_type,
            Queue = (string)row.queue,
            Payload = (string)row.payload,
            Status = (JobStatus)(int)row.status,
            Priority = (int)(row.priority ?? 0),
            Progress = (int)(row.progress ?? 0),
            RetryCount = (int)row.retry_count,
            ErrorMessage = row.error_message,
            ScheduledAt = row.scheduled_at != null ? DateTime.Parse((string)row.scheduled_at) : null,
            CreatedAt = DateTime.Parse((string)row.created_at),
            StartedAt = row.started_at != null ? DateTime.Parse((string)row.started_at) : null,
            CompletedAt = row.completed_at != null ? DateTime.Parse((string)row.completed_at) : null,
            CancellationRequested = row.cancellation_requested == 1
        };
    }

    private QueueJob MapToJobPostgres(dynamic row)
    {
        return new QueueJob
        {
            Id = (Guid)row.id,
            JobType = (string)row.job_type,
            Queue = (string)row.queue,
            Payload = (string)row.payload,
            Status = (JobStatus)(int)row.status,
            Priority = (int)(row.priority ?? 0),
            Progress = (int)(row.progress ?? 0),
            RetryCount = (int)row.retry_count,
            ErrorMessage = row.error_message,
            ScheduledAt = row.scheduled_at,
            CreatedAt = (DateTime)row.created_at,
            StartedAt = row.started_at,
            CompletedAt = row.completed_at,
            CancellationRequested = row.cancellation_requested == true
        };
    }

    public async Task RequeueAsync(Guid jobId, int retryCount, string? errorMessage = null)
    {
        var now = DateTime.UtcNow.ToString("o");
        
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET status = @PendingStatus, 
                        retry_count = @RetryCount, 
                        error_message = @ErrorMessage,
                        started_at = NULL,
                        completed_at = NULL
                    WHERE id = @Id",
                    new { Id = jobId.ToString(), PendingStatus = (int)JobStatus.Pending, RetryCount = retryCount, ErrorMessage = errorMessage });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET status = @PendingStatus, 
                    retry_count = @RetryCount, 
                    error_message = @ErrorMessage,
                    started_at = NULL,
                    completed_at = NULL
                WHERE id = @Id",
                new { Id = jobId, PendingStatus = (int)JobStatus.Pending, RetryCount = retryCount, ErrorMessage = errorMessage });
        }
    }

    public async Task BulkEnqueueAsync(IEnumerable<QueueJob> jobs)
    {
        var jobList = jobs.ToList();
        if (!jobList.Any()) return;

        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                using var transaction = conn.BeginTransaction();
                
                foreach (var job in jobList)
                {
                    var sql = @"
                        INSERT INTO queue_jobs (id, job_type, queue, payload, status, priority, progress, retry_count, scheduled_at, created_at)
                        VALUES (@Id, @JobType, @Queue, @Payload, @Status, @Priority, @Progress, @RetryCount, @ScheduledAt, @CreatedAt)";

                    await conn.ExecuteAsync(sql, new
                    {
                        Id = job.Id.ToString(),
                        job.JobType,
                        job.Queue,
                        job.Payload,
                        Status = (int)job.Status,
                        job.Priority,
                        job.Progress,
                        job.RetryCount,
                        ScheduledAt = job.ScheduledAt?.ToString("o"),
                        CreatedAt = job.CreatedAt.ToString("o")
                    }, transaction);
                }

                transaction.Commit();
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            using var transaction = conn.BeginTransaction();
            
            foreach (var job in jobList)
            {
                var sql = @"
                    INSERT INTO queue_jobs (id, job_type, queue, payload, status, priority, progress, retry_count, scheduled_at, created_at)
                    VALUES (@Id, @JobType, @Queue, @Payload, @Status, @Priority, @Progress, @RetryCount, @ScheduledAt, @CreatedAt)";

                await conn.ExecuteAsync(sql, new
                {
                    Id = job.Id,
                    job.JobType,
                    job.Queue,
                    job.Payload,
                    Status = (int)job.Status,
                    job.Priority,
                    job.Progress,
                    job.RetryCount,
                    ScheduledAt = job.ScheduledAt,
                    CreatedAt = job.CreatedAt
                }, transaction);
            }

            transaction.Commit();
        }
    }

    public async Task MoveToDeadLetterAsync(Guid jobId, string? errorMessage = null)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET queue = @NewQueue, error_message = @ErrorMessage
                    WHERE id = @Id",
                    new { Id = jobId.ToString(), NewQueue = "dead-letter", ErrorMessage = errorMessage });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET queue = @NewQueue, error_message = @ErrorMessage
                WHERE id = @Id",
                new { Id = jobId, NewQueue = "dead-letter", ErrorMessage = errorMessage });
        }
    }

    public async Task<IEnumerable<QueueJob>> GetDeadLetterJobsAsync(string queue)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var jobs = await conn.QueryAsync<dynamic>(@"
                    SELECT * FROM queue_jobs 
                    WHERE queue = @Queue AND status = @FailedStatus
                    ORDER BY completed_at DESC
                    LIMIT 100", new { Queue = queue, FailedStatus = (int)JobStatus.Failed });

                return jobs.Select(MapToJobSqlite).ToList();
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var jobs = await conn.QueryAsync<dynamic>(@"
                SELECT * FROM queue_jobs 
                WHERE queue = @Queue AND status = @FailedStatus
                ORDER BY completed_at DESC
                LIMIT 100", new { Queue = queue, FailedStatus = (int)JobStatus.Failed });

            return jobs.Select(MapToJobPostgres).ToList();
        }
    }

    public async Task RecoverStaleJobsAsync(string workerId, int staleTimeoutSeconds)
    {
        var staleTimeout = DateTime.UtcNow.AddSeconds(-staleTimeoutSeconds).ToString("o");

        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET status = @PendingStatus, started_at = NULL, worker_id = NULL
                    WHERE worker_id = @WorkerId 
                    AND status = @RunningStatus 
                    AND started_at < @StaleTimeout",
                    new { WorkerId = workerId, RunningStatus = (int)JobStatus.Running, PendingStatus = (int)JobStatus.Pending, StaleTimeout = staleTimeout });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET status = @PendingStatus, started_at = NULL, worker_id = NULL
                WHERE worker_id = @WorkerId 
                AND status = @RunningStatus 
                AND started_at < @StaleTimeout",
                new { WorkerId = workerId, RunningStatus = (int)JobStatus.Running, PendingStatus = (int)JobStatus.Pending, StaleTimeout = staleTimeout });
        }
    }

    public async Task RegisterWorkerAsync(string workerId, string[] queues, int heartbeatIntervalSeconds)
    {
        var now = DateTime.UtcNow.ToString("o");
        var queuesJson = string.Join(",", queues);

        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    INSERT OR REPLACE INTO queue_workers (worker_id, queues, last_heartbeat, is_active)
                    VALUES (@WorkerId, @Queues, @LastHeartbeat, 1)",
                    new { WorkerId = workerId, Queues = queuesJson, LastHeartbeat = now });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                INSERT INTO queue_workers (worker_id, queues, last_heartbeat, is_active)
                VALUES (@WorkerId, @Queues, @LastHeartbeat, true)
                ON CONFLICT (worker_id) DO UPDATE SET
                    queues = @Queues,
                    last_heartbeat = @LastHeartbeat,
                    is_active = true",
                new { WorkerId = workerId, Queues = queuesJson, LastHeartbeat = now });
        }
    }

    public async Task HeartbeatAsync(string workerId)
    {
        var now = DateTime.UtcNow.ToString("o");

        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_workers 
                    SET last_heartbeat = @LastHeartbeat
                    WHERE worker_id = @WorkerId",
                    new { WorkerId = workerId, LastHeartbeat = now });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_workers 
                SET last_heartbeat = @LastHeartbeat
                WHERE worker_id = @WorkerId",
                new { WorkerId = workerId, LastHeartbeat = now });
        }
    }

    public async Task UnregisterWorkerAsync(string workerId)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_workers 
                    SET is_active = 0
                    WHERE worker_id = @WorkerId",
                    new { WorkerId = workerId });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_workers 
                SET is_active = false
                WHERE worker_id = @WorkerId",
                new { WorkerId = workerId });
        }
    }

    public async Task<IEnumerable<WorkerInfo>> GetActiveWorkersAsync()
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var workers = await conn.QueryAsync<dynamic>(@"
                    SELECT worker_id, queues, last_heartbeat, is_active 
                    FROM queue_workers 
                    WHERE is_active = 1");

                return workers.Select(w => new WorkerInfo
                {
                    WorkerId = (string)w.worker_id,
                    Queues = ((string)w.queues).Split(','),
                    LastHeartbeat = DateTime.Parse((string)w.last_heartbeat),
                    IsActive = w.is_active == 1
                }).ToList();
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var workers = await conn.QueryAsync<dynamic>(@"
                SELECT worker_id, queues, last_heartbeat, is_active 
                FROM queue_workers 
                WHERE is_active = true");

            return workers.Select(w => new WorkerInfo
            {
                WorkerId = (string)w.worker_id,
                Queues = ((string)w.queues).Split(','),
                LastHeartbeat = (DateTime)w.last_heartbeat,
                IsActive = (bool)w.is_active
            }).ToList();
        }
    }

    public async Task UpdateProgressAsync(Guid jobId, int progress)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_jobs 
                    SET progress = @Progress
                    WHERE id = @Id",
                    new { Id = jobId.ToString(), Progress = Math.Clamp(progress, 0, 100) });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_jobs 
                SET progress = @Progress
                WHERE id = @Id",
                new { Id = jobId, Progress = Math.Clamp(progress, 0, 100) });
        }
    }

    public Task<bool> IsQueuePausedAsync(string queue)
    {
        return Task.FromResult(_pausedQueues.TryGetValue(queue, out var isPaused) && isPaused);
    }

    public Task SetQueuePausedAsync(string queue, bool paused)
    {
        _pausedQueues[queue] = paused;
        return Task.CompletedTask;
    }

    public async Task SaveScheduleAsync(JobSchedule schedule)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"
                    INSERT INTO queue_schedules (id, job_type, payload, queue, cron_expression, next_run_at, is_enabled, created_at)
                    VALUES (@Id, @JobType, @Payload, @Queue, @CronExpression, @NextRunAt, @IsEnabled, @CreatedAt)";

                await conn.ExecuteAsync(sql, new
                {
                    schedule.Id,
                    schedule.JobType,
                    schedule.Payload,
                    schedule.Queue,
                    schedule.CronExpression,
                    NextRunAt = schedule.NextRunAt?.ToString("o"),
                    IsEnabled = schedule.IsEnabled ? 1 : 0,
                    CreatedAt = schedule.CreatedAt.ToString("o")
                });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                INSERT INTO queue_schedules (id, job_type, payload, queue, cron_expression, next_run_at, is_enabled, created_at)
                VALUES (@Id, @JobType, @Payload, @Queue, @CronExpression, @NextRunAt, @IsEnabled, @CreatedAt)";

            await conn.ExecuteAsync(sql, new
            {
                schedule.Id,
                schedule.JobType,
                schedule.Payload,
                schedule.Queue,
                schedule.CronExpression,
                schedule.NextRunAt,
                schedule.IsEnabled,
                schedule.CreatedAt
            });
        }
    }

    public async Task UpdateScheduleAsync(JobSchedule schedule)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(@"
                    UPDATE queue_schedules 
                    SET next_run_at = @NextRunAt, last_run_at = @LastRunAt, is_enabled = @IsEnabled
                    WHERE id = @Id",
                    new
                    {
                        schedule.Id,
                        NextRunAt = schedule.NextRunAt?.ToString("o"),
                        LastRunAt = schedule.LastRunAt?.ToString("o"),
                        IsEnabled = schedule.IsEnabled ? 1 : 0
                    });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                UPDATE queue_schedules 
                SET next_run_at = @NextRunAt, last_run_at = @LastRunAt, is_enabled = @IsEnabled
                WHERE id = @Id",
                schedule);
        }
    }

    public async Task DeleteScheduleAsync(string scheduleId)
    {
        if (_provider == "sqlite")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync("DELETE FROM queue_schedules WHERE id = @Id", 
                    new { Id = scheduleId });
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync("DELETE FROM queue_schedules WHERE id = @Id", 
                new { Id = scheduleId });
        }
    }

    public async Task<IEnumerable<JobSchedule>> GetSchedulesAsync(string? queue = null)
    {
        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var sql = "SELECT * FROM queue_schedules";
                if (!string.IsNullOrEmpty(queue))
                    sql += " WHERE queue = @Queue";

                var results = await conn.QueryAsync<dynamic>(sql, new { Queue = queue });

                return results.Select(MapToScheduleSqlite).ToList();
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = "SELECT * FROM queue_schedules";
            if (!string.IsNullOrEmpty(queue))
                sql += " WHERE queue = @Queue";

            var results = await conn.QueryAsync<dynamic>(sql, new { Queue = queue });

            return results.Select(MapToSchedulePostgres).ToList();
        }
    }

    public async Task<IEnumerable<JobSchedule>> GetDueSchedulesAsync()
    {
        var now = DateTime.UtcNow.ToString("o");

        if (_provider == "sqlite")
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                var results = await conn.QueryAsync<dynamic>(@"
                    SELECT * FROM queue_schedules 
                    WHERE is_enabled = 1 AND next_run_at IS NOT NULL AND next_run_at <= @Now",
                    new { Now = now });

                return results.Select(MapToScheduleSqlite).ToList();
            });
        }
        else
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var results = await conn.QueryAsync<dynamic>(@"
                SELECT * FROM queue_schedules 
                WHERE is_enabled = true AND next_run_at IS NOT NULL AND next_run_at <= @Now",
                new { Now = DateTime.UtcNow });

            return results.Select(MapToSchedulePostgres).ToList();
        }
    }

    private static JobSchedule MapToScheduleSqlite(dynamic row)
    {
        return new JobSchedule
        {
            Id = (string)row.id,
            JobType = (string)row.job_type,
            Payload = (string)row.payload,
            Queue = (string)row.queue,
            CronExpression = (string)row.cron_expression,
            NextRunAt = row.next_run_at != null ? DateTime.Parse((string)row.next_run_at) : null,
            LastRunAt = row.last_run_at != null ? DateTime.Parse((string)row.last_run_at) : null,
            IsEnabled = row.is_enabled == 1,
            CreatedAt = DateTime.Parse((string)row.created_at)
        };
    }

    private static JobSchedule MapToSchedulePostgres(dynamic row)
    {
        return new JobSchedule
        {
            Id = (string)row.id,
            JobType = (string)row.job_type,
            Payload = (string)row.payload,
            Queue = (string)row.queue,
            CronExpression = (string)row.cron_expression,
            NextRunAt = row.next_run_at,
            LastRunAt = row.last_run_at,
            IsEnabled = (bool)row.is_enabled,
            CreatedAt = (DateTime)row.created_at
        };
    }
}
