using Microsoft.Extensions.Logging;
using Moq;
using QueueEngine.Config;
using QueueEngine.Data;
using QueueEngine.Scheduling;

namespace QueueEngine.Tests.Scheduling;

public class SchedulerTests
{
    [Fact]
    public void Constructor_ShouldInitialize()
    {
        var mockRepo = new Mock<IJobRepository>();
        var mockLogger = new Mock<ILogger<Scheduler>>();
        var options = new QueueEngineOptions { Scheduler = new SchedulerOptions { Enabled = true } };

        var scheduler = new Scheduler(mockRepo.Object, options, mockLogger.Object);
        
        Assert.NotNull(scheduler);
    }

    [Fact]
    public void Stop_ShouldNotThrow()
    {
        var mockRepo = new Mock<IJobRepository>();
        var mockLogger = new Mock<ILogger<Scheduler>>();
        var options = new QueueEngineOptions { Scheduler = new SchedulerOptions { Enabled = true } };

        var scheduler = new Scheduler(mockRepo.Object, options, mockLogger.Object);
        
        scheduler.Stop();
    }
}

public class CronHelperTests
{
    [Theory]
    [InlineData("* * * * *", true)]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 * * * *", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public void IsValid_ShouldReturnCorrectResult(string expression, bool expected)
    {
        var result = CronHelper.IsValid(expression);
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetNextRun_WithValidCron_ShouldReturnNextOccurrence()
    {
        var result = CronHelper.GetNextRun("* * * * *");
        
        Assert.NotNull(result);
        Assert.True(result > DateTime.UtcNow);
    }

    [Fact]
    public void GetNextRun_WithInvalidCron_ShouldReturnNull()
    {
        var result = CronHelper.GetNextRun("invalid");
        
        Assert.Null(result);
    }
}

public class JobScheduleTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var schedule = new JobSchedule();
        
        Assert.NotNull(schedule.Id);
        Assert.Equal("default", schedule.Queue);
        Assert.True(schedule.IsEnabled);
        Assert.NotNull(schedule.CreatedAt);
    }

    [Fact]
    public void Constructor_WithCustomValues_ShouldSetValues()
    {
        var schedule = new JobSchedule
        {
            Id = "custom-id",
            JobType = "my-job",
            Payload = "{\"test\":true}",
            Queue = "priority",
            CronExpression = "*/5 * * * *",
            IsEnabled = false
        };
        
        Assert.Equal("custom-id", schedule.Id);
        Assert.Equal("my-job", schedule.JobType);
        Assert.Equal("priority", schedule.Queue);
        Assert.Equal("*/5 * * * *", schedule.CronExpression);
        Assert.False(schedule.IsEnabled);
    }
}
