using QueueEngine.Models;
using QueueEngine.Workers;

public record EmailPayload(string To, string Subject, string Body);

public class EmailJobHandler : JobHandler<EmailPayload>
{
    public override string JobType => "send-email";

    protected override async Task HandleAsync(QueueJob job, EmailPayload payload, CancellationToken ct)
    {
        Console.WriteLine($"  ✉  Sending email to {payload.To} — '{payload.Subject}'");
        await Task.Delay(500, ct);
        Console.WriteLine($"  ✓  Email sent to {payload.To}");
    }
}

public record NotificationPayload(string UserId, string Message);

public class NotificationJobHandler : JobHandler<NotificationPayload>
{
    public override string JobType => "notify-user";

    protected override async Task HandleAsync(QueueJob job, NotificationPayload payload, CancellationToken ct)
    {
        Console.WriteLine($"  🔔  Notifying user {payload.UserId}: {payload.Message}");
        await Task.Delay(300, ct);
    }
}
