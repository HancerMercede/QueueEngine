using QueueEngine.Models;
using QueueEngine.Workers;

namespace QueueEngine.Tests.Handlers;

public class JobHandlerTests
{
    [Fact]
    public void JobHandler_ShouldHaveJobType()
    {
        var handler = new TestEmailHandler();
        
        Assert.Equal("send-email", handler.JobType);
    }

    private class TestEmailHandler : JobHandler<TestEmailPayload>
    {
        public override string JobType => "send-email";

        protected override Task HandleAsync(QueueJob job, TestEmailPayload payload, IProgress<int>? progress, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private record TestEmailPayload(string To, string Subject, string Body);
}

public class HandlerRegistrationTests
{
    [Fact]
    public void MultipleHandlers_DifferentTypes_ShouldWork()
    {
        var handlers = new Dictionary<string, IJobHandler>
        {
            ["job1"] = new TestHandler1(),
            ["job2"] = new TestHandler2(),
            ["job3"] = new TestHandler3()
        };

        Assert.Equal(3, handlers.Count);
        Assert.Contains("job1", handlers.Keys);
        Assert.Contains("job2", handlers.Keys);
        Assert.Contains("job3", handlers.Keys);
    }

    private class TestHandler1 : JobHandler<TestPayload> { public override string JobType => "job1"; protected override Task HandleAsync(QueueJob j, TestPayload p, IProgress<int>? p3, CancellationToken ct) => Task.CompletedTask; }
    private class TestHandler2 : JobHandler<TestPayload> { public override string JobType => "job2"; protected override Task HandleAsync(QueueJob j, TestPayload p, IProgress<int>? p3, CancellationToken ct) => Task.CompletedTask; }
    private class TestHandler3 : JobHandler<TestPayload> { public override string JobType => "job3"; protected override Task HandleAsync(QueueJob j, TestPayload p, IProgress<int>? p3, CancellationToken ct) => Task.CompletedTask; }
    private record TestPayload(string Data);
}
