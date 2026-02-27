namespace QueueEngine.Workers;

public class RateLimiter(int maxPerSecond)
{
    private readonly Queue<DateTime> _requests = new();
    private readonly Lock _lock = new();

    public async Task WaitAsync(CancellationToken ct)
    {
        TimeSpan waitTime;
        
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            while (_requests.Count > 0 && _requests.Peek() < now.AddSeconds(-1))
            {
                _requests.Dequeue();
            }

            if (_requests.Count >= maxPerSecond)
            {
                waitTime = _requests.Peek() - now.AddSeconds(-1);
            }
            else
            {
                waitTime = TimeSpan.Zero;
            }
        }

        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime, ct);
        }

        lock (_lock)
        {
            _requests.Enqueue(DateTime.UtcNow);
        }
    }
}
