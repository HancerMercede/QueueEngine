using Dapper;
using Microsoft.Data.Sqlite;
using Npgsql;
using QueueEngine.Config;
using QueueEngine.Models;

namespace QueueEngine.Data;

public class JobRepository : IJobRepository
{
    private readonly string _connectionString;
    private readonly string _provider;
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
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT,
                    scheduled_at TEXT,
                    created_at TEXT NOT NULL,
                    started_at TEXT,
                    completed_at TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_queue_status ON queue_jobs(queue, status);
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
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT,
                    scheduled_at TIMESTAMP,
                    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                    started_at TIMESTAMP,
                    completed_at TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_queue_status ON queue_jobs(queue, status);
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
                    INSERT INTO queue_jobs (id, job_type, queue, payload, status, retry_count, scheduled_at, created_at)
                    VALUES (@Id, @JobType, @Queue, @Payload, @Status, @RetryCount, @ScheduledAt, @CreatedAt)";

                await conn.ExecuteAsync(sql, new
                {
                    Id = job.Id.ToString(),
                    job.JobType,
                    job.Queue,
                    job.Payload,
                    Status = (int)job.Status,
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
                INSERT INTO queue_jobs (id, job_type, queue, payload, status, retry_count, scheduled_at, created_at)
                VALUES (@Id, @JobType, @Queue, @Payload, @Status, @RetryCount, @ScheduledAt, @CreatedAt)";

            await conn.ExecuteAsync(sql, new
            {
                Id = job.Id,
                job.JobType,
                job.Queue,
                job.Payload,
                Status = (int)job.Status,
                job.RetryCount,
                ScheduledAt = job.ScheduledAt,
                CreatedAt = job.CreatedAt
            });

            return job.Id;
        }
    }

    public async Task<QueueJob?> DequeueAsync(string queue)
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
                    SET status = @RunningStatus, started_at = @Now
                    WHERE id = (
                        SELECT id FROM queue_jobs 
                        WHERE queue = @Queue 
                        AND status = @PendingStatus 
                        AND (scheduled_at IS NULL OR scheduled_at <= @Now)
                        ORDER BY created_at ASC 
                        LIMIT 1
                    )
                    RETURNING *", new { 
                    Queue = queue, 
                    PendingStatus = (int)JobStatus.Pending,
                    RunningStatus = (int)JobStatus.Running,
                    Now = now
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
                SET status = @RunningStatus, started_at = @Now
                WHERE id = (
                    SELECT id FROM queue_jobs 
                    WHERE queue = @Queue 
                    AND status = @PendingStatus 
                    AND (scheduled_at IS NULL OR scheduled_at <= @Now)
                    ORDER BY created_at ASC 
                    LIMIT 1
                )
                RETURNING id, job_type, queue, payload, status, retry_count, error_message, scheduled_at, created_at, started_at, completed_at", new { 
                Queue = queue, 
                PendingStatus = (int)JobStatus.Pending,
                RunningStatus = (int)JobStatus.Running,
                Now = now
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

    public async Task<(int Pending, int Running, int Done, int Failed)> GetStatsAsync(string queue)
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
                        SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END) as failed
                    FROM queue_jobs 
                    WHERE queue = @Queue", new { Queue = queue });

                return (
                    (int)(stats?.pending ?? 0),
                    (int)(stats?.running ?? 0),
                    (int)(stats?.done ?? 0),
                    (int)(stats?.failed ?? 0)
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
                    SUM(CASE WHEN status = 3 THEN 1 ELSE 0 END) as failed
                FROM queue_jobs 
                WHERE queue = @Queue", new { Queue = queue });

            return (
                (int)(stats?.pending ?? 0),
                (int)(stats?.running ?? 0),
                (int)(stats?.done ?? 0),
                (int)(stats?.failed ?? 0)
            );
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
            RetryCount = (int)row.retry_count,
            ErrorMessage = row.error_message,
            ScheduledAt = row.scheduled_at != null ? DateTime.Parse((string)row.scheduled_at) : null,
            CreatedAt = DateTime.Parse((string)row.created_at),
            StartedAt = row.started_at != null ? DateTime.Parse((string)row.started_at) : null,
            CompletedAt = row.completed_at != null ? DateTime.Parse((string)row.completed_at) : null
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
            RetryCount = (int)row.retry_count,
            ErrorMessage = row.error_message,
            ScheduledAt = row.scheduled_at,
            CreatedAt = (DateTime)row.created_at,
            StartedAt = row.started_at,
            CompletedAt = row.completed_at
        };
    }
}
