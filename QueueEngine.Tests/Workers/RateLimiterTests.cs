using QueueEngine.Workers;
using Xunit;

namespace QueueEngine.Tests.Workers;

public class RateLimiterTests
{
    [Fact]
    public async Task WaitAsync_WithRateLimit_ShouldAllowRequests()
    {
        var limiter = new RateLimiter(10);
        var cts = new CancellationTokenSource();

        var start = DateTime.UtcNow;
        
        for (int i = 0; i < 3; i++)
        {
            await limiter.WaitAsync(cts.Token);
        }

        var elapsed = DateTime.UtcNow - start;
        Assert.True(elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitAsync_WhenCancelled_ShouldThrow()
    {
        var limiter = new RateLimiter(10);
        var cts = new CancellationTokenSource();
        
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            limiter.WaitAsync(cts.Token));
    }
}
