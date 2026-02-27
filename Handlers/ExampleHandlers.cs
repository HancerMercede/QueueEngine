using QueueEngine.Models;
using QueueEngine.Workers;

public record EmailPayload(string To, string Subject, string Body);

public class EmailJobHandler : JobHandler<EmailPayload>
{
    public override string JobType => "send-email";

    protected override async Task HandleAsync(QueueJob job, EmailPayload payload, IProgress<int>? progress, CancellationToken ct)
    {
        Console.WriteLine($"  ✉  Sending email to {payload.To} — '{payload.Subject}'");
        progress?.Report(25);
        await Task.Delay(200, ct);
        progress?.Report(50);
        await Task.Delay(200, ct);
        progress?.Report(75);
        await Task.Delay(100, ct);
        progress?.Report(100);
        Console.WriteLine($"  ✓  Email sent to {payload.To}");
    }
}

public record NotificationPayload(string UserId, string Message);

public class NotificationJobHandler : JobHandler<NotificationPayload>
{
    public override string JobType => "notify-user";

    protected override async Task HandleAsync(QueueJob job, NotificationPayload payload, IProgress<int>? progress, CancellationToken ct)
    {
        Console.WriteLine($"  🔔  Notifying user {payload.UserId}: {payload.Message}");
        progress?.Report(50);
        await Task.Delay(150, ct);
        progress?.Report(100);
    }
}
