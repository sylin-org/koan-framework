namespace Koan.Scheduling;

public interface IFixedDelay
{
    TimeSpan Delay { get; }
}