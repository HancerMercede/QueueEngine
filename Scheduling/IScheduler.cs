namespace QueueEngine.Scheduling;

public interface IScheduler
{
    Task ScheduleAsync(string jobType, object payload, string cronExpression, string queue = "default");
    Task UnscheduleAsync(string scheduleId);
    Task<IEnumerable<JobSchedule>> GetSchedulesAsync(string? queue = null);
    void Start();
    void Stop();
}

public class JobSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string JobType { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Queue { get; set; } = "default";
    public string CronExpression { get; set; } = string.Empty;
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CronHelper
{
    public static DateTime? GetNextRun(string cronExpression)
    {
        try
        {
            var parts = cronExpression.Split(' ');
            if (parts.Length < 5 || parts.Length > 6)
                return null;

            var now = DateTime.UtcNow;
            var next = now.AddSeconds(10);

            for (int i = 0; i < 1000 && next.Year < now.Year + 2; i++)
            {
                if (MatchesCron(next, parts))
                    return next;
                next = next.AddMinutes(1);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesCron(DateTime dt, string[] parts)
    {
        return MatchesField(parts[0], dt.Minute, 0, 59) &&
               MatchesField(parts[1], dt.Hour, 0, 23) &&
               MatchesField(parts[2], dt.Day, 1, 31) &&
               MatchesField(parts[3], dt.Month, 1, 12) &&
               MatchesField(parts[4], (int)dt.DayOfWeek, 0, 6);
    }

    private static bool MatchesField(string field, int value, int min, int max)
    {
        if (field == "*") return true;

        if (field.Contains('/'))
        {
            var stepParts = field.Split('/');
            var step = int.Parse(stepParts[1]);
            return (value - min) % step == 0;
        }

        if (field.Contains('-'))
        {
            var range = field.Split('-');
            var start = int.Parse(range[0]);
            var end = int.Parse(range[1]);
            return value >= start && value <= end;
        }

        if (field.Contains(','))
        {
            var values = field.Split(',').Select(int.Parse);
            return values.Contains(value);
        }

        return int.TryParse(field, out var exact) && exact == value;
    }

    public static bool IsValid(string cronExpression)
    {
        return GetNextRun(cronExpression) != null;
    }
}
