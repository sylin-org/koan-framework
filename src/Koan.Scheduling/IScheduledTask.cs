namespace Koan.Scheduling;

public interface IScheduledTask
{
    string Id { get; }
    Task Run(CancellationToken ct);
}