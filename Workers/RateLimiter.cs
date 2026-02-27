namespace QueueEngine.Workers;

public class RateLimiter
{
    private readonly int _maxPerSecond;
    private readonly Queue<DateTime> _requests = new();
    private readonly object _lock = new();

    public RateLimiter(int maxPerSecond)
    {
        _maxPerSecond = maxPerSecond;
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            while (_requests.Count > 0 && _requests.Peek() < now.AddSeconds(-1))
            {
                _requests.Dequeue();
            }

            if (_requests.Count >= _maxPerSecond)
            {
                var waitTime = _requests.Peek() - now.AddSeconds(-1);
                if (waitTime > TimeSpan.Zero)
                {
                    Monitor.Exit(_lock);
                    try
                    {
                        Task.Delay(waitTime, ct).Wait(ct);
                    }
                    finally
                    {
                        Monitor.Enter(_lock);
                    }
                }
            }

            _requests.Enqueue(now);
        }
    }
}
