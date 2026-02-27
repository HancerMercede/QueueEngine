using QueueEngine.Models;
using Xunit;

namespace QueueEngine.Tests.Models;

public class QueueJobTests
{
    [Fact]
    public void QueueJob_DefaultValues()
    {
        var job = new QueueJob();

        Assert.Equal(Guid.Empty, job.Id);
        Assert.Equal("default", job.Queue);
        Assert.Equal("{}", job.Payload);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.RetryCount);
    }

    [Fact]
    public void JobStatus_HasAllValues()
    {
        var statuses = Enum.GetValues<JobStatus>();

        Assert.Contains(JobStatus.Pending, statuses);
        Assert.Contains(JobStatus.Running, statuses);
        Assert.Contains(JobStatus.Done, statuses);
        Assert.Contains(JobStatus.Failed, statuses);
        Assert.Contains(JobStatus.Cancelled, statuses);
    }
}
