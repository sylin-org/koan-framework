namespace Koan.Scheduling;

public interface IScheduledTask
{
    string Id { get; }
    Task RunAsync(CancellationToken ct);
}